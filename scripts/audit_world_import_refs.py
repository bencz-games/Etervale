#!/usr/bin/env python3
"""Read-only inventory of WorldImport unresolved reference rows in an existing SQLite DB.

Unlike `dotnet run --project tools/PristonTale.WorldImport -- verify`, this tool
never runs migrations. No database file is created if the supplied path is wrong.
"""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from contextlib import closing
import json
from pathlib import Path
import sqlite3
import sys


TABLE = "world_import_unresolved_refs"


def is_empty_shop_slot(row: dict[str, str]) -> bool:
    """The importer records the authored Korean '없음' slot as 'none', not an item code."""
    return row["kind"] == "npc_shop" and row["reference"] == "none"


def read_references(db_path: Path) -> list[dict[str, str]]:
    """Return the importer's recorded problems without creating or modifying the DB."""
    path = db_path.expanduser()
    if not path.is_file():
        raise FileNotFoundError(f"SQLite database not found: {path}")

    # mode=ro refuses write statements and prevents accidental database creation.
    # Do not set immutable=1: it could hide committed transactions still in WAL.
    uri = path.resolve().as_uri() + "?mode=ro"
    with closing(sqlite3.connect(uri, uri=True)) as connection:
        connection.execute("PRAGMA query_only = ON")
        try:
            rows = connection.execute(
                f"SELECT kind, owner_key, raw_reference, details_json FROM {TABLE} "
                "ORDER BY kind, owner_key, raw_reference, id"
            ).fetchall()
        except sqlite3.OperationalError as exc:
            raise ValueError(f"Missing or incompatible {TABLE} table: {exc}") from exc

    return [dict(zip(("kind", "owner", "reference", "details_json"), row)) for row in rows]


def summarize(rows: list[dict[str, str]], limit: int | None = 10) -> dict:
    """Keep counts and distinct references separated; do not equate 80 rows to 80 codes."""
    groups = defaultdict(list)
    for row in rows:
        groups[row["kind"]].append(row)

    summaries = []
    for kind, items in sorted(groups.items()):
        # A reference may have multiple owners; preserve owners in the visible list.
        pairs = Counter((item["reference"], item["owner"]) for item in items)
        ordered = sorted(pairs.items(), key=lambda entry: (-entry[1], entry[0]))
        selected = ordered if limit is None else ordered[:limit]
        summaries.append({
            "kind": kind,
            "rows": len(items),
            "distinct_references": len({item["reference"] for item in items}),
            "distinct_owner_reference_pairs": len(pairs),
            "entries": [
                {"reference": ref, "owner": owner, "rows": count}
                for (ref, owner), count in selected
            ],
            "remaining_pairs": len(ordered) - len(selected),
        })
    empty_shop_slots = sum(is_empty_shop_slot(item) for item in rows)
    return {
        "total_rows": len(rows),
        "empty_shop_slots": empty_shop_slots,
        "other_unresolved_rows": len(rows) - empty_shop_slots,
        "groups": summaries,
    }


def render(report: dict, details: list[dict[str, str]] | None = None) -> str:
    lines = [
        f"WorldImport unresolved reference rows: {report['total_rows']}",
        f"Known empty NPC shop slots: {report['empty_shop_slots']}; "
        f"other unresolved rows to investigate: {report['other_unresolved_rows']}",
    ]
    if not report["groups"]:
        lines.append("No unresolved rows recorded. This does not validate model assets or INX motions.")
        return "\n".join(lines)

    for group in report["groups"]:
        lines.append(
            f"\n{group['kind']}: {group['rows']} rows, "
            f"{group['distinct_references']} distinct references, "
            f"{group['distinct_owner_reference_pairs']} owner/reference pairs"
        )
        for entry in group["entries"]:
            times = f" x{entry['rows']}" if entry["rows"] > 1 else ""
            hint = " [empty shop slot]" if group["kind"] == "npc_shop" and entry["reference"] == "none" else ""
            lines.append(f"  {entry['reference']}  [owner: {entry['owner']}]{times}{hint}")
        if group["remaining_pairs"]:
            lines.append(f"  ... {group['remaining_pairs']} more owner/reference pairs; use --all")
    if details is not None:
        lines.append("\nNon-placeholder source details:")
        for item in details:
            if is_empty_shop_slot(item):
                continue
            lines.append(
                f"  {item['kind']}  {item['reference']}  [owner: {item['owner']}] "
                f"details_json={item['details_json']}"
            )
    lines.append("\nRead-only report; no catalog rows were changed.")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sqlite", type=Path, required=True, help="Existing WorldImport SQLite database")
    parser.add_argument("--all", action="store_true", help="Show every owner/reference pair, not only the first 10 per kind")
    parser.add_argument("--json", action="store_true", help="Print grouped report in JSON format")
    parser.add_argument("--details", action="store_true", help="Print raw details_json for non-placeholder unresolved rows")
    args = parser.parse_args(argv)

    try:
        rows = read_references(args.sqlite)
        report = summarize(rows, limit=None if args.all else 10)
    except (FileNotFoundError, sqlite3.Error, ValueError, OSError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(render(report, details=rows if args.details else None))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
