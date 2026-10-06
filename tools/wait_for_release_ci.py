#!/usr/bin/env python3
"""Require successful push CI for the exact commit being released."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from collections.abc import Callable
from dataclasses import dataclass
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


class CiGateError(RuntimeError):
    """The release cannot establish that its commit passed CI."""


@dataclass(frozen=True)
class Target:
    repository: str
    sha: str
    branch: str

    def __post_init__(self) -> None:
        if not re.fullmatch(r"[\w.-]+/[\w.-]+", self.repository):
            raise CiGateError("Repository must have the form owner/name.")
        if not re.fullmatch(r"[0-9a-fA-F]{40}", self.sha):
            raise CiGateError("The release target must be a full commit SHA.")
        if not self.branch.strip():
            raise CiGateError("The release target must name its CI branch.")


@dataclass(frozen=True)
class CiRun:
    id: int
    attempt: int
    status: str
    conclusion: str | None


def latest_run(runs: list[dict], target: Target) -> CiRun:
    candidates = []
    for run in runs:
        if not isinstance(run, dict):
            raise CiGateError("GitHub returned an invalid CI run.")
        if (
            run.get("head_sha") != target.sha
            or run.get("head_branch") != target.branch
            or run.get("event") != "push"
            or run.get("path") != ".github/workflows/ci.yml"
        ):
            continue
        repository = run.get("head_repository")
        if (
            not isinstance(repository, dict)
            or str(repository.get("full_name")).lower() != target.repository.lower()
        ):
            continue
        run_id, attempt = run.get("id"), run.get("run_attempt")
        status, conclusion = run.get("status"), run.get("conclusion")
        if (
            type(run_id) is not int
            or run_id < 1
            or type(attempt) is not int
            or attempt < 1
            or not isinstance(status, str)
            or (conclusion is not None and not isinstance(conclusion, str))
        ):
            raise CiGateError("GitHub returned incomplete CI run metadata.")
        candidates.append(CiRun(run_id, attempt, status, conclusion))
    if not candidates:
        raise CiGateError(f"No push CI run exists for {target.sha} on {target.branch}.")
    return max(candidates, key=lambda run: (run.id, run.attempt))


def request_runs(target: Target, page: int) -> dict:
    token = os.environ.get("GH_TOKEN")
    if not token:
        raise CiGateError("GH_TOKEN is required to check release CI.")
    query = urlencode(
        {
            "branch": target.branch,
            "head_sha": target.sha,
            "event": "push",
            "per_page": 100,
            "page": page,
        }
    )
    request = Request(
        f"https://api.github.com/repos/{target.repository}/actions/workflows/ci.yml/runs?{query}",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2026-03-10",
            "User-Agent": "novalist-release-ci-gate",
        },
    )
    try:
        with urlopen(request, timeout=30) as response:
            payload = json.load(response)
    except HTTPError as error:
        raise CiGateError(f"GitHub CI query failed with HTTP {error.code}.") from None
    except (URLError, TimeoutError, OSError, ValueError):
        raise CiGateError("Unable to read CI status from GitHub.") from None
    if not isinstance(payload, dict):
        raise CiGateError("GitHub returned an invalid workflow run response.")
    return payload


def fetch_runs(target: Target) -> list[dict]:
    runs = []
    for page in range(1, 11):
        payload = request_runs(target, page)
        batch, total = payload.get("workflow_runs"), payload.get("total_count")
        if not isinstance(batch, list) or type(total) is not int or total < 0:
            raise CiGateError("GitHub returned incomplete workflow run data.")
        runs.extend(batch)
        if len(runs) >= total:
            return runs
        if not batch:
            raise CiGateError("GitHub returned an incomplete page of CI runs.")
    raise CiGateError("Too many matching CI runs to establish the latest result.")


def wait_for_ci(
    target: Target,
    *,
    timeout_seconds: int = 3600,
    poll_seconds: int = 30,
    fetch: Callable[[Target], list[dict]] = fetch_runs,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
    report: Callable[[str], None] = print,
) -> CiRun:
    if timeout_seconds <= 0 or not 1 <= poll_seconds <= 60:
        raise CiGateError(
            "Timeout must be positive; polling must be between 1 and 60 seconds."
        )
    deadline = clock() + timeout_seconds
    previous = None
    while clock() < deadline:
        run = latest_run(fetch(target), target)
        url = f"https://github.com/{target.repository}/actions/runs/{run.id}/attempts/{run.attempt}"
        if run != previous:
            report(
                f"CI for {target.sha}: {run.status}/{run.conclusion or 'pending'}; {url}"
            )
            previous = run
        if clock() >= deadline:
            break
        if run.status == "completed":
            if run.conclusion == "success":
                return run
            raise CiGateError(
                f"Release blocked: CI concluded {run.conclusion or 'without a result'}; {url}"
            )
        if run.status not in {
            "queued",
            "in_progress",
            "requested",
            "waiting",
            "pending",
        }:
            raise CiGateError(
                f"Release blocked: unrecognized CI status {run.status}; {url}"
            )
        sleep(min(poll_seconds, max(0, deadline - clock())))
    raise CiGateError(f"Timed out waiting for CI for {target.sha} on {target.branch}.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--branch", required=True)
    parser.add_argument("--timeout-seconds", type=int, default=3600)
    parser.add_argument("--poll-seconds", type=int, default=30)
    args = parser.parse_args(argv)
    try:
        target = Target(args.repository, args.sha, args.branch)
        wait_for_ci(
            target,
            timeout_seconds=args.timeout_seconds,
            poll_seconds=args.poll_seconds,
            report=lambda message: print(message, flush=True),
        )
    except CiGateError as error:
        print(f"release-ci: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
