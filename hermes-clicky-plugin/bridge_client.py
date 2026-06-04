"""json-rpc stdio client for the future native clicky bridge.

v1 skeleton intentionally returns bridge_unavailable when no bridge command is
configured. fake success would make agents trust capabilities that do not exist.
"""

from __future__ import annotations

import json
import os
import subprocess
import uuid
from dataclasses import dataclass
from typing import Any


class ClickyBridgeError(RuntimeError):
    def __init__(self, code: str, message: str, retryable: bool = False, permission: str | None = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.retryable = retryable
        self.permission = permission

    def to_dict(self) -> dict[str, Any]:
        data = {"code": self.code, "message": self.message, "retryable": self.retryable}
        if self.permission is not None:
            data["permission"] = self.permission
        return data


@dataclass(frozen=True)
class BridgeConfig:
    command: str | None = None
    timeout_seconds: int = 30

    @staticmethod
    def from_env() -> "BridgeConfig":
        return BridgeConfig(
            command=os.getenv("CLICKY_BRIDGE_COMMAND"),
            timeout_seconds=int(os.getenv("CLICKY_BRIDGE_TIMEOUT", "30")),
        )


class ClickyBridgeClient:
    def __init__(self, config: BridgeConfig | None = None):
        self.config = config or BridgeConfig.from_env()

    def call(self, method: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        if not self.config.command:
            raise ClickyBridgeError(
                "bridge_unavailable",
                "CLICKY_BRIDGE_COMMAND is not set; native Clicky bridge is not configured yet.",
                retryable=False,
            )

        request = {
            "jsonrpc": "2.0",
            "id": str(uuid.uuid4()),
            "method": method,
            "params": params or {},
        }

        try:
            completed = subprocess.run(
                self.config.command,
                input=json.dumps(request) + "\n",
                text=True,
                shell=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                timeout=self.config.timeout_seconds,
                check=False,
            )
        except subprocess.TimeoutExpired as exc:
            raise ClickyBridgeError("bridge_timeout", f"Clicky bridge timed out after {self.config.timeout_seconds}s", retryable=True) from exc

        if completed.returncode != 0:
            stderr = completed.stderr.strip()[:1000]
            raise ClickyBridgeError("bridge_process_failed", stderr or f"bridge exited with {completed.returncode}", retryable=True)

        lines = [line for line in completed.stdout.splitlines() if line.strip()]
        if not lines:
            raise ClickyBridgeError("bridge_empty_response", "Clicky bridge returned no JSON-RPC response", retryable=True)

        try:
            response = json.loads(lines[-1])
        except json.JSONDecodeError as exc:
            raise ClickyBridgeError("bridge_invalid_json", "Clicky bridge returned invalid JSON", retryable=True) from exc

        if "error" in response:
            err = response["error"] or {}
            raise ClickyBridgeError(
                str(err.get("code", "bridge_error")),
                str(err.get("message", "Clicky bridge returned an error")),
                bool(err.get("retryable", False)),
                permission=err.get("permission"),
            )

        result = response.get("result")
        if not isinstance(result, dict):
            raise ClickyBridgeError("bridge_invalid_result", "Clicky bridge result must be an object", retryable=True)
        return result
