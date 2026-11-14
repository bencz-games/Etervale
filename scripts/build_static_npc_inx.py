#!/usr/bin/env python3
"""Reproduce the two static NPC INX caches with the user's external PT Asset Tool.

No third-party compiler code or game asset is included here. This script reads
explicitly supplied compiler code, hashes the output, and never installs models.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import pathlib
import struct
import sys
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
RECIPES = ROOT / "tools" / "model-recovery" / "recipes"
EXPECTED = {
    "kadomatsu": {
        "ini": "e10a5151d9235e770e749f48b8f93693ae006409ca861abaf914f7c03dfa9a65",
        "inx": "3a2fed3cbc9634b41e3115688039397ecfdd214b50dc26899cf95e8474485191",
    },
    "zizzyadorii": {
        "ini": "a43429c18abcfb5a4fc48c4aa2308c173ed976596df6f18c74d72ade1ceff6d5",
        "inx": "7eed4bf48f7aace3f740bb794cd76eb9110c95f3bfe0195c390b9ee06f516948",
    },
}
MODULE_MEMBER = "pt_asset_tool/model_cache_compiler.py"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_compiler(source: pathlib.Path):
    """Load only the compiler module from an explicitly chosen trusted tool source."""
    if source.is_dir():
        code_path = source / MODULE_MEMBER
        code = code_path.read_bytes()
    elif source.suffix.lower() == ".zip":
        with zipfile.ZipFile(source) as archive:
            code = archive.read(MODULE_MEMBER)
    elif source.is_file() and source.name == "model_cache_compiler.py":
        code = source.read_bytes()
    else:
        raise ValueError("--compiler must be an extracted PT Asset Tool directory, its ZIP, or model_cache_compiler.py")

    # Load the separately supplied code; it is not vendored or installed by this project.
    with tempfile.TemporaryDirectory(prefix="etervale-model-compiler-") as temp:
        module_path = pathlib.Path(temp) / "model_cache_compiler.py"
        module_path.write_bytes(code)
        spec = importlib.util.spec_from_file_location("etervale_external_model_cache_compiler", module_path)
        if spec is None or spec.loader is None:
            raise ValueError("Could not load the external compiler")
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        try:
            spec.loader.exec_module(module)
        finally:
            sys.modules.pop(spec.name, None)
        return module


def build(compiler, name: str) -> tuple[bytes, bytes]:
    text = (RECIPES / f"{name}.utf8.ini").read_text(encoding="utf-8")
    ini = text.encode("cp949")
    if sha256(ini) != EXPECTED[name]["ini"]:
        raise ValueError(f"{name}: recipe differs from the CP949 version validated in 2026-11")

    logical_ini = f"char/npcxms/{name}.ini"
    inx, metadata = compiler.compile_model_cache(
        logical_ini, ini, item_catalog={}, skill_catalog={}
    )
    if len(inx) != 95_268 or sha256(inx) != EXPECTED[name]["inx"]:
        raise ValueError(f"{name}: INX does not match validated bytes; check compiler version and checksum path policy")
    motion_count = struct.unpack_from("<I", inx, 0x18C + 512 * 172)[0]
    inspected = compiler.inspect_model_cache(inx, logical_cache=f"char/npcxms/{name}.inx")
    if not inspected["valid"] or not metadata["semantic_roundtrip_valid"] or motion_count != 11:
        raise ValueError(f"{name}: INX structural/keyword validation failed")
    return ini, inx


def write_if_safe(path: pathlib.Path, data: bytes, force: bool) -> None:
    if path.exists():
        if not path.is_file():
            raise FileExistsError(f"{path} exists and is not a regular file")
        if path.read_bytes() == data:
            print(f"unchanged {path}")
            return
        if not force:
            raise FileExistsError(f"{path} has different contents; refusing overwrite (use --force only after backup)")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    print(f"wrote {path} ({len(data)} bytes, SHA256 {sha256(data)})")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler", type=pathlib.Path, required=True, help="trusted PT Asset Tool directory, ZIP or compiler .py")
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "build" / "model-recovery", help="staging directory; NOT game assets")
    parser.add_argument("--model", choices=[*EXPECTED, "all"], default="all")
    parser.add_argument("--verify-only", action="store_true", help="compile and verify hashes without writing files")
    parser.add_argument("--force", action="store_true", help="overwrite different files in the output directory")
    args = parser.parse_args(argv)
    try:
        compiler = load_compiler(args.compiler)
        selected = list(EXPECTED) if args.model == "all" else [args.model]
        built = [(name, *build(compiler, name)) for name in selected]
        targets = [(args.output / f"{name}{extension}", data)
                   for name, ini, inx in built for extension, data in ((".ini", ini), (".inx", inx))]
        if not args.verify_only and not args.force:
            # Preflight the whole set: a mismatch cannot leave a partially rebuilt pair.
            for path, data in targets:
                if path.exists() and (not path.is_file() or path.read_bytes() != data):
                    raise FileExistsError(f"{path} differs; refusing to overwrite without --force")
        for name, ini, inx in built:
            print(f"{name}: verified 95,268-byte INX, 11 declared motions, synthetic static Stand 0..1")
        if not args.verify_only:
            for path, data in targets:
                write_if_safe(path, data, args.force)
        return 0
    except (ValueError, OSError, KeyError, zipfile.BadZipFile, ImportError) as error:
        parser.exit(1, f"error: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
