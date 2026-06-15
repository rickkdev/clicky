"""deterministic scoped autopilot session policy.

This module coordinates already-proposed actions inside a bounded task scope. It
never captures the screen, prompts users, or executes desktop actions.
"""

from __future__ import annotations

from collections.abc import MutableSequence, Mapping, Sequence
from datetime import datetime, timezone, timedelta
from typing import Any

from .permission_policy import evaluate_permission_for_action
from .safety_policy import evaluate_safety_policy

PROTOCOL_VERSION = "clicky.hermes.v1"
_TERMINAL_VERIFICATION_STATUSES = {"failed", "uncertain", "blockedByPrompt"}


class ScopedAutopilotSession:
    """Bounded low-risk automation session with deterministic policy gates."""

    def __init__(
        self,
        *,
        session_id: str,
        task_description: str,
        allowed_applications: Sequence[str] | None,
        allowed_action_types: Sequence[str],
        timeout_seconds: int,
        max_steps: int,
        started_at: str | datetime | None = None,
        allowed_windows: Sequence[str] | None = None,
        audit_log: MutableSequence[dict[str, Any]] | None = None,
    ) -> None:
        self.session_id = str(session_id)
        self.task_description = str(task_description).strip()
        self.allowed_applications = {str(item).lower() for item in (allowed_applications or [])}
        self.allowed_windows = {str(item).lower() for item in (allowed_windows or [])}
        self.allowed_action_types = {str(item) for item in allowed_action_types}
        self.timeout_seconds = max(0, int(timeout_seconds))
        self.max_steps = max(0, int(max_steps))
        self.started_at = _coerce_time(started_at)
        self.audit_log = audit_log
        self.step_count = 0
        self.stop_reason: str | None = None

    @property
    def is_stopped(self) -> bool:
        return self.stop_reason is not None

    def evaluate_next_action(self, proposal: Mapping[str, Any], *, now: str | datetime | None = None) -> dict[str, Any]:
        """Evaluate one proposed action against scope, permission, and safety gates."""
        current_time = _coerce_time(now)
        proposal_id = str(proposal.get("id", ""))
        action_type = str(proposal.get("actionType", ""))

        if self.is_stopped:
            return self._stopped_result(proposal, "session_already_stopped", "session is already stopped", current_time)
        if current_time - self.started_at > timedelta(seconds=self.timeout_seconds):
            return self._stop(proposal, "timeout", "autopilot session timed out", current_time)
        if self.step_count >= self.max_steps:
            return self._stop(proposal, "max_steps", "autopilot session reached maximum step count", current_time)

        scope_reason = self._scope_violation_reason(proposal)
        if scope_reason:
            return self._stop(proposal, "scope_violation", scope_reason, current_time)

        if action_type not in self.allowed_action_types:
            return self._stop(proposal, "scope_violation", f"action type {action_type} is outside the session scope", current_time)

        permission_decision = evaluate_permission_for_action("scopedAutopilot", proposal)
        safety_decision = evaluate_safety_policy(proposal)
        gate_decision = _combined_gate_decision(permission_decision, safety_decision)
        self.step_count += 1

        if gate_decision == "block":
            return self._stop(
                proposal,
                "blocked_action",
                _decision_reason(permission_decision, safety_decision),
                current_time,
                permission_decision=permission_decision,
                safety_decision=safety_decision,
            )

        status = "requiresConfirmation" if gate_decision == "requireConfirmation" else "allowed"
        result = self._base_result(proposal, current_time)
        result.update(
            {
                "status": status,
                "stepIndex": self.step_count,
                "reason": _decision_reason(permission_decision, safety_decision),
                "permissionDecision": permission_decision,
                "safetyDecision": safety_decision,
                "forwardToExecutor": status == "allowed",
            }
        )
        self._audit("autopilot_step", result, proposal_id=proposal_id, action_type=action_type)
        return result

    def record_verification_result(self, verification_result: Mapping[str, Any], *, now: str | datetime | None = None) -> dict[str, Any]:
        """Stop the session when post-action verification says automation must stop."""
        current_time = _coerce_time(now)
        status = str(verification_result.get("status", ""))
        if status in _TERMINAL_VERIFICATION_STATUSES or verification_result.get("continueAutomation") is False:
            reason = "failed_verification" if status == "failed" else status or "verification_stopped"
            return self._stop({}, reason, str(verification_result.get("reason", reason)), current_time)

        result = {
            "protocolVersion": PROTOCOL_VERSION,
            "ok": True,
            "sessionId": self.session_id,
            "status": "continuing",
            "verificationStatus": status,
            "continueAutomation": True,
            "forwardToExecutor": False,
            "timestamp": _format_time(current_time),
        }
        self._audit("autopilot_verification", result)
        return result

    def cancel(self, *, reason: str = "user_cancel", now: str | datetime | None = None) -> dict[str, Any]:
        return self._stop({}, reason or "user_cancel", "autopilot session cancelled by user", _coerce_time(now))

    def _scope_violation_reason(self, proposal: Mapping[str, Any]) -> str | None:
        app = str(proposal.get("application", "")).strip().lower()
        window = str(proposal.get("windowTitle", "")).strip().lower()
        if self.allowed_applications and app not in self.allowed_applications:
            return f"application {proposal.get('application', '')} is outside the session scope"
        if self.allowed_windows and window not in self.allowed_windows:
            return f"window {proposal.get('windowTitle', '')} is outside the session scope"
        return None

    def _stop(
        self,
        proposal: Mapping[str, Any],
        stop_reason: str,
        reason: str,
        current_time: datetime,
        *,
        permission_decision: Mapping[str, Any] | None = None,
        safety_decision: Mapping[str, Any] | None = None,
    ) -> dict[str, Any]:
        self.stop_reason = stop_reason
        result = self._stopped_result(proposal, stop_reason, reason, current_time)
        if permission_decision is not None:
            result["permissionDecision"] = dict(permission_decision)
        if safety_decision is not None:
            result["safetyDecision"] = dict(safety_decision)
        self._audit("autopilot_stopped", result, proposal_id=str(proposal.get("id", "")), action_type=str(proposal.get("actionType", "")))
        return result

    def _stopped_result(self, proposal: Mapping[str, Any], stop_reason: str, reason: str, current_time: datetime) -> dict[str, Any]:
        result = self._base_result(proposal, current_time)
        result.update(
            {
                "status": "stopped",
                "stopReason": stop_reason,
                "reason": reason,
                "stepIndex": self.step_count,
                "forwardToExecutor": False,
            }
        )
        return result

    def _base_result(self, proposal: Mapping[str, Any], current_time: datetime) -> dict[str, Any]:
        return {
            "protocolVersion": PROTOCOL_VERSION,
            "ok": True,
            "sessionId": self.session_id,
            "taskDescription": self.task_description,
            "proposalId": proposal.get("id"),
            "actionType": proposal.get("actionType"),
            "timestamp": _format_time(current_time),
        }

    def _audit(self, event: str, result: Mapping[str, Any], *, proposal_id: str = "", action_type: str = "") -> None:
        if self.audit_log is None:
            return
        self.audit_log.append(
            {
                "event": event,
                "sessionId": self.session_id,
                "proposalId": proposal_id or str(result.get("proposalId", "")),
                "actionType": action_type or str(result.get("actionType", "")),
                "status": result.get("status"),
                "stopReason": result.get("stopReason"),
                "stepIndex": result.get("stepIndex"),
                "forwardToExecutor": result.get("forwardToExecutor", False),
            }
        )


def _combined_gate_decision(permission_decision: Mapping[str, Any], safety_decision: Mapping[str, Any]) -> str:
    decisions = {str(permission_decision.get("decision", "")), str(safety_decision.get("decision", ""))}
    if "block" in decisions:
        return "block"
    if "requireConfirmation" in decisions:
        return "requireConfirmation"
    return "allow"


def _decision_reason(permission_decision: Mapping[str, Any], safety_decision: Mapping[str, Any]) -> str:
    return f"permission: {permission_decision.get('reason', '')}; safety: {safety_decision.get('reason', '')}"


def _coerce_time(value: str | datetime | None) -> datetime:
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
