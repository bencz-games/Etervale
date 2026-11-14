"""No .NET install or game assets required: exercise the shell launchers with a stub CLI."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[2]
SCRIPTS = ("run-server.sh", "run-client.sh", "create-account.sh")


class LocalLauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "copy with spaces"
        (self.root / "scripts").mkdir(parents=True)
        (self.root / "bin").mkdir()
        for script in SCRIPTS:
            shutil.copy2(REPO / "scripts" / script, self.root / "scripts" / script)

        stub = self.root / "bin" / "dotnet"
        stub.write_text("""#!/usr/bin/env bash
set -euo pipefail
printf '%s\\n' "$PWD" > "$CAPTURE_CWD"
printf '%s\\n' "$@" > "$CAPTURE_ARGS"
printf 'provider=%s\\ndb=%s\\n' "${Database__Provider-}" "${Database__SqlitePath-}" > "$CAPTURE_ENV"
for arg in "$@"; do
  if [[ "$arg" == '--password-stdin' ]]; then
    IFS= read -r secret
    printf '%s' "$secret" > "$CAPTURE_PASSWORD"
  fi
done
""")
        stub.chmod(0o755)
        self.env = os.environ.copy()
        self.env.update({
            "PATH": str(self.root / "bin") + os.pathsep + os.environ.get("PATH", ""),
            "CAPTURE_CWD": str(self.root / "cwd"),
            "CAPTURE_ARGS": str(self.root / "args"),
            "CAPTURE_ENV": str(self.root / "env"),
            "CAPTURE_PASSWORD": str(self.root / "password"),
            "PT_PG_CONN": "Host=example;Password=should-not-override-explicit-sqlite",
        })

    def prepare_db(self):
        (self.root / "data").mkdir(exist_ok=True)
        (self.root / "data" / "pristontale.db").write_text("existing sqlite sentinel")

    def prepare_assets(self):
        (self.root / "assets" / "Field").mkdir(parents=True)
        (self.root / "assets" / "char").mkdir(parents=True)

    def run_script(self, script, *, input_text=None, args=()):
        return subprocess.run(
            ["bash", str(self.root / "scripts" / script), *args],
            cwd="/", env=self.env, input=input_text, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
        )

    def read_args(self):
        return (self.root / "args").read_text().splitlines()

    def test_scripts_pass_bash_syntax(self):
        for name in SCRIPTS:
            with self.subTest(name=name):
                subprocess.run(["bash", "-n", str(self.root / "scripts" / name)], check=True)

    def test_server_refuses_missing_database_without_running_dotnet(self):
        self.prepare_assets()
        p = self.run_script("run-server.sh")
        self.assertNotEqual(0, p.returncode)
        self.assertIn("Database not found", p.stderr)
        self.assertFalse((self.root / "args").exists())

    def test_server_uses_exact_existing_sqlite_and_repo_directory(self):
        self.prepare_db()
        self.prepare_assets()
        p = self.run_script("run-server.sh")
        self.assertEqual(0, p.returncode, p.stderr)
        self.assertEqual(str(self.root), (self.root / "cwd").read_text().strip())
        self.assertEqual("run", self.read_args()[0])
        self.assertIn("Server/PristonTale.Server", self.read_args())
        self.assertIn("provider=Sqlite", (self.root / "env").read_text())
        self.assertIn(f"db={self.root / 'data' / 'pristontale.db'}", (self.root / "env").read_text())
        self.assertEqual("existing sqlite sentinel", (self.root / "data" / "pristontale.db").read_text())

    def test_client_refuses_missing_assets(self):
        p = self.run_script("run-client.sh")
        self.assertNotEqual(0, p.returncode)
        self.assertIn("Client assets are missing", p.stderr)
        self.assertFalse((self.root / "args").exists())

    def test_client_runs_from_checkout_not_invocation_directory(self):
        self.prepare_assets()
        p = self.run_script("run-client.sh")
        self.assertEqual(0, p.returncode, p.stderr)
        self.assertEqual(str(self.root), (self.root / "cwd").read_text().strip())
        self.assertIn("Client/PristonTale", self.read_args())

    def test_account_refuses_missing_database(self):
        p = self.run_script("create-account.sh", args=("tester",))
        self.assertNotEqual(0, p.returncode)
        self.assertIn("Database not found", p.stderr)
        self.assertFalse((self.root / "args").exists())

    def test_account_password_goes_to_stdin_not_process_args(self):
        self.prepare_db()
        p = self.run_script("create-account.sh", args=("tester",), input_text="pass words!'$\\\\\npass words!'$\\\\\n")
        self.assertEqual(0, p.returncode, p.stderr)
        args = self.read_args()
        self.assertIn("--password-stdin", args)
        self.assertIn("--sqlite", args)
        self.assertIn(str(self.root / "data" / "pristontale.db"), args)
        self.assertNotIn("pass words", " ".join(args))
        self.assertEqual("pass words!'$\\\\", (self.root / "password").read_text())
        self.assertEqual("existing sqlite sentinel", (self.root / "data" / "pristontale.db").read_text())

    def test_account_does_not_run_on_mismatched_or_blank_password(self):
        self.prepare_db()
        for input_text in ("one\\ntwo\\n", "\\n\\n"):
            with self.subTest(input_text=input_text):
                p = self.run_script("create-account.sh", args=("tester",), input_text=input_text)
                self.assertNotEqual(0, p.returncode)
                self.assertFalse((self.root / "args").exists())

    def test_unexpected_arguments_rejected(self):
        self.prepare_db()
        self.prepare_assets()
        for script in ("run-server.sh", "run-client.sh"):
            with self.subTest(script=script):
                p = self.run_script(script, args=("--unexpected",))
                self.assertEqual(2, p.returncode)
                self.assertFalse((self.root / "args").exists())


if __name__ == "__main__":
    unittest.main()
