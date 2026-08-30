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
from urllib.parse import parse_qs, urlencode, urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener


DEFAULT_REPO = "Advance-Technologies-Foundation/clio"
DEFAULT_PROJECT_KEY = "Advance-Technologies-Foundation_clio"
DEFAULT_SONAR_URL = "https://sonarcloud.io"
SONAR_CHECK = "SonarCloud Code Analysis"
SONAR_APP_SLUG = "sonarqubecloud"
BLOCKING_STATUSES = "OPEN,CONFIRMED,ACCEPTED"


class Unverified(RuntimeError):
    """The zero-new-issues result could not be proven."""


class NoRedirectHandler(HTTPRedirectHandler):
    """Prevent authenticated Sonar requests from crossing origins."""

    def redirect_request(self, request: Request, *args: Any, **kwargs: Any) -> None:
        return None


SONAR_OPENER = build_opener(NoRedirectHandler)


def gh(*arguments: str, timeout: int) -> str:
    try:
        result = subprocess.run(
            ["gh", *arguments],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
            timeout=timeout,
        )
    except FileNotFoundError as error:
        raise Unverified("GitHub CLI 'gh' is not installed or not on PATH.") from error
    except subprocess.TimeoutExpired as error:
        raise Unverified(f"GitHub CLI timed out after {timeout} seconds.") from error
    if result.returncode:
        detail = result.stderr.strip() or result.stdout.strip() or "unknown gh error"
        raise Unverified(f"GitHub CLI failed: {detail}")
    return result.stdout


def pr_head(pull_request: int, timeout: int) -> str:
    head = gh(
        "pr",
        "view",
        str(pull_request),
        "--repo",
        DEFAULT_REPO,
        "--json",
        "headRefOid",
        "--jq",
        ".headRefOid",
        timeout=timeout,
    ).strip()
    if not head:
        raise Unverified("GitHub returned an empty PR head SHA.")
    return head


def sonar_check(head: str, pull_request: int, timeout: int) -> dict[str, Any]:
    raw = gh(
        "api",
        f"repos/{DEFAULT_REPO}/commits/{head}/check-runs?per_page=100",
        "--paginate",
        "--slurp",
        timeout=timeout,
    )
    try:
        pages = json.loads(raw)
        checks = [
            check
            for page in pages
            for check in page.get("check_runs", [])
            if isinstance(check, dict)
            and check.get("name") == SONAR_CHECK
            and isinstance(check.get("app"), dict)
            and check["app"].get("slug") == SONAR_APP_SLUG
        ]
        if not checks:
            raise Unverified(
                f"No trusted '{SONAR_CHECK}' check exists on head {head}."
            )
        check = max(checks, key=lambda item: int(item.get("id") or 0))
    except Unverified:
        raise
    except (AttributeError, ValueError, TypeError) as error:
        raise Unverified("GitHub check-run response had an unexpected shape.") from error

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
    details = urlparse(str(check.get("details_url") or ""))
    details_query = parse_qs(details.query)
    if (
        details.scheme != "https"
        or details.netloc != "sonarcloud.io"
        or details.path != "/dashboard"
        or details_query.get("id") != [DEFAULT_PROJECT_KEY]
        or details_query.get("pullRequest") != [str(pull_request)]
    ):
        raise Unverified("Sonar check details do not match the expected project and PR.")
    return check


def get_json(url: str, timeout: int) -> dict[str, Any]:
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.netloc != "sonarcloud.io":
        raise Unverified("Sonar API URL is outside the trusted SonarCloud origin.")
    headers = {"Accept": "application/json"}
    token = os.environ.get("SONAR_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = Request(url, headers=headers, method="GET")
    try:
        with SONAR_OPENER.open(request, timeout=timeout) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace").strip()[:500]
        raise Unverified(
            f"Sonar API returned HTTP {error.code}{f': {detail}' if detail else ''}"
        ) from error
    except (URLError, TimeoutError) as error:
        raise Unverified(f"Sonar API request failed: {error}") from error
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise Unverified("Sonar API response was not valid JSON.") from error
    if not isinstance(payload, dict):
        raise Unverified("Sonar API response had an unexpected shape.")
    return payload


def pagination(
    payload: dict[str, Any],
    requested_page: int,
    expected_total: int | None,
    expected_page_size: int | None,
) -> tuple[int, int, list[Any]]:
    paging, issues = payload.get("paging"), payload.get("issues")
    if not isinstance(paging, dict) or not isinstance(issues, list):
        raise Unverified("Sonar response omitted paging or issues data.")
    try:
        total = paging["total"]
        page_index = paging["pageIndex"]
        page_size = paging["pageSize"]
    except KeyError as error:
        raise Unverified("Sonar response omitted valid pagination data.") from error
    if not all(type(value) is int for value in (total, page_index, page_size)):
        raise Unverified("Sonar response contained invalid pagination types.")
    if total < 0 or page_size <= 0 or page_index != requested_page:
        raise Unverified("Sonar returned inconsistent pagination data.")
    if expected_total is not None and total != expected_total:
        raise Unverified("Sonar issue total changed during pagination.")
    if expected_page_size is not None and page_size != expected_page_size:
        raise Unverified("Sonar page size changed during pagination.")
    if len(issues) > page_size:
        raise Unverified("Sonar returned more issues than the declared page size.")
    return total, page_size, issues


def add_issues(
    found: dict[str, dict[str, Any]], issues: list[Any]
) -> None:
    for issue in issues:
        if not isinstance(issue, dict) or not issue.get("key"):
            raise Unverified("Sonar returned a malformed issue.")
        key = str(issue["key"])
        if key in found:
            raise Unverified("Sonar returned a duplicate issue during pagination.")
        found[key] = issue


def sonar_query(pull_request: int, page: int) -> str:
    return urlencode(
        {
            "componentKeys": DEFAULT_PROJECT_KEY,
            "pullRequest": pull_request,
            "issueStatuses": BLOCKING_STATUSES,
            "inNewCodePeriod": "true",
            "p": page,
            "ps": 500,
        }
    )


def sonar_issues(pull_request: int, timeout: int) -> list[dict[str, Any]]:
    found: dict[str, dict[str, Any]] = {}
    expected_total: int | None = None
    expected_page_size: int | None = None
    page = 1
    while True:
        query = sonar_query(pull_request, page)
        payload = get_json(f"{DEFAULT_SONAR_URL}/api/issues/search?{query}", timeout)
        expected_total, expected_page_size, issues = pagination(
            payload, page, expected_total, expected_page_size
        )
        add_issues(found, issues)
        page_count = max(
            1, (expected_total + expected_page_size - 1) // expected_page_size
        )
        if page >= page_count:
            break
        if not issues:
            raise Unverified("Sonar pagination ended before every issue was returned.")
        page += 1
    if len(found) != expected_total:
        raise Unverified(
            f"Sonar reported {expected_total} issues but returned {len(found)}."
        )
    return list(found.values())


def issue_text(issue: dict[str, Any]) -> str:
    component = str(issue.get("component") or "unknown-component")
    prefix = f"{DEFAULT_PROJECT_KEY}:"
    if component.startswith(prefix):
        component = component[len(prefix) :]
    line = issue.get("line")
    location = f"{component}:{line}" if line is not None else component
    return (
        f"[{issue.get('severity', 'UNKNOWN')}] {issue.get('rule', 'unknown-rule')} "
        f"{issue.get('issueStatus', issue.get('status', 'UNKNOWN'))} {location} - "
        f"{issue.get('message', 'No message supplied.')} ({issue.get('key')})"
    )


def arguments(values: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--pr", type=int, required=True, help="GitHub pull request number.")
    parser.add_argument(
        "--timeout", type=int, default=30, help="GitHub and Sonar timeout in seconds."
    )
    parsed = parser.parse_args(values)
    if parsed.pr <= 0 or parsed.timeout <= 0:
        parser.error("--pr and --timeout must be positive integers")
    return parsed


def main(values: Sequence[str] | None = None) -> int:
    options = arguments(values if values is not None else sys.argv[1:])
    try:
        head = pr_head(options.pr, options.timeout)
        check = sonar_check(head, options.pr, options.timeout)
        issues = sonar_issues(options.pr, options.timeout)
        current_head = pr_head(options.pr, options.timeout)
        if current_head != head:
            raise Unverified(f"PR head changed during verification: {head} -> {current_head}.")
        current_check = sonar_check(head, options.pr, options.timeout)
        if current_check.get("id") != check.get("id"):
            raise Unverified("Sonar analysis changed during verification; run the check again.")
    except Unverified as error:
        print(f"UNVERIFIED: {error}", file=sys.stderr)
        return 2

    details_url = str(check.get("details_url") or "")
    if issues:
        print(f"FAILED: PR #{options.pr} head {head} has {len(issues)} new Sonar issue(s).")
        if details_url:
            print(f"Sonar analysis: {details_url}")
        for issue in issues:
            print(issue_text(issue))
        return 1

    print(f"PASSED: PR #{options.pr} head {head} has zero new Sonar issues.")
    if details_url:
        print(f"Sonar analysis: {details_url}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
