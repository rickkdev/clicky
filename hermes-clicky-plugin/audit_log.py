"""deterministic action audit log records for gated desktop automation.

This module writes structured JSONL only when explicitly called by a future
action pipeline. It never executes actions, prompts users, captures screens, or
launches native runtimes.
"""

from __future__ import annotations

from collections.abc import Mapping
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import json
import os
import re

PROTOCOL_VERSION = "clicky.hermes.v1"

_EVENTS = {
    "action_proposed",
    "policy_decision",
    "confirmation",
    "execution",
    "verification",
    "failure",
    "cancellation",
}

_SENSITIVE_PATTERNS = [
    re.compile(r"\b(password|passcode|token|api[_ -]?key|secret|private[_ -]?key|seed phrase|recovery phrase)\b\s*[:=]?", re.I),
    re.compile(r"\b(sk-[a-z0-9_-]{6,}|xox[baprs]-[a-z0-9-]+)\b", re.I),
]


def default_log_locations() -> dict[str, str]:
    """Return documented native default audit-log locations."""

    return {
        "windows": r"%APPDATA%\Clicky\audit.jsonl",
        "macos": "~/Library/Application Support/Clicky/audit.jsonl",
    }


def redact_sensitive_text(value: Any, *, allow_full_text: bool = False, fallback: str = "[redacted]") -> str:
    """Return safe summary text with credentials/full typed text redacted by default."""

    if value is None:
        return ""
    text = " ".join(str(value).split())
    if not text:
        return ""
    if allow_full_text:
        return _truncate(text, 160)
    if any(pattern.search(text) for pattern in _SENSITIVE_PATTERNS):
        return fallback
    return _truncate(text, 160)


class ActionAuditLog:
    """Append-only JSONL action audit log with disabled/minimized privacy modes."""

    def __init__(self, path: str | os.PathLike[str] | None = None, *, enabled: bool = True, mode: str = "standard") -> None:
        self.path = Path(path).expanduser() if path is not None else _default_current_platform_path()
        self.enabled = bool(enabled)
        self.mode = mode if mode in {"standard", "minimized"} else "standard"
        self._sequence = 0

    @staticmethod
    def build_record(
        *,
        event: str,
        request_id: str,
        user_request_summary: str | None = None,
        proposal: Mapping[str, Any] | None = None,
        action_type: str | None = None,
        target_summary: str | None = None,
        decision: str | None = None,
        result: str | None = None,
        error_category: str | None = None,
        timestamp: str | datetime | None = None,
        sequence: int | None = None,
        mode: str = "standard",
        allow_full_text: bool = False,
    ) -> dict[str, Any]:
        proposal = proposal or {}
        action = action_type or str(proposal.get("actionType", ""))
        target = target_summary if target_summary is not None else str(proposal.get("targetLabel", "target"))
        record: dict[str, Any] = {
            "protocolVersion": PROTOCOL_VERSION,
            "event": event if event in _EVENTS else "failure",
            "timestamp": _format_time(timestamp),
            "requestId": str(request_id),
            "actionType": action,
            "decision": str(decision or ""),
            "result": str(result or ""),
        }
        if sequence is not None:
            record["sequence"] = int(sequence)
        if error_category:
            record["errorCategory"] = str(error_category)
        if mode != "minimized":
            record["userRequestSummary"] = redact_sensitive_text(user_request_summary or "", allow_full_text=allow_full_text, fallback="[redacted]")
            record["targetSummary"] = redact_sensitive_text(target, allow_full_text=allow_full_text, fallback="target") or "target"
            input_preview = proposal.get("inputPreview")
            if input_preview is not None:
                record["inputSummary"] = (
                    redact_sensitive_text(input_preview, allow_full_text=True) if allow_full_text else "[redacted]"
                )
        return record

    def append(self, record: Mapping[str, Any]) -> dict[str, Any] | None:
        """Append one record to JSONL unless logging is disabled."""

        if not self.enabled:
            return None
        row = dict(record)
        if "sequence" not in row:
            self._sequence += 1
            row["sequence"] = self._sequence
        else:
            self._sequence = max(self._sequence, int(row["sequence"]))
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
        return row

    def record_proposal(self, request_id: str, user_request_summary: str, proposal: Mapping[str, Any], *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        return self._record("action_proposed", request_id, proposal, user_request_summary=user_request_summary, decision="pending", result="pending", timestamp=timestamp)

    def record_policy_decision(self, request_id: str, proposal: Mapping[str, Any], decision: str, *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        return self._record("policy_decision", request_id, proposal, decision=decision, result="accepted" if decision != "block" else "blocked", timestamp=timestamp)

    def record_confirmation(self, request_id: str, proposal: Mapping[str, Any], decision: str, *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        result = "cancelled" if decision == "cancelled" else "accepted"
        return self._record("confirmation", request_id, proposal, decision=decision, result=result, timestamp=timestamp)

    def record_execution(self, request_id: str, proposal: Mapping[str, Any], result: str, *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        return self._record("execution", request_id, proposal, decision="forwarded", result=result, timestamp=timestamp)

    def record_verification(self, request_id: str, proposal: Mapping[str, Any], result: str, *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        return self._record("verification", request_id, proposal, decision="verified", result=result, timestamp=timestamp)

    def record_failure(self, request_id: str, proposal: Mapping[str, Any], error_category: str, *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        return self._record("failure", request_id, proposal, decision="failed", result="error", error_category=error_category, timestamp=timestamp)

    def record_cancellation(self, request_id: str, proposal: Mapping[str, Any], *, timestamp: str | datetime | None = None) -> dict[str, Any] | None:
        return self._record("cancellation", request_id, proposal, decision="cancelled", result="cancelled", timestamp=timestamp)

    def _record(
        self,
        event: str,
        request_id: str,
        proposal: Mapping[str, Any],
        *,
        user_request_summary: str | None = None,
        decision: str,
        result: str,
        error_category: str | None = None,
        timestamp: str | datetime | None = None,
    ) -> dict[str, Any] | None:
        record = self.build_record(
            event=event,
            request_id=request_id,
            user_request_summary=user_request_summary,
            proposal=proposal,
            decision=decision,
            result=result,
            error_category=error_category,
            timestamp=timestamp,
            mode=self.mode,
        )
        return self.append(record)


def _default_current_platform_path() -> Path:
    if os.name == "nt":
        base = os.environ.get("APPDATA") or str(Path.home() / "AppData" / "Roaming")
        return Path(base) / "Clicky" / "audit.jsonl"
    return Path.home() / "Library" / "Application Support" / "Clicky" / "audit.jsonl"


def _format_time(value: str | datetime | None) -> str:
    if value is None:
        current = datetime.now(timezone.utc)
    elif isinstance(value, datetime):
        current = value.astimezone(timezone.utc) if value.tzinfo else value.replace(tzinfo=timezone.utc)
    else:
        text = str(value)
        if text.endswith("Z"):
            text = text[:-1] + "+00:00"
        current = datetime.fromisoformat(text).astimezone(timezone.utc)
    return current.replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _truncate(value: str, limit: int) -> str:
    if len(value) <= limit:
        return value
    return value[: max(0, limit - 1)].rstrip() + "…"
