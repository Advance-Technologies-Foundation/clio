# Release Process

## Codex skill

Ask Codex to make a clio release, or invoke `$clio-release`. The repository skill
is in [`.agents/skills/clio-release/SKILL.md`](.agents/skills/clio-release/SKILL.md),
with a compatibility link under `.codex/skills/clio-release`.
Codex discovers repository skills in `.agents/skills`; if the new skill does not
appear after the file changes, restart Codex
([official skill documentation](https://learn.chatgpt.com/docs/build-skills)).

The skill selects a verified full `origin/master` commit, waits for its master-push
`Build` run, increments the last version component, writes English release notes,
and follows the release workflow through NuGet publication and version verification.
It does not need a project-version commit. A request to make a release authorizes
publication; no additional confirmation is needed for that same release.

The scripts below are legacy interactive helpers: they tag the current checkout
and create placeholder notes. In an agent worktree, use the skill's explicit
commit selection and reviewed notes instead.

## GitHub Copilot command

Run `/release` in GitHub Copilot Chat. The prompt follows the same shared
`clio-release` procedure: increment the last version component (for example,
`8.0.1.42` → `8.0.1.43`), publish from tested master with meaningful notes, and
verify NuGet delivery.

## Alternative methods

### Automated scripts

The scripts create a tag and a GitHub release:

#### PowerShell (Windows/macOS/Linux):
```powershell
# Interactive mode (creates a tag and a GitHub release)
.\create-release.ps1

# Automatic mode (skips confirmation)
.\create-release.ps1 -Force
```

#### Bash (macOS/Linux):
```bash
# Interactive mode (creates a tag and a GitHub release)
./create-release.sh

# Automatic mode (skips confirmation)
./create-release.sh --force
```

**What the scripts do:**
- Find the latest tag
- Increment the last version component (`X.Y.Z.W` → `X.Y.Z.(W+1)`)
- Create and push the new tag
- Create a GitHub release if GitHub CLI is available and authenticated
- Trigger NuGet publication when the GitHub release is published

## Manual release process

### Pre-release checklist

Before creating a release tag, verify the following:

**BMAD artifacts**
- [ ] All features in this release have an approved test plan in `spec/test-plans/`
- [ ] All stories for this release are `done` in `spec/sprint-status.yaml`
- [ ] Run `/bmad-status` to confirm no feature is stuck in `in-progress` or `review`

**Test gates**
- [ ] `make test-unit` passes locally
- [ ] CI is green on master (`make check-pr`)
- [ ] Review relevant MCP E2E results when MCP tools changed; check per-fixture CI coverage and run missing live-environment cases manually
- [ ] SonarCloud has no new unresolved issues

**Documentation**
- [ ] Command docs updated (`clio/docs/commands/`, `clio/help/en/`, `clio/Commands.md`)
- [ ] AGENTS.md updated if any new policies were introduced
- [ ] What's new draft ready (see [Release Notes](#6-required-release-notes-whats-new) below)

### 1. Preparation

Ensure all changes are ready and tested on `master`. Record the verified full commit SHA for the release.

### 2. Create the release

1. **Create a version tag** in `X.Y.Z.W` format at the verified full master SHA:
   ```bash
   git tag 8.0.1.43 <verified-full-master-sha>
   git push origin refs/tags/8.0.1.43
   ```

2. **Create a GitHub release** with the prepared release notes:
   
   **Option A: GitHub CLI (recommended)**
   ```bash
   gh release create 8.0.1.43 --verify-tag --target <verified-full-master-sha> \
     --title "Release 8.0.1.43" --notes-file <release-notes-file>
   ```
   
   **Option B: Web interface**
   - Open the [Releases page](https://github.com/Advance-Technologies-Foundation/clio/releases)
   - Click "Create a new release"
   - Select the tag you created
   - Enter the release notes
   - Click "Publish release"

### 3. Automatic publishing

Creating the release automatically starts the `release-to-nuget` workflow, which:

1. ✅ **Extracts the version** from the tag (accepts both `8.0.1.43` and `v8.0.1.43`)
2. ✅ **Validates the version format** (must be `X.Y.Z.W`)
3. ✅ **Checks that the `Build` workflow is green** for the tagged commit (it does not re-run the tests itself — see [Release test gate](#release-test-gate-and-integration-coverage))
4. ✅ **Packs** clio with the version from the tag
5. ✅ **Publishes to NuGet** automatically

#### Release test gate and integration coverage

The release workflow (`.github/workflows/reliase-to-nuget.yml`) does not run `dotnet test`. It waits for the
`Build` run (`build.yml`) of the tagged commit and publishes only when that run is green. Only a run started by a
`push` to `master` counts: such a run always executes every unit test shard, while a `pull_request` run of the same
SHA runs only the jobs for the areas the PR changed (a docs-only PR runs no test job at all), so it can be green
without having tested most of the code. The selection lives in `.github/scripts/Select-ReleaseBuildRun.ps1` and is covered
by `clio.tests/ReleaseWorkflow/ReleaseBuildRunSelectorTests.cs`, which fails if the selector accepts a non-master
or non-push run.

**Decision (issue #1573): integration coverage relies on pre-merge evidence (option A).** `build.yml` runs the
`Integration` category only on `pull_request` (its unit shards exclude it), so a green master `Build` does not
re-prove integration tests on the tagged commit. The evidence is the pull-request run before merge: the
`Integration Tests` check is required by the master ruleset, and that run tests the PR merged into master, which
is normally the tree that lands. Re-running it at release time would test an identical tree again and add
minutes to every release (each of the three hosted integration shards takes about 3.5 minutes including its
build; one self-hosted runner would run them in sequence).

**Option B (not taken):** add a step before "Pack clio with release version" that runs

```powershell
dotnet test .\clio.tests\clio.tests.csproj -c Release --filter "TestCategory=Integration"
```

and fails on a non-zero exit code. Spell the filter `TestCategory`, never the `Category` alias
([why](docs/knowledge/Tests/nunit-adapter-drops-large-non-category-shard-filters.md)). If option B is adopted,
update this section and the comment above the gate step in the workflow.

### 4. Version requirements

- **Valid formats**: `8.0.1.43`, `v8.0.1.43`
- **Invalid formats**: `8.0.1`, `v8.0`, `release-8.0.1.43`

### 5. Build locally with a specific version

To build a local package with a specific version:

```powershell
dotnet pack .\clio\clio.csproj -c Release --output ./output /p:AssemblyVersion=8.0.1.43 /p:FileVersion=8.0.1.43 /p:Version=8.0.1.43
```

### 6. Required release notes (What's new)

> Every GitHub release **must** include release notes (What's new).
> Users see these notes when they run `clio update`.

When coding agents such as Codex, GitHub Copilot or Claude Code prepare a release, they **must**:

1. **Track changes** during development: each meaningful commit should have a clear description.
2. **Prepare release notes** when creating the release: a concise, clear list of changes in English.
3. **Include the notes in the GitHub release** using `gh release create --notes-file`.

**Release note format:**
```markdown
- Add feature X for better Y
- Fix issue with Z when doing W
- Improve performance of operation Q
```

**Example of a release with notes:**
```bash
cat > release-notes.md <<'EOF'
- Fix player name display in the environment list
- Add an interactive quiz easter egg
- Improve the update command with a spinner and release notes
EOF
gh release create 8.0.2.65 --verify-tag --target <verified-full-master-sha> \
  --title "Release 8.0.2.65" --notes-file release-notes.md
```

Without release notes, users running `clio update` cannot see what changed in the update.

### 7. Notes

- The **cliogate project** keeps its own version.
- Only the **clio package** receives the version from the release tag.
- Local development uses the default version declared in `clio/clio.csproj`.
