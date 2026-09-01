# Runbook — the ordered commands

Copy-paste sequence for a run. The *why* behind each step is in
[environment.md](environment.md); this file exists so a run costs minutes instead of an afternoon of
rediscovery. Values shown are the ones a real run used — substitute your own.

Set these once per run:

```
FEATURE=eng-95891-formula-expressions
ISSUE=ENG-95891
ENV=krestov-test
REPO=C:/Projects/clio-eng95891                # clio worktree on the branch under test
KB=C:/Projects/clio-knowledge                 # guidance checkout
CLIO="dotnet $REPO/clio/bin/Release/net8.0/clio.dll"
```

## Mode `agent`

### 1. Preflight

```
git -C $REPO fetch origin && git -C $REPO log --oneline -1 && git -C $REPO rev-list --count HEAD..origin/master
```

Behind master → merge or accept it, and say which in the report.

```
$CLIO get-info -e $ENV
```

**Probe liveness, do not trust the alias list.** TS1 stands are recycled constantly: a configured
alias answers `404`, `Could not connect`, or *"ApplicationInfoService returned an unexpected
response"* long after it stops existing. In one run five of six candidate aliases were dead.

```
$CLIO list-packages -e $ENV | grep -i -E 'CrtProcessBuilder|cliogate'
```

Compare with what the branch ships (`git -C $REPO log --oneline -1 -- clio/CrtProcessBuilder/CrtProcessBuilder.gz`).
Stand **ahead** is fine — install is a no-op by design, use `--skip-install`. Stand **behind** the
version the cases require is a stop.

```
dotnet build $REPO/clio/clio.csproj -c Release -f net8.0
```

Rebuild after every branch move: the MCP surface the executor talks to comes from this build.

### 2. Local guidance — the five steps, in this order

```
cp "$LOCALAPPDATA/creatio/clio/appsettings.json" "$LOCALAPPDATA/creatio/clio/appsettings.json.bpskills-backup"
```

```
$CLIO experimental --name knowledge-allow-unsequenced --enable
```

Edit `%LOCALAPPDATA%\creatio\clio\appsettings.json`, replacing the `creatio-curated` entry with the
Git override — `location` must match the canonical URL character for character:

```json
"creatio-curated": {
  "library-id": "com.creatio.clio",
  "type": "git",
  "location": "https://github.com/Advance-Technologies-Foundation/clio-knowledge.git",
  "commit": "<40-hex, a PUSHED commit of the guidance checkout>",
  "enabled": true,
  "priority": 100,
  "participation": "authoritative"
}
```

Drop `repository-owner`, `repository-name` and `asset-name` — they belong to the release transport.

```
$CLIO delete-knowledge --source creatio-curated --force
```

**Not optional and not tidiness.** A derived sequence (`1.13.54` → `1013054`) is orders of magnitude
below a release-installed one (`1.13.25` → `1013025000`), so activation is refused while every status
command still reports success. Deleting the cache removes the generation being compared against.

```
$CLIO install-knowledge --source creatio-curated --json
```

### 3. The two gates

```
$CLIO info-knowledge | grep -i -E 'Installed:|Valid:|Library version|Revision'
git -C $KB rev-parse HEAD
```

Gate 1: `Revision` must equal that HEAD.

Gate 2: call `get-guidance name=routing` and one article the feature needs, and confirm **real content
comes back**. `info-knowledge` reporting `Valid: yes` does not mean the library serves — that exact
combination (valid, installed, serving nothing) is what step 2's cache deletion prevents. A large
response is the pass; `guidance-unavailable` with an empty `availableGuides` is the failure.

### 4. Prompt and scratch

No prompt file yet → run `/bp-test-cases <ISSUE>` first. Then:

```
mkdir -p "<scratch>/bp-test-$ISSUE-$RUN" && cp "spec/$FEATURE/$FEATURE-manual-test-prompt.md" "<scratch>/bp-test-$ISSUE-$RUN/prompt.md"
```

`mcp-clio-only.json` in that directory — **forward slashes**; a Windows path with single backslashes is
invalid JSON and fails silently at launch:

```json
{ "mcpServers": { "clio": { "command": "dotnet",
  "args": ["C:/Projects/clio-eng95891/clio/bin/Release/net8.0/clio.dll", "mcp-server"] } } }
```

Confirm the process tools are reachable: `features` in `appsettings.json` must carry
`"process-designer": true`.

### 5. Launch

```
cd "<scratch>/bp-test-$ISSUE-$RUN" && claude -p "$(cat prompt.md)" --mcp-config ./mcp-clio-only.json --strict-mcp-config --session-id $RUN > result.txt 2>&1
```

Add `--bare` before `-p` when `ANTHROPIC_API_KEY` or `apiKeyHelper` is available. Transcript lands at
`~/.claude/projects/<scratch-slug>/$RUN.jsonl`.

### 6. Teardown — three steps, all required

```
$CLIO experimental --name knowledge-allow-unsequenced --disable
```

```
cp "$LOCALAPPDATA/creatio/clio/appsettings.json.bpskills-backup" "$LOCALAPPDATA/creatio/clio/appsettings.json"
```

```
$CLIO install-knowledge --source creatio-curated
```

The flag is persistent and global: while it is on, the content-integrity check is weakened for every
later clio run on this machine. The third step matters because step 2 of the setup deleted the release
generation — restoring the configuration alone leaves the machine with no installed library.

**Do not delete the processes the run created on the stand.** They are the input to mode `browser`.

## Mode `browser`

```
/bp-test-run <ISSUE> --mode browser --env <alias>
```

Needs no knowledge wiring, no build, no flag, and restores nothing — it only reads the manifest, opens
the designer, runs the processes and looks. Confirm the manifest's `stand.alias` matches the
environment before anything else.
