"""tool handlers for hermes-clicky-plugin."""

from __future__ import annotations

import json
import platform as platform_module
import uuid
from typing import Any

from . import macos_capabilities
from .audit_log import ActionAuditLog
from .bridge_client import ClickyBridgeClient, ClickyBridgeError
from .permission_policy import permission_capability_fields, resolve_active_permission_tier
from .post_action_verification import verify_post_action_observation


def _json(data: dict[str, Any]) -> str:
    return json.dumps(data, ensure_ascii=False)


SUPPORTED_PLATFORMS = ("windows", "macos")
SUPPORTED_EXECUTE_ACTION_TYPES = {"openApplication", "focusWindow", "hotkey", "typeText", "click", "doubleClick"}

def _platform_name(requested: str | None = None) -> str:
    if requested and requested != "auto":
        return requested
    sys = platform_module.system().lower()
    if sys == "darwin":
        return "macos"
    if sys == "windows":
        return "windows"
    return sys or "unknown"


def _unsupported_platform(platform_name: str) -> dict[str, Any]:
    return {
        "protocolVersion": "clicky.hermes.v1",
        "ok": False,
        "status": "unsupported_platform",
        "platform": platform_name,
        "supportedPlatforms": list(SUPPORTED_PLATFORMS),
        "capabilities": {
            "observeScreen": {"enabled": False, "reason": "unsupported_platform"},
            "explainScreen": {"enabled": False, "reason": "unsupported_platform"},
            "pointToTarget": {"enabled": False, "reason": "unsupported_platform"},
            "overlay": {"enabled": False, "reason": "unsupported_platform"},
            "osControl": {"enabled": False, "reason": "phase_2_not_implemented"},
        },
        **permission_capability_fields(),
        "error": {
            "code": "unsupported_platform",
            "message": "Clicky Hermes plugin V1 supports windows and macos only.",
            "retryable": False,
        },
    }


def _bridge_or_error(method: str, params: dict[str, Any]) -> dict[str, Any]:
    try:
        return ClickyBridgeClient().call(method, params)
    except ClickyBridgeError as exc:
        return {
            "protocolVersion": "clicky.hermes.v1",
            "ok": False,
            "status": exc.code,
            "error": exc.to_dict(),
        }
    except Exception as exc:  # plugin handlers must never raise into hermes
        return {
            "protocolVersion": "clicky.hermes.v1",
            "ok": False,
            "status": "plugin_error",
            "error": {"code": "plugin_error", "message": str(exc), "retryable": False},
        }


def get_clicky_capabilities(args: dict, **kwargs) -> str:
    requested_platform = args.get("platform", "auto")
    platform_name = _platform_name(requested_platform)
    if platform_name not in SUPPORTED_PLATFORMS:
        return _json(_unsupported_platform(platform_name))

    bridge_result = _bridge_or_error("clicky.getCapabilities", {"platform": platform_name})
    if bridge_result.get("ok") is not False:
        bridge_result.update(permission_capability_fields(resolve_active_permission_tier()))
        return _json(bridge_result)

    if platform_name == "macos":
        result = macos_capabilities.build_macos_capabilities()
        result.update(permission_capability_fields(resolve_active_permission_tier()))
        return _json(result)

    # deterministic skeleton response until the native bridge exists
    return _json({
        "protocolVersion": "clicky.hermes.v1",
        "ok": True,
        "status": "bridge_unavailable",
        "plugin": {
            "name": "hermes-clicky-plugin",
            "version": "0.1.0",
            "folderBoundary": "all Hermes plugin code lives under hermes-clicky-plugin/",
        },
        "platform": platform_name,
        "capabilities": {
            "observeScreen": {"enabled": False, "reason": "native_bridge_unavailable"},
            "explainScreen": {"enabled": False, "reason": "native_bridge_unavailable"},
            "pointToTarget": {"enabled": False, "reason": "native_bridge_unavailable"},
            "overlay": {"enabled": False, "reason": "native_bridge_unavailable"},
            "osControl": {"enabled": False, "reason": "phase_2_not_implemented"},
        },
        **permission_capability_fields(resolve_active_permission_tier()),
        "bridge": bridge_result["error"],
    })


def observe_clicky_screen(args: dict, **kwargs) -> str:
    image_mode = args.get("imageMode", "metadataOnly")
    params = {
        "imageMode": image_mode,
        "includeCursorScreenOnly": bool(args.get("includeCursorScreenOnly", True)),
    }
    if args.get("outputDirectory"):
        params["outputDirectory"] = args.get("outputDirectory")
    if args.get("maxBase64Bytes"):
        params["maxBase64Bytes"] = int(args.get("maxBase64Bytes"))
    result = _bridge_or_error("clicky.observeScreen", params)
    return _json(result)


def explain_clicky_screen(args: dict, **kwargs) -> str:
    task = str(args.get("task", "")).strip()
    if not task:
        return _json({"protocolVersion": "clicky.hermes.v1", "ok": False, "status": "invalid_request", "error": {"code": "missing_task", "message": "task is required", "retryable": False}})
    params = {"task": task}
    if args.get("observationId"):
        params["observationId"] = args.get("observationId")
    if args.get("screenId"):
        params["screenId"] = args.get("screenId")
    result = _bridge_or_error("clicky.explainScreen", params)
    return _json(result)


def point_clicky_target(args: dict, **kwargs) -> str:
    target = str(args.get("target", "")).strip()
    if not target:
        return _json({"protocolVersion": "clicky.hermes.v1", "ok": False, "status": "invalid_request", "error": {"code": "missing_target", "message": "target is required", "retryable": False}})
    result = _bridge_or_error("clicky.pointToTarget", {
        "target": target,
        "task": args.get("task"),
        "screenId": args.get("screenId"),
        "renderOverlay": bool(args.get("renderOverlay", True)),
    })
    return _json(result)


def execute_clicky_action(args: dict, **kwargs) -> str:
    action_type = str(args.get("actionType", "")).strip()
    if not action_type:
        return _json({"protocolVersion": "clicky.hermes.v1", "ok": False, "status": "invalid_request", "error": {"code": "missing_action_type", "message": "actionType is required", "retryable": False}})
    if action_type not in SUPPORTED_EXECUTE_ACTION_TYPES:
        return _json({"protocolVersion": "clicky.hermes.v1", "ok": False, "status": "invalid_request", "error": {"code": "invalid_action_type", "message": f"unsupported actionType: {action_type}", "retryable": False}})

    proposal_id = str(args.get("proposalId") or f"hermes-action-{uuid.uuid4().hex[:12]}")
    target = str(args.get("target") or "").strip()
    proposal: dict[str, Any] = {
        "id": proposal_id,
        "actionType": action_type,
        "targetLabel": target,
        "requiresConfirmation": bool(args.get("requiresConfirmation", False)),
    }
    if args.get("reason"):
        proposal["reason"] = args.get("reason")
    if action_type == "openApplication" and target:
        proposal["application"] = target
    if action_type == "typeText":
        proposal["inputPreview"] = str(args.get("text") or args.get("inputPreview") or "")
    if action_type == "hotkey":
        proposal["hotkey"] = args.get("keys") or args.get("hotkey") or []
    if action_type in {"click", "doubleClick"} and args.get("position"):
        proposal["coordinates"] = args.get("position")

    params: dict[str, Any] = {
        "proposal": proposal,
        "permissionDecision": {"actionType": action_type, "decision": "allow"},
        "safetyDecision": {"proposalId": proposal_id, "actionType": action_type, "decision": "allow", "forwardToExecutor": True},
    }
    if args.get("confirmationApproved"):
        params["confirmationResponse"] = {
            "proposalId": proposal_id,
            "decision": "approved",
            "approved": True,
            "forwardToExecutor": True,
            "safetyDecision": {"decision": "allow", "forwardToExecutor": True},
        }
    result = _bridge_or_error("clicky.executeAction", params)
    _record_action_execution(args, proposal, result)
    _attach_post_action_verification(args, proposal, result)
    return _json(result)


def _attach_post_action_verification(args: dict[str, Any], proposal: dict[str, Any], result: dict[str, Any]) -> None:
    """Attach deterministic post-action verification status or a caller hook.

    The Hermes-facing tool must not capture the screen itself. When a caller
    supplies an already-collected postActionObservation, classify it locally;
    otherwise make the verification requirement explicit in the result contract.
    """
    if result.get("status") != "executed" or not result.get("ok", False):
        return

    result.setdefault("proposalId", proposal.get("id"))
    result.setdefault("actionType", proposal.get("actionType"))
    expected_state = args.get("expectedState") or args.get("expected_state")

    nested_result = result.get("result")
    if isinstance(nested_result, dict) and nested_result.get("verificationRequired") is False:
        result["verificationRequired"] = False
        result["verificationStatus"] = "skipped"
        result["postActionVerification"] = {
            "status": "skipped",
            "reason": "bridge_marked_verification_not_required",
            "expectedState": expected_state,
            "observationRequired": False,
        }
        return

    observation = args.get("postActionObservation") or args.get("post_action_observation")
    if isinstance(observation, dict):
        verification = verify_post_action_observation(
            result,
            observation,
            expected_state=str(expected_state) if expected_state else None,
            request_id=str(proposal.get("id")),
        )
        result["verificationRequired"] = False
        result["verificationStatus"] = verification["status"]
        result["postActionVerificationResult"] = verification
        _record_action_verification(args, proposal, verification)
        return

    result["verificationRequired"] = True
    result["verificationStatus"] = "required"
    result["postActionVerification"] = {
        "status": "required",
        "reason": "post_action_observation_not_supplied",
        "expectedState": expected_state,
        "observationRequired": True,
        "observationArgument": "postActionObservation",
    }


def _audit_log_from_args(args: dict[str, Any]) -> ActionAuditLog | None:
    path = args.get("auditLogPath") or args.get("audit_log_path")
    if not path:
        return None
    return ActionAuditLog(path=path, enabled=bool(args.get("auditLogEnabled", True)), mode=str(args.get("auditLogMode", "standard")))


def _record_action_execution(args: dict[str, Any], proposal: dict[str, Any], result: dict[str, Any]) -> None:
    audit = _audit_log_from_args(args)
    if audit is None:
        return
    try:
        if result.get("status") == "executed" and result.get("ok", False):
            audit.record_execution(str(proposal.get("id")), proposal, "executed")
        else:
            audit.record_failure(str(proposal.get("id")), proposal, str(result.get("status") or "execution_failed"))
    except Exception as exc:
        result["auditStatus"] = "failed"
        result["auditError"] = {"code": "audit_write_failed", "message": str(exc), "retryable": False}


def _record_action_verification(args: dict[str, Any], proposal: dict[str, Any], verification: dict[str, Any]) -> None:
    audit = _audit_log_from_args(args)
    if audit is None:
        return
    try:
        audit.record_verification(str(proposal.get("id")), proposal, str(verification.get("status") or "unknown"))
    except Exception:
        # Verification already completed; do not fail the tool response because
        # optional audit persistence failed.
        return
