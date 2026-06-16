"""tool schemas exposed to hermes."""

GET_CLICKY_CAPABILITIES = {
    "name": "get_clicky_capabilities",
    "description": "Report Clicky plugin/runtime capabilities for screen observation, explanation, pointing, overlay rendering, and future OS control. Use before calling other Clicky tools.",
    "parameters": {
        "type": "object",
        "properties": {
            "platform": {
                "type": "string",
                "enum": ["auto", "windows", "macos"],
                "description": "Target platform. Use auto unless the user explicitly asks for a platform.",
                "default": "auto",
            }
        },
    },
}

OBSERVE_CLICKY_SCREEN = {
    "name": "observe_clicky_screen",
    "description": "Ask Clicky to observe the current screen and return semantic display/image metadata. Use when Hermes needs to understand what is visible.",
    "parameters": {
        "type": "object",
        "properties": {
            "imageMode": {
                "type": "string",
                "enum": ["metadataOnly", "file", "base64"],
                "description": "How screenshot data should be returned. Default metadataOnly. Use file only when image pixels are needed.",
                "default": "metadataOnly",
            },
            "includeCursorScreenOnly": {
                "type": "boolean",
                "description": "If true, observe only the display containing the cursor.",
                "default": True,
            },
            "outputDirectory": {
                "type": "string",
                "description": "Optional directory for imageMode=file screenshots. Ignored for metadataOnly.",
            },
            "maxBase64Bytes": {
                "type": "integer",
                "description": "Maximum screenshot byte size allowed for imageMode=base64. Larger images return metadata without bytes.",
                "minimum": 1,
                "default": 1000000,
            },
        },
    },
}

EXPLAIN_CLICKY_SCREEN = {
    "name": "explain_clicky_screen",
    "description": "Ask Clicky to explain visible dialogs, pages, controls, or UI state without executing OS actions. Does not require speech or TTS.",
    "parameters": {
        "type": "object",
        "properties": {
            "task": {
                "type": "string",
                "description": "What the user wants explained about the visible screen.",
            },
            "observationId": {
                "type": "string",
                "description": "Optional observation id returned by observe_clicky_screen.",
            },
            "screenId": {
                "type": "string",
                "description": "Optional display/screen id within an observation.",
            },
        },
        "required": ["task"],
    },
}

POINT_CLICKY_TARGET = {
    "name": "point_clicky_target",
    "description": "Ask Clicky to visually identify a requested target on screen and optionally render Clicky's pointer/annotation overlay. Does not click anything.",
    "parameters": {
        "type": "object",
        "properties": {
            "target": {
                "type": "string",
                "description": "The visible thing to point at, e.g. 'repository settings tab' or 'continue button'.",
            },
            "task": {
                "type": "string",
                "description": "Optional user task context explaining why the target matters.",
            },
            "screenId": {
                "type": "string",
                "description": "Optional screen observation id from observe_clicky_screen.",
            },
            "renderOverlay": {
                "type": "boolean",
                "description": "Whether Clicky should render the pointer/annotation overlay.",
                "default": True,
            },
        },
        "required": ["target"],
    },
}


EXECUTE_CLICKY_ACTION = {
    "name": "execute_clicky_action",
    "description": "Ask Clicky to execute a permissioned native desktop action such as opening an app, focusing a window, pressing a hotkey, typing text, or clicking. Use only after checking capabilities and when the user has requested desktop control.",
    "parameters": {
        "type": "object",
        "properties": {
            "actionType": {
                "type": "string",
                "enum": ["openApplication", "focusWindow", "openUrl", "hotkey", "typeText", "click", "doubleClick"],
                "description": "Native action to execute.",
            },
            "target": {"type": "string", "description": "Application/window/control target."},
            "url": {"type": "string", "description": "HTTPS URL to open for openUrl actions."},
            "browser": {"type": "string", "description": "Optional browser/app name for openUrl actions, e.g. Google Chrome."},
            "text": {"type": "string", "description": "Text to type for typeText actions. Avoid secrets."},
            "inputPreview": {"type": "string", "description": "Alias for text/input preview for typeText actions. Avoid secrets."},
            "keys": {"type": "array", "items": {"type": "string"}, "description": "Keys/modifiers for hotkey actions."},
            "hotkey": {"type": "array", "items": {"type": "string"}, "description": "Alias for keys/modifiers for hotkey actions."},
            "position": {
                "type": "object",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"},
                    "displayId": {"type": "string"},
                },
                "description": "Optional position for click actions.",
            },
            "reason": {"type": "string", "description": "Short user-facing reason for the action."},
            "confirmationApproved": {"type": "boolean", "description": "Whether confirmation was approved.", "default": False},
            "expectedState": {"type": "string", "description": "Optional deterministic hint describing the expected screen/app state after execution."},
            "postActionObservation": {
                "type": "object",
                "description": "Optional already-collected observation to verify after execution. The tool does not capture the screen itself.",
                "properties": {
                    "matchedExpectedState": {"type": "boolean"},
                    "screenChanged": {"type": "boolean"},
                    "permissionPromptVisible": {"type": "boolean"},
                    "errorDialogVisible": {"type": "boolean"},
                    "targetStillUnchanged": {"type": "boolean"},
                    "appLostFocus": {"type": "boolean"},
                    "summary": {"type": "string"},
                    "explanation": {"type": "string"},
                },
            },
            "auditLogPath": {"type": "string", "description": "Optional JSONL path for privacy-preserving execution/verification audit records."},
        },
        "required": ["actionType"],
    },
}
