import json
from contextlib import contextmanager
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import live_checks


@contextmanager
def check_directory():
    directory = Path(tempfile.mkdtemp(prefix="ZeyaRunnerCheck-"))
    try:
        yield directory
    finally:
        for path in directory.iterdir():
            path.unlink()
        directory.rmdir()


class TerminalRunnerTests(unittest.TestCase):
    def test_failed_checks_remain_enabled(self):
        with self.assertRaisesRegex(AssertionError, "intentional failure"):
            live_checks.require(False, "intentional failure")

    def test_process_failure_propagates_diagnostics(self):
        with check_directory() as folder:
            runner = live_checks.TerminalChecks(folder)
            result = subprocess.CompletedProcess([], 7, "test output", "test error")
            with patch.object(live_checks.subprocess, "run", return_value=result) as run:
                with self.assertRaisesRegex(AssertionError, "test outputtest error"):
                    runner.run_chat("Hello")
                self.assertEqual(run.call_args.args[0][-1], str(Path(folder) / "memory.json"))

    def test_expected_startup_failure_is_explicit(self):
        with check_directory() as folder:
            runner = live_checks.TerminalChecks(folder)
            result = subprocess.CompletedProcess([], 1, "Cannot load memory", "")
            with patch.object(live_checks.subprocess, "run", return_value=result):
                self.assertEqual(runner.run_chat("/memory", expected_exit_code=1), "Cannot load memory")

    def test_timeouts_are_not_hidden(self):
        with check_directory() as folder:
            runner = live_checks.TerminalChecks(folder)
            with patch.object(live_checks.subprocess, "run", side_effect=subprocess.TimeoutExpired("dotnet", 420)):
                with self.assertRaises(subprocess.TimeoutExpired):
                    runner.run_chat("Hello")

    def test_invalid_memory_shape_fails(self):
        with check_directory() as folder:
            runner = live_checks.TerminalChecks(folder)
            runner.memory_path.write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(AssertionError, "array"):
                runner.read_saved_facts()
            runner.memory_path.write_text("{broken", encoding="utf-8")
            with self.assertRaises(json.JSONDecodeError):
                runner.read_saved_facts()

    def test_missing_assembly_fails_before_starting_app(self):
        with check_directory() as folder:
            runner = live_checks.TerminalChecks(folder)
            with patch.object(live_checks, "assembly", Path(folder) / "missing.dll"):
                with patch.object(live_checks.subprocess, "run") as run:
                    with self.assertRaisesRegex(AssertionError, "Build Zeya"):
                        runner.run()
                    run.assert_not_called()


if(__name__ == "__main__"):
    unittest.main()
