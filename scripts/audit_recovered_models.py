#!/usr/bin/env python3
"""Inventory six recovered model cases and Christmas folder-case discrepancies.

This is a read-only file/path audit; it does not infer that an INX has usable clips.
"""

from __future__ import annotations

import argparse
import hashlib
import pathlib

# Logical names are lower-case as resolved by the Etervale asset loader on Linux.
CHECKS = {
    "Hellbeast / Red Gladiator": [
        "char/monster/mpthell/mpthellbeast.inx",
        "char/monster/mpthell/mpthellbeast.smd",
        "char/monster/mpthell/mpthellbeast.smb",
    ],
    "Villain / Son of Battle": [
        "char/npc/villain/villain.inx",
        "char/npc/villain/villain.smd",
        "char/npc/villain/villain.smb",
    ],
    "Lava Golem": ["char/monster/rptlavagolem1/rptlavagolem1.inx"],
    "Kadomatsu": [
        "char/npcxms/kadomatsu.inx", "char/npcxms/kadomatsu.smd", "char/npcxms/kadomatsu.smb",
    ],
    "Zinjya / Zizzyadorii": [
        "char/npcxms/zizzyadorii.inx", "char/npcxms/zizzyadorii.smd", "char/npcxms/zizzyadorii.smb",
    ],
    "Mythcreature (not placed in imported fields)": [
        "char/npc/mythcreature_round_4/mythcreature_round_4.inx",
        "char/npc/mythcreature_round_4/mythcreature_round_4.smd",
        "char/npc/mythcreature_round_4/mythcreature_round_4.smb",
    ],
    "Christmas Longbo (case check)": [
        "char/npcxms/ChTree_Longbo.inx",
        "char/npcxms/ChTree_Longbo01.smd",
        "char/npcxms/ChTree_Longbo01.smb",
    ],
}
GENERATED_HASHES = {
    "char/npcxms/kadomatsu.inx": "3a2fed3cbc9634b41e3115688039397ecfdd214b50dc26899cf95e8474485191",
    "char/npcxms/zizzyadorii.inx": "7eed4bf48f7aace3f740bb794cd76eb9110c95f3bfe0195c390b9ee06f516948",
}


def casefold_matches(root: pathlib.Path, relative: str) -> list[pathlib.Path]:
    """Search just the case-folded path segments, including parallel npcXms/npcxms trees."""
    candidates = [root]
    for part in pathlib.PurePosixPath(relative).parts:
        following = []
        for parent in candidates:
            if not parent.is_dir():
                continue
            try:
                following.extend(p for p in parent.iterdir() if p.name.casefold() == part.casefold())
            except PermissionError:
                continue
        candidates = following
        if not candidates:
            break
    return [path for path in candidates if path.is_file()]


def status(root: pathlib.Path, relative: str) -> tuple[str, str]:
    target = root.joinpath(*pathlib.PurePosixPath(relative).parts)
    if target.is_file():
        note = ""
        expected = GENERATED_HASHES.get(relative)
        if expected:
            with target.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            note = "reconstructed INX SHA256 match" if digest == expected else "INX differs from the reconstructed cache; inspect --motions"
        return "present", note
    candidates = casefold_matches(root, relative)
    if candidates:
        return "case mismatch", ", ".join(str(p.relative_to(root)) for p in candidates[:5])
    return "missing", ""


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=pathlib.Path, required=True, help="root directory containing char/")
    parser.add_argument("--strict", action="store_true", help="return failure if a required file is absent or has case mismatch (Mythcreature exempt)")
    args = parser.parse_args(argv)
    if not args.assets.is_dir():
        parser.error("--assets must name an existing directory")

    missing_required = 0
    for label, files in CHECKS.items():
        print(f"\n{label}")
        for relative in files:
            found, note = status(args.assets, relative)
            print(f"  {found:13} {relative}" + (f"  [{note}]" if note else ""))
            if found != "present" and not label.startswith("Mythcreature"):
                missing_required += 1
    print(f"\nRequired files not at their exact logical path: {missing_required}")
    print("Presence alone cannot establish usable INX motions; validate with PristonTale.AssetDump --motions.")
    return 1 if args.strict and missing_required else 0


if __name__ == "__main__":
    raise SystemExit(main())
