"""deterministic post-action screen verification for gated desktop automation.

This layer consumes an already-completed execution result and a fresh observation
summary from a fake or native observe seam. It never captures the screen or
executes desktop actions by itself.
"""

from __future__ import annotations

from collections.abc import Callable, Mapping
from datetime import datetime, timezone
from typing import Any, Optional

PROTOCOL_VERSION = "clicky.hermes.v1"
TERMINAL_STOP_STATUSES = {"failed", "uncertain", "blockedByPrompt"}

ObservationProvider = Callable[[Mapping[str, Any], Optional[str]], Mapping[str, Any]]


class PostActionVerificationRunner:
    """Run one post-action observation unless the execution result opted out."""

    def __init__(self, observe_after_action: ObservationProvider):
        self._observe_after_action = observe_after_action

    def verify_after_execution(
        self,
        execution_result: Mapping[str, Any],
        *,
        expected_state: str | None = None,
        request_id: str | None = None,
        timestamp: str | datetime | None = None,
    ) -> dict[str, Any]:
        if not _verification_required(execution_result):
            return _verification_result(
                execution_result,
                status="skipped",
                reason="verification_not_required",
                request_id=request_id,
                expected_state=expected_state,
                observation=None,
                observation_ran=False,
                timestamp=timestamp,
            )

        observation = self._observe_after_action(execution_result, expected_state)
        return verify_post_action_observation(
            execution_result,
            observation,
            expected_state=expected_state,
            request_id=request_id,
            timestamp=timestamp,
        )


def verify_post_action_observation(
    execution_result: Mapping[str, Any],
    observation: Mapping[str, Any],
    *,
    expected_state: str | None = None,
    request_id: str | None = None,
    timestamp: str | datetime | None = None,
) -> dict[str, Any]:
    """Classify a post-action observation using provider-neutral flags."""
    if execution_result.get("status") != "executed" or not execution_result.get("ok", False):
        return _verification_result(
            execution_result,
            status="skipped",
            reason="action_not_executed",
            request_id=request_id,
            expected_state=expected_state,
            observation=observation,
            observation_ran=False,
            timestamp=timestamp,
        )

    status, reason = _classify_observation(observation)
    return _verification_result(
        execution_result,
        status=status,
        reason=reason,
        request_id=request_id,
        expected_state=expected_state,
        observation=observation,
        observation_ran=True,
        timestamp=timestamp,
    )


def _classify_observation(observation: Mapping[str, Any]) -> tuple[str, str]:
    if observation.get("permissionPromptVisible"):
        return "blockedByPrompt", "permission_prompt"
    if observation.get("errorDialogVisible"):
        return "failed", "error_dialog"
    if observation.get("targetStillUnchanged"):
        return "failed", "target_unchanged"
    if observation.get("appLostFocus"):
        return "failed", "app_lost_focus"
    if observation.get("matchedExpectedState"):
        return "success", "expected_state_observed"
    if observation.get("screenChanged") is False:
        return "failed", "screen_unchanged"
    return "uncertain", "changed_but_unverified"


def _verification_required(execution_result: Mapping[str, Any]) -> bool:
    result = execution_result.get("result")
    if isinstance(result, Mapping) and result.get("verificationRequired") is False:
        return False
    return execution_result.get("status") == "executed" and bool(execution_result.get("ok", False))


def _verification_result(
    execution_result: Mapping[str, Any],
    *,
    status: str,
    reason: str,
    request_id: str | None,
    expected_state: str | None,
    observation: Mapping[str, Any] | None,
    observation_ran: bool,
    timestamp: str | datetime | None,
) -> dict[str, Any]:
    continue_automation = status not in TERMINAL_STOP_STATUSES
    result: dict[str, Any] = {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "requestId": request_id,
        "proposalId": execution_result.get("proposalId"),
        "actionType": execution_result.get("actionType"),
        "status": status,
        "reason": reason,
        "expectedState": expected_state,
        "observationRan": observation_ran,
        "continueAutomation": continue_automation,
        "requiresUserGuidance": status == "uncertain",
        "verifiedAt": _timestamp(timestamp),
    }
    if observation is not None:
        result["observationSummary"] = observation.get("summary") or observation.get("explanation")
    return result


def _timestamp(value: str | datetime | None) -> str:
    if isinstance(value, str):
        return value
    if isinstance(value, datetime):
        dt = value.astimezone(timezone.utc) if value.tzinfo else value.replace(tzinfo=timezone.utc)
    else:
        dt = datetime.now(timezone.utc)
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")
