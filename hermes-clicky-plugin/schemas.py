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
