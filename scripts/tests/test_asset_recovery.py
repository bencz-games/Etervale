#!/usr/bin/env python3
"""Lightweight stdlib tests; run: python3 -m unittest discover -s scripts/tests -p 'test_asset_recovery.py'"""

from __future__ import annotations

import hashlib
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import audit_recovered_models as audit
import build_static_npc_inx as build


class RecoveryTests(unittest.TestCase):
    def test_recipes_reencode_to_original_cp949(self):
        for name, hashes in build.EXPECTED.items():
            recipe = (build.RECIPES / f"{name}.utf8.ini").read_text(encoding="utf8")
            self.assertEqual(hashlib.sha256(recipe.encode("cp949")).hexdigest(), hashes["ini"])
            self.assertIn("*서있기동작 0 1 반복", recipe)

    def test_case_mismatch_is_detected_through_parallel_directories(self):
        with tempfile.TemporaryDirectory() as temp:
            assets = pathlib.Path(temp)
            upper = assets / "char" / "npcXms"
            lower = assets / "char" / "npcxms"
            upper.mkdir(parents=True)
            lower.mkdir(parents=True)
            (upper / "zizzyadorii.smd").write_bytes(b"a")
            logical = "char/npcxms/zizzyadorii.smd"
            self.assertEqual(audit.status(assets, logical), ("case mismatch", "char/npcXms/zizzyadorii.smd"))
            (lower / "zizzyadorii.smd").write_bytes(b"a")
            self.assertEqual(audit.status(assets, logical), ("present", ""))
            self.assertEqual(audit.status(assets, "char/npcxms/nope.smd"), ("missing", ""))

    def test_output_refuses_to_overwrite_unrelated_data(self):
        with tempfile.TemporaryDirectory() as temp:
            path = pathlib.Path(temp) / "kadomatsu.inx"
            path.write_bytes(b"different")
            with self.assertRaises(FileExistsError):
                build.write_if_safe(path, b"expected", force=False)
            self.assertEqual(path.read_bytes(), b"different")
            build.write_if_safe(path, b"expected", force=True)
            self.assertEqual(path.read_bytes(), b"expected")


if __name__ == "__main__":
    unittest.main()
