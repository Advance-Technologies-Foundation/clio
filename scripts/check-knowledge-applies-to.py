#!/usr/bin/env python3
"""Advisory checks for the docs/knowledge/ knowledge base.

Two reports, both advisory - this script never fails a pull request:

1. *Touched records*: records whose ``applies-to`` intersects the diff, so the author is told a
   recorded fact covers code they just changed and has to be updated or deleted (AGENTS.md,
   "Knowledge base").
2. *Dead paths*: records whose ``applies-to`` points at a path that no longer exists on disk. This
   is the check whose absence let the retired workspace diary accumulate 365 dead references.

Usage::

    python3 scripts/check-knowledge-applies-to.py --base origin/master
    python3 scripts/check-knowledge-applies-to.py --dead-only
    python3 scripts/check-knowledge-applies-to.py --base <sha> --markdown report.md

``applies-to`` entries are literal repository-relative paths, or directory prefixes ending in
``/``. Globs are not expanded on purpose: a literal path either exists or it does not, which is
what makes the dead-path report trustworthy.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from dataclasses import dataclass, field

KNOWLEDGE_DIR = "docs/knowledge"
MARKER = "<!-- knowledge-applies-to-check -->"


@dataclass
class Record:
    path: str
    description: str = ""
    ticket: str = ""
    date: str = ""
    applies_to: list[str] = field(default_factory=list)
    problems: list[str] = field(default_factory=list)


def repo_root() -> str:
    out = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True, text=True, check=True,
    )
    return out.stdout.strip()


def parse_front_matter(text: str) -> tuple[dict[str, object], list[str]]:
    """Minimal YAML front-matter reader: scalars and ``- item`` lists only.

    Deliberately not PyYAML: the workflow runs on a bare runner with no pip install step, and the
    schema in docs/knowledge/README.md is small enough that a real YAML parser buys nothing.
    """
    problems: list[str] = []
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return {}, ["no YAML front matter (the file must start with '---')"]
    try:
        end = next(i for i in range(1, len(lines)) if lines[i].strip() == "---")
    except StopIteration:
        return {}, ["front matter is never closed with '---'"]

    data: dict[str, object] = {}
    key: str | None = None
    for raw in lines[1:end]:
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        if raw.lstrip().startswith("- "):
            if key is None:
                problems.append(f"list item outside of any key: {raw.strip()}")
                continue
            data.setdefault(key, [])
            if not isinstance(data[key], list):
                data[key] = []
            data[key].append(raw.split("- ", 1)[1].strip().strip("'\""))
            continue
        if ":" not in raw:
            problems.append(f"unparsable front-matter line: {raw.strip()}")
            continue
        key, _, value = raw.partition(":")
        key = key.strip()
        value = value.strip().strip("'\"")
        data[key] = value if value else []
    return data, problems


def scalar(value: object) -> str:
    """Front-matter scalar as a string; an empty or list-valued key reads as absent."""
    return value.strip() if isinstance(value, str) else ""


def load_records(root: str) -> list[Record]:
    base = os.path.join(root, KNOWLEDGE_DIR)
    records: list[Record] = []
    for dirpath, _dirnames, filenames in os.walk(base):
        for name in sorted(filenames):
            if not name.endswith(".md") or name.upper() == "README.MD":
                continue
            abs_path = os.path.join(dirpath, name)
            rel = os.path.relpath(abs_path, root).replace(os.sep, "/")
            with open(abs_path, encoding="utf-8") as handle:
                data, problems = parse_front_matter(handle.read())
            applies = data.get("applies-to") or []
            if isinstance(applies, str):
                applies = [applies] if applies else []
            record = Record(
                path=rel,
                # A key present but empty parses as [] (see parse_front_matter), which must read as
                # absent rather than as the string "[]".
                description=scalar(data.get("description")),
                ticket=scalar(data.get("ticket")),
                date=scalar(data.get("date")),
                applies_to=[a for a in applies if a],
                problems=problems,
            )
            if not record.description:
                record.problems.append("missing 'description'")
            if not record.applies_to:
                record.problems.append("missing 'applies-to' (at least one path is required)")
            if not record.date:
                record.problems.append("missing 'date'")
            for entry in record.applies_to:
                if entry.startswith("/") or (len(entry) > 1 and entry[1] == ":"):
                    record.problems.append(f"absolute path in applies-to: {entry}")
                if any(ch in entry for ch in "*?["):
                    record.problems.append(f"glob in applies-to (literal paths only): {entry}")
            records.append(record)
    return sorted(records, key=lambda r: r.path)


def changed_files(base: str) -> tuple[list[str], str | None]:
    """Files changed against ``base``. Returns (files, error)."""
    merge_base = subprocess.run(
        ["git", "merge-base", base, "HEAD"], capture_output=True, text=True,
    )
    if merge_base.returncode != 0:
        return [], (
            f"cannot resolve a merge base with '{base}': {merge_base.stderr.strip()} "
            "(a shallow clone is the usual cause - fetch with depth 0)"
        )
    diff = subprocess.run(
        ["git", "diff", "--name-only", f"{merge_base.stdout.strip()}...HEAD"],
        capture_output=True, text=True,
    )
    if diff.returncode != 0:
        return [], f"git diff failed: {diff.stderr.strip()}"
    files = {line for line in diff.stdout.splitlines() if line.strip()}
    # Uncommitted work counts too, so that a local run before the commit reports the same records
    # the pull request will. On a CI checkout the working tree is clean, so this adds nothing.
    for extra in (["git", "diff", "--name-only", "HEAD"],
                  ["git", "diff", "--name-only", "--cached", "HEAD"],
                  ["git", "ls-files", "--others", "--exclude-standard"]):
        result = subprocess.run(extra, capture_output=True, text=True)
        if result.returncode == 0:
            files.update(line for line in result.stdout.splitlines() if line.strip())
    return sorted(files), None


def matches(entry: str, changed: list[str]) -> list[str]:
    if entry.endswith("/"):
        return [c for c in changed if c.startswith(entry)]
    return [c for c in changed if c == entry]


def dead_paths(root: str, record: Record) -> list[str]:
    return [
        entry for entry in record.applies_to
        if not os.path.exists(os.path.join(root, entry))
    ]


def build_report(root: str, records: list[Record], base: str | None, dead_only: bool) -> tuple[str, int]:
    lines: list[str] = [MARKER, "## Knowledge base check", ""]
    touched_count = 0

    if not records:
        lines += [
            f"No records under `{KNOWLEDGE_DIR}/` yet, so there is nothing to intersect with this diff.",
            "",
        ]

    if not dead_only and base and records:
        changed, error = changed_files(base)
        if error:
            lines += [f"> Diff intersection skipped: {error}", ""]
        else:
            hits: list[tuple[Record, list[str]]] = []
            for record in records:
                touched = sorted({m for entry in record.applies_to for m in matches(entry, changed)})
                if touched:
                    hits.append((record, touched))
            touched_count = len(hits)
            if hits:
                lines += [
                    f"This pull request touches code covered by **{len(hits)}** knowledge record(s).",
                    "Update or delete each one in this pull request if the fact changed"
                    " - and if nothing changed, no action is needed.",
                    "",
                ]
                for record, touched in hits:
                    lines.append(f"- [`{record.path}`]({record.path}) - {record.description}")
                    for f in touched:
                        lines.append(f"  - touched: `{f}`")
                lines.append("")
            else:
                lines += [
                    "No knowledge record covers the files changed in this pull request.",
                    "",
                ]

    dead = [(r, d) for r in records if (d := dead_paths(root, r))]
    if dead:
        lines += [
            f"### Records pointing at paths that no longer exist ({len(dead)})",
            "",
            "Either the path moved (fix `applies-to`) or the fact is gone (delete the record).",
            "",
        ]
        for record, missing in dead:
            lines.append(f"- [`{record.path}`]({record.path})")
            for entry in missing:
                lines.append(f"  - missing: `{entry}`")
        lines.append("")

    malformed = [r for r in records if r.problems]
    if malformed:
        lines += [f"### Records that do not match the schema ({len(malformed)})", ""]
        for record in malformed:
            lines.append(f"- [`{record.path}`]({record.path})")
            for problem in record.problems:
                lines.append(f"  - {problem}")
        lines.append("")

    if records and not dead and not malformed and (dead_only or not base):
        lines += [f"All {len(records)} record(s) resolve and match the schema.", ""]

    lines += [
        "<sub>Advisory only - this check never fails the build. See "
        "[`docs/knowledge/README.md`](docs/knowledge/README.md).</sub>",
    ]
    return "\n".join(lines), touched_count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", help="base ref to diff against, e.g. origin/master")
    parser.add_argument("--dead-only", action="store_true", help="skip the diff intersection")
    parser.add_argument("--markdown", help="also write the report to this file")
    args = parser.parse_args()

    root = repo_root()
    records = load_records(root)
    report, _touched = build_report(root, records, args.base, args.dead_only)
    print(report)
    if args.markdown:
        with open(args.markdown, "w", encoding="utf-8") as handle:
            handle.write(report + "\n")
    return 0  # advisory by contract: never fail the caller


if __name__ == "__main__":
    sys.exit(main())
