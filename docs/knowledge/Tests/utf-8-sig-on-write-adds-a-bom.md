---
description: Python's utf-8-sig ADDS a BOM on write while merely tolerating one on read, so read-modify-write silently changes a file's bytes - seven incidents on one ticket, none caught by eye
applies-to:
  - docs/knowledge/Tests/a-review-of-a-working-tree-must-name-its-hash.md
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — `utf-8-sig` is asymmetric. On **read** it strips a BOM if present and tolerates its
absence; on **write** it **adds** one unconditionally. So the natural scripted-edit shape

```python
s = io.open(path, encoding="utf-8-sig").read()
io.open(path, "w", encoding="utf-8-sig").write(s.replace(old, new))
```

silently gives a BOM to every file that did not have one. The same call also translates `\n` to `\r\n`
on Windows unless `newline=""` is passed, so one line of boilerplate changes two things about the bytes
and neither is the edit you meant.

The safe form reads bytes, decides from them, and writes bytes:

```python
raw = io.open(path, "rb").read()
bom = raw.startswith(b"\xef\xbb\xbf")
s = raw.decode("utf-8-sig")
...
io.open(path, "wb").write(s.encode("utf-8-sig") if bom else s.encode("utf-8"))
```

**Why it is this way** — it is a tooling default, not carelessness, and that is the whole reason the
record is worth keeping. `utf-8-sig` and `utf-8` differ by four characters in source and the wrong one
is the one that looks more careful. ENG-91853 produced **seven files across four occasions**, two
people, three repositories:

| repository | files | occasion |
|---|---|---|
| clio | `AGENTS.md`, `spec/ai-business-process-generation/ai-bp-connection-rules.md` | one scripted patch pass |
| clio | `IProcessGraphValidator.cs`, `ValidateProcessGraphPrompt.cs`, `ValidateProcessGraphToolE2ETests.cs` | one contract-surface pass |
| clio-knowledge | `bundle-source.json` | one manifest edit — broke four bundle tests |
| crt-process-builder | `Formulas/ConditionParameterNames.cs` | a mutation restore, announced as byte-identical |

**State the unit, because the two numbers argue differently.** Seven *files* reads as "this happens
constantly". Four *occasions* reads as "one bad default fired four times, and two of those were a single
scripted pass touching several files at once" — which is the accurate picture of the risk and the
weaker-sounding number. A count drifts toward the louder unit whenever nobody names it.

**What breaks if you ignore it** — nothing visible. A BOM is invisible in every editor and every code
review; it appears only as a one-line first hunk in a diff, which reads as noise. On this ticket it
surfaced three different ways and never by looking:

- four bundle tests failed, because the manifest's parser rejected the byte
- a full-file byte comparison against `HEAD` found two more that nothing else would have
- git reported the tree **clean** for the line-ending half, because `core.autocrlf` normalises it away

That last one matters most: `git status` is not a check for this. A file can differ from what you had,
match the blob after normalisation, and report clean. Verify with a byte comparison against `HEAD`, not
with git's own view:

```bash
for f in $(git diff --name-only); do
  printf "%-60s HEAD=%s now=%s\n" "$f" \
    "$(git show HEAD:"$f" | head -c3 | xxd -p)" "$(head -c3 "$f" | xxd -p)"
done
```

Related: [[a-review-of-a-working-tree-must-name-its-hash]] — the same normalisation is why a worktree
hash is not a valid anchor.
