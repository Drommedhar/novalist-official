"""Release CI must prove success for the commit it is about to build."""

import contextlib
import io
import json
import os
from pathlib import Path
import re
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, urlparse

from tools import wait_for_release_ci as gate


TARGET = gate.Target("Drommedhar/novalist-official", "a" * 40, "main")


def run_payload(**changes):
    result = {
        "id": 100,
        "run_attempt": 1,
        "head_sha": TARGET.sha,
        "head_branch": TARGET.branch,
        "head_repository": {"full_name": TARGET.repository},
        "event": "push",
        "path": ".github/workflows/ci.yml",
        "status": "completed",
        "conclusion": "success",
    }
    result.update(changes)
    return result


class Clock:
    def __init__(self):
        self.now = 0
        self.sleeps = []

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.now += seconds


class ReleaseCiTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.messages = []

    def wait(self, responses, **options):
        return gate.wait_for_ci(
            TARGET,
            fetch=Mock(side_effect=responses),
            clock=self.clock,
            sleep=self.clock.sleep,
            report=self.messages.append,
            **options,
        )

    def test_success_requires_no_wait(self):
        result = self.wait([[run_payload()]])
        self.assertEqual(result.id, 100)
        self.assertEqual(self.clock.sleeps, [])
        self.assertIn(TARGET.sha, self.messages[0])
        self.assertIn("/actions/runs/100/attempts/1", self.messages[0])

    def test_queued_and_running_wait_until_green(self):
        result = self.wait(
            [
                [run_payload(status="queued", conclusion=None)],
                [run_payload(status="in_progress", conclusion=None)],
                [run_payload()],
            ]
        )
        self.assertEqual(result.conclusion, "success")
        self.assertEqual(self.clock.sleeps, [30, 30])

    def test_waiting_ci_that_turns_red_blocks_release(self):
        with self.assertRaisesRegex(gate.CiGateError, "concluded failure"):
            self.wait(
                [
                    [run_payload(status="in_progress", conclusion=None)],
                    [run_payload(conclusion="failure")],
                ]
            )
        self.assertEqual(self.clock.sleeps, [30])

    def test_all_terminal_non_success_results_block(self):
        for conclusion in (
            "failure",
            "cancelled",
            "timed_out",
            "action_required",
            "neutral",
            "skipped",
            "stale",
            None,
        ):
            with self.subTest(conclusion=conclusion):
                with self.assertRaisesRegex(gate.CiGateError, "Release blocked"):
                    self.wait([[run_payload(conclusion=conclusion)]])

    def test_missing_ci_fails_without_waiting(self):
        with self.assertRaisesRegex(gate.CiGateError, "No push CI run"):
            self.wait([[]])
        self.assertEqual(self.clock.sleeps, [])

    def test_wrong_commit_branch_event_workflow_or_repository_cannot_pass(self):
        invalid_runs = (
            {"head_sha": "b" * 40},
            {"head_sha": None},
            {"head_branch": "other"},
            {"event": "pull_request"},
            {"path": ".github/workflows/other.yml"},
            {"head_repository": {"full_name": "fork/novalist-official"}},
            {"head_repository": None},
        )
        for changes in invalid_runs:
            with self.subTest(changes=changes):
                with self.assertRaisesRegex(gate.CiGateError, "No push CI run"):
                    self.wait([[run_payload(**changes)]])

    def test_latest_failed_run_wins_over_older_green_regardless_of_order(self):
        older, newer = run_payload(id=100), run_payload(id=101, conclusion="failure")
        for runs in ([older, newer], [newer, older]):
            with self.subTest(runs=runs):
                with self.assertRaisesRegex(gate.CiGateError, "concluded failure"):
                    self.wait([runs])

    def test_newer_running_run_is_not_bypassed_by_previous_success(self):
        result = self.wait(
            [
                [
                    run_payload(),
                    run_payload(id=101, status="in_progress", conclusion=None),
                ],
                [run_payload(), run_payload(id=101)],
            ]
        )
        self.assertEqual(result.id, 101)
        self.assertEqual(self.clock.sleeps, [30])

    def test_latest_rerun_attempt_must_finish(self):
        result = self.wait(
            [
                [
                    run_payload(),
                    run_payload(run_attempt=2, status="queued", conclusion=None),
                ],
                [run_payload(run_attempt=2)],
            ]
        )
        self.assertEqual(result.attempt, 2)
        self.assertEqual(self.clock.sleeps, [30])

    def test_failed_rerun_does_not_reuse_successful_original(self):
        with self.assertRaisesRegex(gate.CiGateError, "concluded failure"):
            self.wait(
                [[run_payload(), run_payload(run_attempt=2, conclusion="failure")]]
            )

    def test_rerunning_an_older_run_does_not_supersede_newer_failure(self):
        with self.assertRaisesRegex(gate.CiGateError, "concluded failure"):
            self.wait(
                [
                    [
                        run_payload(run_attempt=8),
                        run_payload(id=101, conclusion="failure"),
                    ]
                ]
            )

    def test_polling_rechecks_latest_run_instead_of_pinning_initial_run(self):
        with self.assertRaisesRegex(gate.CiGateError, "concluded failure"):
            self.wait(
                [
                    [run_payload(status="in_progress", conclusion=None)],
                    [run_payload(), run_payload(id=101, conclusion="failure")],
                ]
            )

    def test_branch_advancing_does_not_invalidate_tested_release_commit(self):
        result = self.wait(
            [
                [
                    run_payload(),
                    run_payload(id=101, head_sha="b" * 40, conclusion="failure"),
                ]
            ]
        )
        self.assertEqual(result.id, 100)

    def test_timeout_caps_final_sleep(self):
        waiting = [run_payload(status="waiting", conclusion=None)]
        with self.assertRaisesRegex(gate.CiGateError, "Timed out"):
            self.wait([waiting, waiting], timeout_seconds=35)
        self.assertEqual(self.clock.sleeps, [30, 5])
        self.assertEqual(self.clock.now, 35)

    def test_success_received_after_deadline_cannot_pass(self):
        def late_response(_target):
            self.clock.now = 61
            return [run_payload()]

        with self.assertRaisesRegex(gate.CiGateError, "Timed out"):
            gate.wait_for_ci(
                TARGET,
                fetch=late_response,
                clock=self.clock,
                sleep=self.clock.sleep,
                report=self.messages.append,
                timeout_seconds=60,
            )

    def test_unknown_status_and_malformed_metadata_fail_closed(self):
        invalid = [
            run_payload(status="unknown"),
            run_payload(id=None),
            run_payload(run_attempt=None),
            run_payload(conclusion={}),
            None,
        ]
        for run in invalid:
            with self.subTest(run=run), self.assertRaises(gate.CiGateError):
                self.wait([[run]])

    def test_invalid_timing_options_rejected(self):
        for options in (
            {"timeout_seconds": 0},
            {"poll_seconds": 0},
            {"poll_seconds": 61},
        ):
            with self.subTest(options=options), self.assertRaises(gate.CiGateError):
                self.wait([], **options)

    def test_target_requires_repository_full_sha_and_branch(self):
        for arguments in (
            ("bad", TARGET.sha, "main"),
            (TARGET.repository, "abc", "main"),
            (TARGET.repository, TARGET.sha, ""),
        ):
            with self.subTest(arguments=arguments), self.assertRaises(gate.CiGateError):
                gate.Target(*arguments)


class GitHubQueryTests(unittest.TestCase):
    def test_request_filters_exact_commit_branch_and_push_workflow(self):
        payload = {"total_count": 1, "workflow_runs": [run_payload()]}
        with (
            patch.dict(os.environ, {"GH_TOKEN": "fixture-token"}),
            patch.object(
                gate, "urlopen", return_value=io.StringIO(json.dumps(payload))
            ) as opener,
        ):
            self.assertEqual(gate.fetch_runs(TARGET), payload["workflow_runs"])
        request = opener.call_args.args[0]
        parsed = urlparse(request.full_url)
        self.assertEqual(
            parsed.path, f"/repos/{TARGET.repository}/actions/workflows/ci.yml/runs"
        )
        self.assertEqual(
            parse_qs(parsed.query),
            {
                "branch": ["main"],
                "head_sha": [TARGET.sha],
                "event": ["push"],
                "per_page": ["100"],
                "page": ["1"],
            },
        )
        self.assertEqual(request.get_header("Authorization"), "Bearer fixture-token")
        self.assertNotIn("fixture-token", request.full_url)
        self.assertEqual(opener.call_args.kwargs["timeout"], 30)

    def test_all_pages_are_considered_when_choosing_latest(self):
        pages = [
            {"total_count": 2, "workflow_runs": [run_payload()]},
            {
                "total_count": 2,
                "workflow_runs": [run_payload(id=101, conclusion="failure")],
            },
        ]
        with patch.object(gate, "request_runs", side_effect=pages) as request:
            latest = gate.latest_run(gate.fetch_runs(TARGET), TARGET)
        self.assertEqual(latest.conclusion, "failure")
        self.assertEqual([call.args[1] for call in request.call_args_list], [1, 2])

    def test_missing_token_fails_before_network_access(self):
        with (
            patch.dict(os.environ, {}, clear=True),
            patch.object(gate, "urlopen") as opener,
        ):
            with self.assertRaisesRegex(gate.CiGateError, "GH_TOKEN is required"):
                gate.fetch_runs(TARGET)
        opener.assert_not_called()

    def test_network_errors_do_not_expose_token_or_server_response(self):
        errors = [
            HTTPError("url", 403, "fixture-token", {}, None),
            URLError("fixture-token"),
            TimeoutError("fixture-token"),
        ]
        for error in errors:
            with (
                self.subTest(error=type(error).__name__),
                patch.dict(os.environ, {"GH_TOKEN": "fixture-token"}),
                patch.object(gate, "urlopen", side_effect=error),
            ):
                with self.assertRaises(gate.CiGateError) as caught:
                    gate.fetch_runs(TARGET)
                self.assertNotIn("fixture-token", str(caught.exception))

    def test_invalid_json_or_response_structure_fails_closed(self):
        for response in (
            "invalid",
            "[]",
            "{}",
            '{"total_count": 1, "workflow_runs": []}',
        ):
            with (
                self.subTest(response=response),
                patch.dict(os.environ, {"GH_TOKEN": "fixture-token"}),
                patch.object(gate, "urlopen", return_value=io.StringIO(response)),
            ):
                with self.assertRaises(gate.CiGateError):
                    gate.fetch_runs(TARGET)

    def test_cli_returns_failure_without_credentials(self):
        stderr = io.StringIO()
        with patch.dict(os.environ, {}, clear=True), contextlib.redirect_stderr(stderr):
            code = gate.main(
                [
                    "--repository",
                    TARGET.repository,
                    "--sha",
                    TARGET.sha,
                    "--branch",
                    "main",
                ]
            )
        self.assertEqual(code, 1)
        self.assertIn("GH_TOKEN is required", stderr.getvalue())


class ReleaseWorkflowTests(unittest.TestCase):
    def test_every_build_waits_for_ci_gate(self):
        workflow = (
            Path(__file__).resolve().parents[1] / ".github/workflows/release.yml"
        ).read_text(encoding="utf-8")
        headings = list(re.finditer(r"^  ([a-z][\w-]*):\s*$", workflow, re.MULTILINE))
        jobs = {
            heading[1]: workflow[
                heading.end() : headings[index + 1].start()
                if index + 1 < len(headings)
                else len(workflow)
            ]
            for index, heading in enumerate(headings)
        }
        for name in ("publish", "mac-app-store", "ios-app-store", "sdk-and-example"):
            with self.subTest(job=name):
                self.assertRegex(jobs[name], r"(?m)^    needs: require-green-ci$")
        for name in (
            "require-green-ci",
            "publish",
            "mac-app-store",
            "ios-app-store",
            "sdk-and-example",
            "release",
        ):
            with self.subTest(rechecked_job=name):
                action = "uses: ./.github/actions/require-green-ci"
                self.assertIn(action, jobs[name])
                self.assertIn("actions: read", jobs[name])
                self.assertNotRegex(jobs[name].split(action)[0], r"(?m)^\s+run:")
        self.assertIn("timeout-minutes: 65", jobs["require-green-ci"])

    def test_shared_gate_uses_exact_commit_and_correct_branch(self):
        action = (
            Path(__file__).resolve().parents[1]
            / ".github/actions/require-green-ci/action.yml"
        ).read_text(encoding="utf-8")
        self.assertIn("tools/wait_for_release_ci.py", action)
        self.assertIn("RELEASE_SHA: ${{ github.sha }}", action)
        self.assertIn(
            "github.ref_type == 'branch' && github.ref_name || github.event.repository.default_branch",
            action,
        )
        self.assertIn("GH_TOKEN: ${{ github.token }}", action)


if __name__ == "__main__":
    unittest.main()
