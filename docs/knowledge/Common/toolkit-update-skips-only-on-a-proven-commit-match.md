---
description: update-toolkit compares agents against the toolkit's RELEASE branch, not the marketplace's main branch, and never trusts clio's own managed-skills.json as the installed commit - the old unconditional reinstall is what made the hourly auto-update cost ~15 s
applies-to:
  - clio/Common/Skills/SkillInstallService.cs
  - clio/Common/Skills/ToolkitReleaseProbe.cs
  - clio/Common/Skills/InstalledToolkitVersions.cs
ticket: "#1950"
date: 2026-11-15
---

**What is true** — the installed payload follows the toolkit repository's `release` branch, while the
marketplace checkout each agent keeps follows `main`. On 2026-11-15 the marketplace was at `main` =
`b9364c8` and both Claude and Codex had `release` = `6b296e3` installed. The up-to-date check must
compare with `refs/heads/release`, and with the commit the AGENT recorded, not with
`managed-skills.json`.

**Why it is this way** — each agent's Update is really a reinstall ported from the toolkit's
installer, where it migrates old file-copy installs: Codex runs legacy cleanup, `marketplace remove`,
`marketplace add` (a fresh clone) and `plugin add` on every call. Run hourly at startup it cost 11 s
for Codex and ~15 s overall on machines where nothing had changed. clio's manifest records what clio
last did, and goes stale as soon as the user or the agent's own marketplace auto-update moves the
payload.

**What breaks if you ignore it** — comparing with `main` reports every agent as outdated on every
run, which is the 15 s full reinstall again, now hidden in a background child. Trusting the manifest,
or reading an unknown commit as a match, skips updates silently: the update is unattended, so a user
would stay on an old toolkit with nothing to see.
