"""Tests for the WorldImport unresolved-reference read-only report."""

import importlib.util
import io
from contextlib import closing, redirect_stderr, redirect_stdout
from pathlib import Path
import sqlite3
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "audit_world_import_refs.py"
SPEC = importlib.util.spec_from_file_location("audit_world_import_refs", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class WorldImportAuditTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.db = Path(self.tmp.name) / "pristontale.db"
        with closing(sqlite3.connect(self.db)) as db, db:
            db.execute("""CREATE TABLE world_import_unresolved_refs (
                id INTEGER PRIMARY KEY, kind TEXT, owner_key TEXT,
                raw_reference TEXT, details_json TEXT)""")
            db.executemany(
                "INSERT INTO world_import_unresolved_refs (kind, owner_key, raw_reference, details_json) VALUES (?,?,?,?)",
                [
                    ("npc_shop", "NPC_A", "WA999", "{}"),
                    ("npc_shop", "NPC_B", "WA999", "{}"),
                    ("npc_shop", "NPC_A", "WA999", "{}"),
                    ("field_fixed_npc", "field-1", "absent.npc", "{}"),
                    ("monster_drop", "monster-1", "XX123", "{}"),
                ],
            )

    def test_counts_and_owners_do_not_confuse_rows_with_unique_codes(self):
        report = MODULE.summarize(MODULE.read_references(self.db))
        self.assertEqual(report["total_rows"], 5)
        shop = next(group for group in report["groups"] if group["kind"] == "npc_shop")
        self.assertEqual(shop["rows"], 3)
        self.assertEqual(shop["distinct_references"], 1)
        self.assertEqual(shop["distinct_owner_reference_pairs"], 2)
        self.assertEqual(shop["entries"][0], {"reference": "WA999", "owner": "NPC_A", "rows": 2})

    def test_shop_placeholders_are_not_missing_items(self):
        rows = [
            {"kind": "npc_shop", "owner": "Vendor", "reference": "none", "details_json": '{"Tab":0,"Ordinal":7}'},
            {"kind": "npc_shop", "owner": "Vendor", "reference": "none", "details_json": '{"Tab":0,"Ordinal":8}'},
            {"kind": "npc_shop", "owner": "Vendor", "reference": "WA999", "details_json": "{}"},
            {"kind": "monster_drop", "owner": "Bad Wolf", "reference": "or228", "details_json": '{"Line":0}'},
        ]
        report = MODULE.summarize(rows)
        self.assertEqual(report["total_rows"], 4)
        self.assertEqual(report["empty_shop_slots"], 2)
        self.assertEqual(report["other_unresolved_rows"], 2)
        visible = MODULE.render(report, details=rows)
        self.assertIn("other unresolved rows to investigate: 2", visible)
        self.assertEqual(visible.count("details_json="), 2)
        self.assertIn("or228", visible)

    def test_command_is_read_only(self):
        with closing(sqlite3.connect(self.db)) as db, db:
            db.execute("PRAGMA user_version=17")
        before = self.db.read_bytes()
        with redirect_stdout(io.StringIO()) as stream:
            self.assertEqual(MODULE.main(["--sqlite", str(self.db), "--all"]), 0)
        self.assertIn("WorldImport unresolved reference rows: 5", stream.getvalue())
        self.assertEqual(before, self.db.read_bytes())

    def test_details_option_keeps_raw_source_fields(self):
        with closing(sqlite3.connect(self.db)) as db, db:
            db.execute(
                "INSERT INTO world_import_unresolved_refs (kind, owner_key, raw_reference, details_json) "
                "VALUES (?, ?, ?, ?)",
                ("npc_shop", "Vendor", "none", '{"Tab":1,"Ordinal":5}'),
            )
            db.execute(
                "UPDATE world_import_unresolved_refs SET details_json = ? WHERE raw_reference = ?",
                ('{"Line":2,"Weight":13}', "XX123"),
            )
        with redirect_stdout(io.StringIO()) as out:
            self.assertEqual(MODULE.main(["--sqlite", str(self.db), "--details"]), 0)
        self.assertIn("Known empty NPC shop slots: 1; other unresolved rows to investigate: 5", out.getvalue())
        self.assertIn('details_json={"Line":2,"Weight":13}', out.getvalue())
        self.assertNotIn('details_json={"Tab":1,"Ordinal":5}', out.getvalue())

    def test_wrong_database_path_is_not_created(self):
        missing = Path(self.tmp.name) / "nonexistent.sqlite"
        with redirect_stderr(io.StringIO()):
            self.assertEqual(MODULE.main(["--sqlite", str(missing)]), 1)
        self.assertFalse(missing.exists())

    def test_missing_unresolved_table_is_reported(self):
        with closing(sqlite3.connect(self.db)) as db, db:
            db.execute("DROP TABLE world_import_unresolved_refs")
        with redirect_stderr(io.StringIO()) as errors:
            self.assertEqual(MODULE.main(["--sqlite", str(self.db)]), 1)
        self.assertIn("Missing or incompatible", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
