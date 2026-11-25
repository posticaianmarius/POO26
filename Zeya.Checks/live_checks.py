import argparse
import json
from pathlib import Path
import subprocess
import sys
import tempfile

repository = Path(__file__).resolve().parents[1]
assembly = repository / "Zeya.Checks" / "bin" / "Debug" / "net10.0" / "Zeya.Checks.dll"


def require(condition, message):
    if(not condition):
        raise AssertionError(message)


class TerminalChecks:
    def __init__(self, checks_directory):
        self.directory = Path(checks_directory)
        if(not self.directory.is_dir()):
            raise ValueError("An existing temporary check directory is required.")
        self.memory_path = self.directory / "memory.json"
        self.passed = 0

    def run_chat(self, *messages, path=None, cwd=repository, expected_exit_code=0):
        memory_file = self.memory_path if path is None else Path(path)
        result = subprocess.run(
            ["dotnet", str(assembly), "--console", str(memory_file)],
            input="\n".join((*messages, "/exit", "")),
            text=True,
            encoding="utf-8",
            capture_output=True,
            cwd=cwd,
            timeout=420,
        )
        require(result.returncode == expected_exit_code, result.stdout + result.stderr)
        return result.stdout

    def read_saved_facts(self):
        facts = json.loads(self.memory_path.read_text(encoding="utf-8"))
        require(isinstance(facts, list), "The memory file must contain an array.")
        require(all(isinstance(fact, dict) for fact in facts), "Each memory must be an object.")
        return facts

    def report_pass(self, name):
        self.passed += 1
        print("PASS " + name, flush=True)

    def run(self):
        require(assembly.is_file(), "Build Zeya first: dotnet build Zeya.slnx")
        output = self.run_chat("Remember that my favorite color is blue.", "/memory")
        facts = self.read_saved_facts()
        require(
            "Memory saved." in output and len(facts) == 1 and "blue" in facts[0]["text"].lower(),
            output,
        )
        original_id = facts[0]["id"]
        self.report_pass("save a lasting fact and inspect it")

        original = self.memory_path.read_bytes()
        output = self.run_chat("Blue is my favourite colour.", cwd=repository / "Lab_2")
        require(self.memory_path.read_bytes() == original, output)
        self.report_pass("skip a paraphrase after restarting from another directory")

        output = self.run_chat("Correction: my favorite color is green now, not blue.")
        facts = self.read_saved_facts()
        require(
            len(facts) == 1 and facts[0]["id"] == original_id and "green" in facts[0]["text"].lower(),
            output,
        )
        self.report_pass("correct a fact without duplicating it")

        original = self.memory_path.read_bytes()
        output = self.run_chat(
            "Thanks!",
            "What is a class in C#?",
            "Remember that my favorite animal is a fox for this conversation only.",
        )
        require(self.memory_path.read_bytes() == original, output)
        self.report_pass("skip filler, generic questions, and session-only facts")

        previous_facts = self.read_saved_facts()
        output = self.run_chat("In general, I prefer short explanations.")
        global_facts = self.read_saved_facts()
        require(
            all(fact in global_facts for fact in previous_facts),
            "Adding an independent preference changed an existing fact.\n"
            + output + "\nSaved facts: " + json.dumps(global_facts, ensure_ascii=False),
        )
        require(
            len(global_facts) == len(previous_facts) + 1
            and any(fact["scope"] == "global" and "short" in fact["text"].lower() for fact in global_facts),
            "The global explanation preference was not saved.\n"
            + output + "\nSaved facts: " + json.dumps(global_facts, ensure_ascii=False),
        )
        output = self.run_chat("For project Orion only, I prefer detailed explanations.")
        facts = self.read_saved_facts()
        require(
            all(fact in facts for fact in global_facts),
            "A scoped exception changed an existing fact.\n"
            + output + "\nSaved facts: " + json.dumps(facts, ensure_ascii=False),
        )
        global_preference_exists = any(
            fact["scope"] == "global" and "short" in fact["text"].lower()
            for fact in facts
        )
        project_exception_exists = any(
            "orion" in fact["scope"].lower() and "detailed" in fact["text"].lower()
            for fact in facts
        )
        require(
            len(facts) == 3 and global_preference_exists and project_exception_exists,
            output + "\nSaved facts: " + json.dumps(facts, ensure_ascii=False),
        )
        self.report_pass("preserve global preferences and project exceptions")

        output = self.run_chat("What is my favorite color? Reply with just the color.")
        require(
            "green" in output.lower() and "blue" not in output.lower() and len(self.read_saved_facts()) == 3,
            output,
        )
        self.report_pass("recall a corrected fact in a new process")

        output = self.run_chat("Forget my favorite color.", "/memory")
        facts = self.read_saved_facts()
        require(len(facts) == 2 and all("color" not in fact["text"].lower() for fact in facts), output)
        self.report_pass("forget a fact through a natural-language request")

        removed_id = facts[0]["id"]
        output = self.run_chat(f"/forget {removed_id}", "/forget", "/clear", "/memory")
        facts = self.read_saved_facts()
        require(
            "Forgotten. Chat cleared." in output
            and "Use /forget <id>." in output
            and "Chat cleared. Memories kept." in output
            and len(facts) == 1
            and facts[0]["id"] != removed_id,
            output,
        )
        self.report_pass("forget by ID and keep remaining facts through /clear")

        corrupt_path = self.directory / "corrupt.json"
        corrupt_path.write_text("{broken", encoding="utf-8")
        output = self.run_chat("/memory", path=corrupt_path, expected_exit_code=1)
        require(
            "Cannot load memory:" in output and corrupt_path.read_text(encoding="utf-8") == "{broken",
            output,
        )
        self.report_pass("preserve a corrupt file without overwriting it")

        directory_path = self.directory / "directory.json"
        directory_path.mkdir()
        output = self.run_chat("/memory", path=directory_path, expected_exit_code=1)
        require(
            "Cannot load memory:" in output
            and not list(directory_path.iterdir())
            and not list(self.directory.glob("*.tmp")),
            output,
        )
        self.report_pass("reject a directory used as a memory file at startup")
        print(f"Passed: {self.passed} live checks.", flush=True)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    argparse.ArgumentParser(description="Run isolated Zeya terminal checks with Ollama.").parse_args()
    checks_directory = Path(tempfile.mkdtemp(prefix="ZeyaLiveChecks-"))
    try:
        TerminalChecks(checks_directory).run()
    finally:
        # Cleanup failures propagate; Python retains the original failure as exception context.
        for path in checks_directory.iterdir():
            if(path.is_file()):
                path.unlink()
            else:
                path.rmdir()
        checks_directory.rmdir()


if(__name__ == "__main__"):
    main()
