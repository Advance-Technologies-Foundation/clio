---
name: clio-release
description: Publish a new Advance-Technologies-Foundation/clio CLI release from tested master, write release notes, and verify NuGet delivery. Use when asked to release clio; excludes ClioRing preview releases and release planning alone.
---

# Clio release

This is the shared release procedure for Codex, Claude, and the Copilot release
prompt. Agent discovery wrappers point here; maintain only this canonical procedure.

Read `AGENTS.md`, `project-context.md`, `RELEASE.md`, the current
`.github/workflows/reliase-to-nuget.yml` (the spelling is intentional), and
`docs/knowledge/process/release-published-workflow-runs-from-the-tag-commit.md`.
Resolve these paths from the clio repository root, not the skill directory.

## Select the release

- A request to make a release authorizes its tag, GitHub release, and automatic NuGet
  publication. Continue through verification without asking again. Planning alone
  does not authorize publication.
- Check `gh` authentication, repository identity, and worktree state. Fetch
  `origin master --tags`. Release a full, recorded 40-character `origin/master` SHA;
  never tag an arbitrary working branch, detached HEAD, or uncommitted changes.
- Inspect remote releases and tags. Choose the highest stable clio `X.Y.Z.W`
  version (an optional `v` prefix is accepted), excluding Ring and package tags,
  then increment **W**, unless the user specified a version. Check both remote tags
  and releases for a collision immediately before publication. Never overwrite one.
- Inspect the commits and merged PRs since the previous release. Check applicable
  test plans, story status, command documentation, MCP evidence, and quality gates.
  Distinguish recorded pre-merge checks from checks run in this session; do not
  present missing live-environment coverage as verified.
- If there are no new commits since the previous release, report that there is
  nothing to release and stop rather than publishing an identical package.
- Require a successful `Build` (`build.yml`) run for the exact selected SHA with
  `event=push` and `headBranch=master`. A green PR run is insufficient. Monitor an
  in-flight run; stop publication on failure and report the failing job. The release
  workflow checks this gate again. Integration evidence comes from pre-merge PR
  checks as described in `RELEASE.md`.

## Write and publish

Write English release notes to a temporary Markdown file. Explain user-visible
features and fixes, call out behavior changes and required package upgrades, link
the supporting PRs, include `clio update`, and link the full comparison. Avoid
generic "Automated release" text. Do not claim unreleased guidance is available.

Preserve the established developer-facing release-note style:

- Start with `## <emoji> Clio X.Y.Z.W — <hook>` and a warm one-line opener
  explaining the problem the release solves.
- Use applicable sections such as `### ✨ What's New`, `### 🐛 Fixes`, and
  `### 📚 Docs`. Start each bullet with a bold lead-in, explain what changed and
  why it helps, and link the supporting PR.
- Close with `### 💡 Why upgrade?` and `*Happy coding! 🎉*`.
- Base every claim on the actual changes; a friendly hook must not overstate
  unconfirmed behavior. Do not infer a different tone from a recent release.

The workflow injects the tag version into the package and assembly. Do not edit
`clio.csproj`, `Directory.Packages.props`, or bundled package versions just to
release clio. The legacy `create-release.*` scripts choose a tag from local HEAD
and use placeholder notes; do not run them blindly in an agent worktree.

Create the tag at the verified SHA, push only that tag, and publish with the notes:

```sh
git tag <version> <verified-full-master-sha>
git push origin refs/tags/<version>
gh release create <version> --verify-tag --target <verified-full-master-sha> \
  --title "Release <version>" --notes-file <notes-file>
```

If an operation returns an ambiguous result, inspect remote state before retrying.
Never delete or move a published tag, delete a release, or retry an accepted NuGet
version as an automatic recovery. A source/workflow fix needs its own verified
commit and normally a new version; explain the concrete recovery if user action is
needed.

## Verify delivery

- Verify the remote tag resolves to the selected SHA and the release is published.
- Find the `release-to-nuget` run for this tag/SHA and monitor it to completion.
  Use compact status and job summaries, inspecting logs only for a failed step.
  Keep waits bounded so progress can be communicated.
- Require the pack, isolated tool installation/version smoke check, and NuGet
  publish steps to succeed. Inspect publish logs without printing credentials.
- Check `https://api.nuget.org/v3-flatcontainer/clio/index.json` for the version.
  Allow for indexing delay; if it persists, distinguish NuGet acceptance from
  public availability. Where practical, install that exact public version into a
  temporary `--tool-path` with an isolated NuGet configuration and verify
  `info --clio`. Do not replace the user's global tool merely to smoke-test it.
- Report the version, release link, workflow result, and NuGet visibility or any
  remaining indexing delay. A GitHub release alone does not prove delivery.
