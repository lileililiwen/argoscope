#!/usr/bin/env python3
"""Hosted release gate (R1): readiness-gated traffic promotion.

Checks a running Argoscope candidate release and blocks promotion when
readiness fails. Provider-independent: works against any host that serves
the API. Pure stdlib; no dependencies.

Usage:
    python3 scripts/ops_readiness.py --base-url http://127.0.0.1:8080 [--revision rev-123]

Exit 0: release may receive traffic. Exit non-zero: promotion blocked.
"""

import argparse
import json
import os
import sys
import urllib.request


def get(base_url: str, path: str) -> tuple[int, str]:
    req = urllib.request.Request(base_url.rstrip("/") + path, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status, resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as exc:
        try:
            body = exc.read().decode("utf-8", "replace")
        except Exception:
            body = ""
        return exc.code, body


def fail(message: str) -> int:
    print(f"BLOCKED: {message}")
    return 1


def main() -> int:
    parser = argparse.ArgumentParser(description="Argoscope release readiness gate.")
    parser.add_argument("--base-url", default=os.environ.get("ARGOSCOPE_BASE_URL", "http://127.0.0.1:8080"))
    parser.add_argument("--revision", default=os.environ.get("ARGOSCOPE_RELEASE_REVISION", ""))
    args = parser.parse_args()

    status, live_body = get(args.base_url, "/api/v1/health/live")
    if status != 200:
        return fail(f"liveness check returned {status}; release is not running.")
    try:
        live = json.loads(live_body)
    except json.JSONDecodeError:
        return fail("liveness response is not JSON.")
    revision = str(live.get("revision", ""))
    if not revision:
        return fail("liveness response carries no revision; artifact is not immutable.")
    if args.revision and revision != args.revision:
        return fail(f"running revision {revision!r} != expected {args.revision!r}; wrong artifact.")

    status, ready_body = get(args.base_url, "/api/v1/health/ready")
    if status != 200:
        return fail(f"readiness check returned {status}; traffic promotion stops, prior release stays.")
    try:
        ready = json.loads(ready_body)
    except json.JSONDecodeError:
        return fail("readiness response is not JSON.")
    if ready.get("status") != "ready":
        return fail("readiness status is not 'ready'.")
    checks = ready.get("checks", {})
    if checks.get("database") != "ok" or checks.get("migrations") != "compatible":
        return fail(f"database/migration checks not green: {checks}.")

    status, release_body = get(args.base_url, "/api/v1/ops/release")
    if status != 200:
        return fail(f"release endpoint returned {status}.")
    try:
        release = json.loads(release_body)
    except json.JSONDecodeError:
        return fail("release response is not JSON.")
    if release.get("revision") != revision:
        return fail("release revision disagrees with liveness revision.")
    if not release.get("artifact"):
        return fail("release carries no immutable artifact tag.")
    if release.get("migrations") != "expand-compatible":
        return fail("migration state is not expand-compatible.")

    for label, body in (("liveness", live_body), ("readiness", ready_body), ("release", release_body)):
        lowered = body.lower()
        for marker in ("connection string", "webhooksecret", "github:token", "BEGIN PRIVATE KEY"):
            if marker in lowered:
                return fail(f"{label} response leaks secret material ({marker!r}).")

    print(f"READY: revision={revision} artifact={release.get('artifact')} checks={checks}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
