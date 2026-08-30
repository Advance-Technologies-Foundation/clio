"""Regression tests for the Sonar new-issue delivery gate."""

from __future__ import annotations

import importlib.util
import io
import json
import subprocess
import unittest
from contextlib import redirect_stderr
from pathlib import Path
from unittest.mock import patch


SCRIPT_PATH = Path(__file__).with_name("check_sonar_new_issues.py")
SPEC = importlib.util.spec_from_file_location("check_sonar_new_issues", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load {SCRIPT_PATH}")
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)


def sonar_check(check_id: int = 1) -> dict[str, object]:
    """Return a valid Sonar check-run payload."""
    return {
        "id": check_id,
        "name": checker.SONAR_CHECK,
        "head_sha": "head-sha",
        "status": "completed",
        "conclusion": "success",
        "app": {"slug": checker.SONAR_APP_SLUG},
        "details_url": (
            "https://sonarcloud.io/dashboard?"
            f"id={checker.DEFAULT_PROJECT_KEY}&pullRequest=1264"
        ),
    }


class Response:
    """Minimal context-managed HTTP response for decoding tests."""

    def __init__(self, body: bytes) -> None:
        self.body = body

    def __enter__(self) -> Response:
        return self

    def __exit__(self, *args: object) -> None:
        return None

    def read(self) -> bytes:
        return self.body


class SonarNewIssueTests(unittest.TestCase):
    """Exercise fail-closed behavior at the external boundaries."""

    def test_sonar_check_ignores_same_name_run_from_untrusted_app(self) -> None:
        trusted = sonar_check(1)
        spoofed = {**sonar_check(999), "app": {"slug": "github-actions"}}
        payload = json.dumps([{"check_runs": [trusted, spoofed]}])

        with patch.object(checker, "gh", return_value=payload):
            actual = checker.sonar_check("head-sha", 1264, 30)

        self.assertEqual(1, actual["id"])

    def test_sonar_issues_rejects_duplicate_page_without_looping(self) -> None:
        calls = 0

        def repeated_page(url: str, timeout: int) -> dict[str, object]:
            nonlocal calls
            calls += 1
            return {
                "paging": {"pageIndex": calls, "pageSize": 1, "total": 2},
                "issues": [{"key": "same-issue"}],
            }

        with patch.object(checker, "get_json", side_effect=repeated_page):
            with self.assertRaisesRegex(checker.Unverified, "duplicate issue"):
                checker.sonar_issues(1264, 30)

        self.assertEqual(2, calls)

    def test_main_rejects_replaced_analysis_on_same_head(self) -> None:
        error = io.StringIO()

        with (
            patch.object(checker, "pr_head", side_effect=["head-sha", "head-sha"]),
            patch.object(
                checker,
                "sonar_check",
                side_effect=[sonar_check(1), sonar_check(2)],
            ),
            patch.object(checker, "sonar_issues", return_value=[]),
            redirect_stderr(error),
        ):
            result = checker.main(["--pr", "1264"])

        self.assertEqual(2, result)
        self.assertIn("Sonar analysis changed", error.getvalue())

    def test_get_json_rejects_invalid_utf8_as_unverified(self) -> None:
        with patch.object(checker.SONAR_OPENER, "open", return_value=Response(b"\xff")):
            with self.assertRaisesRegex(checker.Unverified, "not valid JSON"):
                checker.get_json("https://sonarcloud.io/api/issues/search", 30)

    def test_gh_timeout_is_unverified(self) -> None:
        with patch.object(
            checker.subprocess,
            "run",
            side_effect=subprocess.TimeoutExpired(["gh"], 3),
        ):
            with self.assertRaisesRegex(checker.Unverified, "timed out"):
                checker.gh("api", "user", timeout=3)

    def test_issue_text_prefers_current_issue_status(self) -> None:
        issue = {
            "key": "issue-key",
            "component": f"{checker.DEFAULT_PROJECT_KEY}:file.cs",
            "severity": "MAJOR",
            "rule": "rule",
            "issueStatus": "ACCEPTED",
            "status": "RESOLVED",
            "message": "message",
        }

        actual = checker.issue_text(issue)

        self.assertIn("ACCEPTED", actual)
        self.assertNotIn("RESOLVED", actual)


if __name__ == "__main__":
    unittest.main()
