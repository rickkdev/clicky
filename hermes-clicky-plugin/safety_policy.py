"""deterministic safety policy for inert action proposals.

This module returns structured policy data only. It never captures the screen,
launches apps, or executes desktop actions.
"""

from __future__ import annotations

from collections.abc import MutableSequence, Mapping
from typing import Any

LOW_CONFIDENCE_THRESHOLD = 0.70

BLOCKING_FLAGS = {"destructive", "payment", "purchase", "credential_entry", "permission_prompt", "blocked_risk"}
CONFIRMATION_FLAGS = {"send_message", "low_confidence"}

_KEYWORDS = {
    "destructive": [
        "delete",
        "remove",
        "destroy",
        "erase",
        "wipe",
        "format",
        "discard",
        "terminate account",
        "close account",
        "permanently",
    ],
    "payment": [
        "pay",
        "payment",
        "invoice",
        "transfer",
        "wire",
        "send money",
        "checkout payment",
    ],
    "purchase": [
        "buy",
        "purchase",
        "order now",
        "place order",
        "checkout",
        "subscribe",
        "upgrade plan",
    ],
    "send_message": [
        "send message",
        "send email",
        "email compose",
        "compose body",
        "reply",
        "post message",
        "slack message",
        "dm ",
        "tweet",
        "publish",
    ],
    "credential_entry": [
        "password",
        "passcode",
        "2fa",
        "two-factor",
        "otp",
        "one-time code",
        "api key",
        "secret key",
        "private key",
        "seed phrase",
        "recovery phrase",
        "credential",
        "token",
        "login code",
    ],
    "permission_prompt": [
        "permission",
        "allow access",
        "allow screen recording",
        "accessibility prompt",
        "system settings",
        "security & privacy",
        "privacy & security",
        "grant access",
        "authorize",
    ],
}


def evaluate_safety_policy(
    proposal: Mapping[str, Any],
    audit_log: MutableSequence[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    """Evaluate one inert action proposal and return a safety decision.

    `forwardToExecutor` is semantic only: it says whether a future executor would
    be allowed to receive the proposal after this safety layer. no executor exists here.
    """

    flags = risk_flags_for_proposal(proposal)
    decision = _decision_for_flags(flags)
    proposal_id = str(proposal.get("id", ""))
    action_type = str(proposal.get("actionType", ""))
    result = {
        "proposalId": proposal_id,
        "actionType": action_type,
        "decision": decision,
        "reason": _reason(decision, flags),
        "riskFlags": flags,
        "forwardToExecutor": decision != "block",
    }
    if audit_log is not None:
        audit_log.append(
            {
                "event": "safety_policy_decision",
                "proposalId": proposal_id,
                "actionType": action_type,
                "decision": decision,
                "status": "blocked" if decision == "block" else "accepted",
                "riskFlags": list(flags),
                "forwardToExecutor": result["forwardToExecutor"],
            }
        )
    return result


def evaluate_safety_policy_for_actions(
    proposals: list[Mapping[str, Any]],
    audit_log: MutableSequence[dict[str, Any]] | None = None,
) -> list[dict[str, Any]]:
    """Evaluate multiple proposals without executing anything."""

    return [evaluate_safety_policy(proposal, audit_log=audit_log) for proposal in proposals]


def risk_flags_for_proposal(proposal: Mapping[str, Any]) -> list[str]:
    text = _combined_text(proposal)
    flags: list[str] = []

    if str(proposal.get("riskLevel", "")).strip().lower() == "blocked":
        flags.append("blocked_risk")

    for flag, keywords in _KEYWORDS.items():
        if any(keyword in text for keyword in keywords):
            flags.append(flag)

    confidence = _confidence(proposal)
    if confidence is None or confidence < LOW_CONFIDENCE_THRESHOLD:
        flags.append("low_confidence")

    return _dedupe(flags)


def _combined_text(proposal: Mapping[str, Any]) -> str:
    parts = [
        proposal.get("actionType"),
        proposal.get("targetLabel"),
        proposal.get("rationale"),
        proposal.get("inputPreview"),
        proposal.get("application"),
        proposal.get("waitCondition"),
        proposal.get("blockedReason"),
    ]
    return " ".join(str(part) for part in parts if part is not None).lower()


def _confidence(proposal: Mapping[str, Any]) -> float | None:
    try:
        return float(proposal.get("confidence"))
    except (TypeError, ValueError):
        return None


def _dedupe(flags: list[str]) -> list[str]:
    seen = set()
    result = []
    for flag in flags:
        if flag not in seen:
            seen.add(flag)
            result.append(flag)
    return result


def _decision_for_flags(flags: list[str]) -> str:
    if any(flag in BLOCKING_FLAGS for flag in flags):
        return "block"
    if any(flag in CONFIRMATION_FLAGS for flag in flags):
        return "requireConfirmation"
    return "allow"


def _reason(decision: str, flags: list[str]) -> str:
    if decision == "block":
        return "blocked by safety policy: " + ", ".join(flags)
    if decision == "requireConfirmation":
        return "requires user confirmation: " + ", ".join(flags)
    return "allowed by safety policy: no risk flags detected"
