"""deterministic permission-tier policy for future OS control.

This module returns policy data only. It never executes desktop actions.
"""

from __future__ import annotations

from collections.abc import Mapping
import os
from typing import Any

PERMISSION_TIERS = ["observe", "point", "confirmBeforeAction", "scopedAutopilot", "fullControl"]
DEFAULT_PERMISSION_TIER = "confirmBeforeAction"
EXPLICIT_TIER_ENV = "CLICKY_PERMISSION_TIER"

ACTION_TYPES = [
    "click",
    "doubleClick",
    "typeText",
    "hotkey",
    "openApplication",
    "focusWindow",
    "waitForScreenChange",
    "stop",
]

SCOPED_AUTOPILOT_ALLOWED_LOW_RISK = {"click", "doubleClick", "focusWindow", "waitForScreenChange", "stop"}


def resolve_active_permission_tier(
    requested_tier: str | None = None,
    environ: Mapping[str, str] | None = None,
) -> str:
    """Return the active tier from explicit external/user configuration.

    requested_tier is intentionally ignored. a plugin request must not silently
    raise privileges, especially to fullControl.
    """

    env = os.environ if environ is None else environ
    configured = str(env.get(EXPLICIT_TIER_ENV, "")).strip()
    if configured in PERMISSION_TIERS:
        return configured
    return DEFAULT_PERMISSION_TIER


def permission_capability_fields(active_tier: str | None = None) -> dict[str, Any]:
    tier = active_tier if active_tier in PERMISSION_TIERS else resolve_active_permission_tier()
    return {
        "activePermissionTier": tier,
        "defaultPermissionTier": DEFAULT_PERMISSION_TIER,
        "availablePermissionTiers": list(PERMISSION_TIERS),
        "permissionTierChange": "explicit_user_setting_or_command_required",
        "fullControlPolicy": "external_user_configuration_only",
    }


def evaluate_permission_for_action(permission_tier: str, proposal: Mapping[str, Any]) -> dict[str, Any]:
    """Evaluate a proposed action against a tier and return inert policy data."""

    tier = permission_tier if permission_tier in PERMISSION_TIERS else DEFAULT_PERMISSION_TIER
    action_type = str(proposal.get("actionType", ""))
    risk_level = str(proposal.get("riskLevel", ""))

    if action_type not in ACTION_TYPES:
        return _decision("block", tier, action_type, "unknown action type")

    if risk_level == "blocked":
        return _decision("block", tier, action_type, "proposal risk level is blocked")

    if tier == "observe":
        return _decision("block", tier, action_type, "observe tier allows screen observation only")

    if tier == "point":
        return _decision("block", tier, action_type, "point tier allows visual pointing only, not OS action execution")

    if tier == "confirmBeforeAction":
        return _decision("requireConfirmation", tier, action_type, "confirmBeforeAction requires user confirmation before any OS action")

    if tier == "scopedAutopilot":
        if risk_level == "low" and action_type in SCOPED_AUTOPILOT_ALLOWED_LOW_RISK:
            return _decision("allow", tier, action_type, "scopedAutopilot allows low-risk navigation/control actions inside an explicit scope")
        return _decision("requireConfirmation", tier, action_type, "scopedAutopilot requires confirmation for text, app, hotkey, medium, or high-risk actions")

    if tier == "fullControl":
        return _decision("allow", tier, action_type, "fullControl allows non-blocked action classes after explicit external user configuration")

    return _decision("block", tier, action_type, "unrecognized permission tier")


def _decision(decision: str, tier: str, action_type: str, reason: str) -> dict[str, Any]:
    return {
        "decision": decision,
        "permissionTier": tier,
        "actionType": action_type,
        "reason": reason,
    }
