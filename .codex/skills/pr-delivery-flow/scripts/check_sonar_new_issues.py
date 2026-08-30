#!/usr/bin/env python3
"""Require zero new Sonar issues on the latest GitHub PR head.

Exit 0 means clean, 1 means issues remain, and 2 means the result is unverified.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from collections.abc import Sequence
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


DEFAULT_REPO = "Advance-Technologies-Foundation/clio"
DEFAULT_PROJECT_KEY = "Advance-Technologies-Foundation_clio"
DEFAULT_SONAR_URL = "https://sonarcloud.io"
SONAR_CHECK = "SonarCloud Code Analysis"
BLOCKING_STATUSES = "OPEN,CONFIRMED,ACCEPTED"


class Unverified(RuntimeError):
    """The zero-new-issues result could not be proven."""


def gh(*arguments: str) -> str:
    try:
        result = subprocess.run(
            ["gh", *arguments],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except FileNotFoundError as error:
        raise Unverified("GitHub CLI 'gh' is not installed or not on PATH.") from error
    if result.returncode:
        detail = result.stderr.strip() or result.stdout.strip() or "unknown gh error"
        raise Unverified(f"GitHub CLI failed: {detail}")
    return result.stdout


def pr_head(repository: str, pull_request: int) -> str:
    head = gh(
        "pr",
        "view",
        str(pull_request),
        "--repo",
        repository,
        "--json",
        "headRefOid",
        "--jq",
        ".headRefOid",
    ).strip()
    if not head:
        raise Unverified("GitHub returned an empty PR head SHA.")
    return head


def sonar_check(repository: str, head: str) -> dict[str, Any]:
    raw = gh(
        "api",
        f"repos/{repository}/commits/{head}/check-runs?per_page=100",
        "--paginate",
        "--slurp",
    )
    try:
        pages = json.loads(raw)
        checks = [
            check
            for page in pages
            for check in page.get("check_runs", [])
            if check.get("name") == SONAR_CHECK
        ]
    except (AttributeError, json.JSONDecodeError, TypeError) as error:
        raise Unverified("GitHub check-run response had an unexpected shape.") from error
    if not checks:
        raise Unverified(f"No '{SONAR_CHECK}' check exists on head {head}.")

    check = max(checks, key=lambda item: int(item.get("id") or 0))
    check_head = str(check.get("head_sha") or "")
    if check_head != head:
        raise Unverified(
            f"Sonar check belongs to head {check_head or 'unknown'}, expected {head}."
        )
    status = str(check.get("status") or "unknown").lower()
    conclusion = str(check.get("conclusion") or "unknown").lower()
    if status != "completed":
        raise Unverified(f"Sonar analysis on head {head} is not complete ({status}).")
    if conclusion != "success":
        raise Unverified(f"Sonar analysis on head {head} did not succeed ({conclusion}).")
    return check


def get_json(url: str, timeout: int) -> dict[str, Any]:
    headers = {"Accept": "application/json"}
    token = os.environ.get("SONAR_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = Request(url, headers=headers, method="GET")
    try:
        with urlopen(request, timeout=timeout) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace").strip()[:500]
        raise Unverified(
            f"Sonar API returned HTTP {error.code}{f': {detail}' if detail else ''}"
        ) from error
    except (URLError, TimeoutError) as error:
        raise Unverified(f"Sonar API request failed: {error}") from error
    except json.JSONDecodeError as error:
        raise Unverified("Sonar API response was not valid JSON.") from error
    if not isinstance(payload, dict):
        raise Unverified("Sonar API response had an unexpected shape.")
    return payload


def sonar_issues(
    sonar_url: str,
    project_key: str,
    pull_request: int,
    timeout: int,
) -> list[dict[str, Any]]:
    found: dict[str, dict[str, Any]] = {}
    expected: int | None = None
    page = 1
    while expected is None or len(found) < expected:
        query = urlencode(
            {
                "componentKeys": project_key,
                "pullRequest": pull_request,
                "issueStatuses": BLOCKING_STATUSES,
                "inNewCodePeriod": "true",
                "p": page,
                "ps": 500,
            }
        )
        payload = get_json(f"{sonar_url.rstrip('/')}/api/issues/search?{query}", timeout)
        paging, issues = payload.get("paging"), payload.get("issues")
        if not isinstance(paging, dict) or not isinstance(issues, list):
            raise Unverified("Sonar response omitted paging or issues data.")
        try:
            total = int(paging["total"])
        except (KeyError, TypeError, ValueError) as error:
            raise Unverified("Sonar response omitted a valid issue total.") from error
        if expected is not None and total != expected:
            raise Unverified("Sonar issue total changed during pagination.")
        expected = total
        for issue in issues:
            if not isinstance(issue, dict) or not issue.get("key"):
                raise Unverified("Sonar returned a malformed issue.")
            found[str(issue["key"])] = issue
        if len(found) >= expected:
            break
        if not issues:
            raise Unverified("Sonar pagination ended before every issue was returned.")
        page += 1
    if len(found) != expected:
        raise Unverified(f"Sonar reported {expected} issues but returned {len(found)}.")
    return list(found.values())


def issue_text(issue: dict[str, Any], project_key: str) -> str:
    component = str(issue.get("component") or "unknown-component")
    prefix = f"{project_key}:"
    if component.startswith(prefix):
        component = component[len(prefix) :]
    line = issue.get("line")
    location = f"{component}:{line}" if line is not None else component
    return (
        f"[{issue.get('severity', 'UNKNOWN')}] {issue.get('rule', 'unknown-rule')} "
        f"{issue.get('status', 'UNKNOWN')} {location} - "
        f"{issue.get('message', 'No message supplied.')} ({issue.get('key')})"
    )


def arguments(values: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--pr", type=int, required=True, help="GitHub pull request number.")
    parser.add_argument("--repo", default=DEFAULT_REPO, help="GitHub OWNER/REPO.")
    parser.add_argument(
        "--project-key", default=DEFAULT_PROJECT_KEY, help="Sonar project key."
    )
    parser.add_argument("--sonar-url", default=DEFAULT_SONAR_URL, help="Sonar base URL.")
    parser.add_argument("--timeout", type=int, default=30, help="API timeout in seconds.")
    parsed = parser.parse_args(values)
    if parsed.pr <= 0 or parsed.timeout <= 0:
        parser.error("--pr and --timeout must be positive integers")
    return parsed


def main(values: Sequence[str] | None = None) -> int:
    options = arguments(values if values is not None else sys.argv[1:])
    try:
        head = pr_head(options.repo, options.pr)
        check = sonar_check(options.repo, head)
        issues = sonar_issues(
            options.sonar_url, options.project_key, options.pr, options.timeout
        )
        current_head = pr_head(options.repo, options.pr)
        if current_head != head:
            raise Unverified(f"PR head changed during verification: {head} -> {current_head}.")
    except Unverified as error:
        print(f"UNVERIFIED: {error}", file=sys.stderr)
        return 2

    details_url = str(check.get("details_url") or "")
    if issues:
        print(f"FAILED: PR #{options.pr} head {head} has {len(issues)} new Sonar issue(s).")
        if details_url:
            print(f"Sonar analysis: {details_url}")
        for issue in issues:
            print(issue_text(issue, options.project_key))
        return 1

    print(f"PASSED: PR #{options.pr} head {head} has zero new Sonar issues.")
    if details_url:
        print(f"Sonar analysis: {details_url}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
