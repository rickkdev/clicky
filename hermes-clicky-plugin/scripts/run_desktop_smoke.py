#!/usr/bin/env python3
"""ci-safe entrypoint for Hermes desktop smoke fixture metadata.

without --real this runs the fake fixture path only. with --real it refuses to
execute desktop actions directly and tells the operator which native runner/logs
to use; real click/type automation stays opt-in and platform-owned.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import smoke_fixtures  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description="run Clicky Hermes desktop smoke fixture metadata")
    parser.add_argument("--platform", choices=["windows", "macos"], required=True)
    parser.add_argument("--real", action="store_true", help="require CLICKY_RUN_DESKTOP_SMOKE=1 and return native-runner instructions")
    args = parser.parse_args()

    if args.real and os.environ.get(smoke_fixtures.OPT_IN_ENV) != "1":
        print(json.dumps({
            "status": "blocked",
            "reason": "real desktop smoke requires CLICKY_RUN_DESKTOP_SMOKE=1",
            "desktopActionsExecuted": False,
        }, indent=2))
        return 2

    result = smoke_fixtures.run_fixture(args.platform, env=os.environ if args.real else {})
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0 if result["status"] in {"passed", "requires_native_runner"} else 1


if __name__ == "__main__":
    raise SystemExit(main())
