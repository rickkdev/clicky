"""Hermes-facing confirmation request/response state for inert action proposals.

This module models confirmation UX data only. It never opens dialogs and never
executes desktop actions.
"""

from __future__ import annotations

from collections.abc import Mapping, MutableSequence
from datetime import datetime, timedelta, timezone
from typing import Any
import hashlib
import re

from .safety_policy import evaluate_safety_policy

PROTOCOL_VERSION = "clicky.hermes.v1"
DEFAULT_TTL_SECONDS = 120
_DECISION_ALIASES = {
    "approve": "approved",
    "approved": "approved",
    "cancel": "cancelled",
    "cancelled": "cancelled",
    "explain": "explanationRequested",
    "explanation": "explanationRequested",
    "explanationRequested": "explanationRequested",
}
_ACTION_VERBS = {
    "click": "click",
    "doubleClick": "double click",
    "typeText": "type text",
    "hotkey": "press hotkey",
    "openApplication": "open application",
    "focusWindow": "focus window",
    "waitForScreenChange": "wait for screen change",
    "stop": "stop",
}
_SENSITIVE_PATTERNS = [
    re.compile(r"password\s*[:=]", re.IGNORECASE),
    re.compile(r"passcode\s*[:=]", re.IGNORECASE),
    re.compile(r"token\s*[:=]", re.IGNORECASE),
    re.compile(r"api[_ -]?key\s*[:=]", re.IGNORECASE),
    re.compile(r"secret", re.IGNORECASE),
    re.compile(r"chain-of-thought", re.IGNORECASE),
    re.compile(r"internal reasoning", re.IGNORECASE),
]


def build_confirmation_request(
    proposal: Mapping[str, Any],
    safety_decision: Mapping[str, Any] | None = None,
    *,
    now: str | datetime | None = None,
    ttl_seconds: int = DEFAULT_TTL_SECONDS,
) -> dict[str, Any]:
    """Build a concise structured confirmation request for Hermes.

    The request intentionally omits raw `inputPreview` and proposal rationale.
    """

    current = _parse_time(now)
    proposal_id = str(proposal.get("id", ""))
    action_type = str(proposal.get("actionType", ""))
    target = _safe_text(str(proposal.get("targetLabel", "target")), fallback="target")
    risk = str(proposal.get("riskLevel", "medium")) or "medium"
    expires_at = current + timedelta(seconds=max(1, int(ttl_seconds)))
    reason_source = "confirmation required"
    if safety_decision:
        reason_source = str(safety_decision.get("reason") or reason_source)

    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "confirmation_required",
        "confirmationId": _confirmation_id(proposal_id, current),
        "proposalId": proposal_id,
        "actionSummary": _action_summary(action_type, target),
        "target": target,
        "risk": risk if risk in {"low", "medium", "high", "blocked"} else "medium",
        "reason": _concise_reason(reason_source),
        "expiresAt": _format_time(expires_at),
    }


def handle_confirmation_response(
    confirmation_request: Mapping[str, Any],
    proposal: Mapping[str, Any],
    *,
    user_decision: str,
    now: str | datetime | None = None,
    audit_log: MutableSequence[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    """Handle a user's confirmation response as inert state.

    Approval performs a fresh safety-policy recheck and only forwards when that
    recheck allows. No executor is called.
    """

    current = _parse_time(now)
    base = {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "confirmation_resolved",
        "confirmationId": str(confirmation_request.get("confirmationId", "")),
        "proposalId": str(confirmation_request.get("proposalId", "")),
    }

    stale_reason = _stale_reason(confirmation_request, proposal, current)
    if stale_reason:
        event = _audit_event("confirmation_stale", confirmation_request, proposal, "stale", current, reason=stale_reason)
        _append(audit_log, event)
        return {
            **base,
            "decision": "stale",
            "reason": stale_reason,
            "forwardToExecutor": False,
            "auditEvent": event,
        }

    decision = _DECISION_ALIASES.get(str(user_decision), "cancelled")
    if decision == "cancelled":
        event = _audit_event("confirmation_cancelled", confirmation_request, proposal, "cancelled", current)
        _append(audit_log, event)
        return {
            **base,
            "decision": "cancelled",
            "forwardToExecutor": False,
            "auditEvent": event,
        }

    if decision == "explanationRequested":
        event = _audit_event("confirmation_explanation_requested", confirmation_request, proposal, "explanationRequested", current)
        _append(audit_log, event)
        return {
            **base,
            "decision": "explanationRequested",
            "needsExplanation": True,
            "forwardToExecutor": False,
            "auditEvent": event,
        }

    safety_decision = evaluate_safety_policy(proposal)
    forward = bool(safety_decision.get("forwardToExecutor")) and safety_decision.get("decision") == "allow"
    event = _audit_event("confirmation_approved", confirmation_request, proposal, "approved", current)
    event["safetyDecision"] = safety_decision.get("decision")
    event["forwardToExecutor"] = forward
    _append(audit_log, event)
    return {
        **base,
        "decision": "approved",
        "safetyRechecked": True,
        "safetyDecision": safety_decision,
        "forwardToExecutor": forward,
        "auditEvent": event,
    }


def _stale_reason(request: Mapping[str, Any], proposal: Mapping[str, Any], current: datetime) -> str | None:
    if str(request.get("proposalId", "")) != str(proposal.get("id", "")):
        return "proposal id mismatch"
    expires_at = _parse_time(request.get("expiresAt"))
    if current > expires_at:
        return "confirmation expired"
    return None


def _audit_event(
    event: str,
    request: Mapping[str, Any],
    proposal: Mapping[str, Any],
    status: str,
    current: datetime,
    *,
    reason: str | None = None,
) -> dict[str, Any]:
    result = {
        "event": event,
        "status": status,
        "confirmationId": str(request.get("confirmationId", "")),
        "proposalId": str(request.get("proposalId", proposal.get("id", ""))),
        "actionType": str(proposal.get("actionType", "")),
        "target": _safe_text(str(proposal.get("targetLabel", "target")), fallback="target"),
        "timestamp": _format_time(current),
        "forwardToExecutor": False,
    }
    if reason:
        result["reason"] = reason
    return result


def _append(audit_log: MutableSequence[dict[str, Any]] | None, event: dict[str, Any]) -> None:
    if audit_log is not None:
        audit_log.append(event)


def _action_summary(action_type: str, target: str) -> str:
    verb = _ACTION_VERBS.get(action_type, "perform action")
    return _truncate(f"{verb} on {target}", 80)


def _concise_reason(reason: str) -> str:
    text = _safe_text(reason, fallback="confirmation required")
    text = text.replace("requires user confirmation:", "needs confirmation:")
    text = text.replace("allowed by safety policy:", "safety check:")
    text = text.replace("blocked by safety policy:", "blocked:")
    return _truncate(text, 96)


def _safe_text(value: str, *, fallback: str) -> str:
    text = " ".join(value.split())
    for pattern in _SENSITIVE_PATTERNS:
        text = pattern.sub("[redacted]", text)
    if "[redacted]" in text and fallback:
        return fallback
    return _truncate(text or fallback, 120)


def _truncate(value: str, limit: int) -> str:
    if len(value) <= limit:
        return value
    return value[: max(0, limit - 1)].rstrip() + "…"


def _confirmation_id(proposal_id: str, current: datetime) -> str:
    seed = f"{proposal_id}:{_format_time(current)}".encode("utf-8")
    return "confirm-" + hashlib.sha256(seed).hexdigest()[:16]


def _parse_time(value: str | datetime | None) -> datetime:
    if value is None:
        return datetime.now(timezone.utc)
    if isinstance(value, datetime):
        return value.astimezone(timezone.utc) if value.tzinfo else value.replace(tzinfo=timezone.utc)
    text = str(value)
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    return datetime.fromisoformat(text).astimezone(timezone.utc)


def _format_time(value: datetime) -> str:
    return value.astimezone(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
