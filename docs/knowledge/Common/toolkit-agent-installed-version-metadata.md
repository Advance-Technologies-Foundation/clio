---
description: toolkit agents record installed versions differently; Codex selects local first and otherwise the greatest cache version, while Claude global installs are user-scope registry entries
applies-to:
  - clio/Common/Skills/InstalledToolkitVersions.cs
ticket: "#1500"
date: 2026-09-12
---

**What is true** — Claude stores global toolkit entries in
`plugins/installed_plugins.json` under the plugin source key with `scope: user`.
Codex selects `local` first, otherwise the greatest cache version using semantic
version comparison and ordinal fallback, then reads the selected plugin manifest.
Copilot marketplace installations live under
`installed-plugins/<marketplace>/<plugin>/`. Cursor's clio installer copies its
runtime into `plugins/local/<plugin>/`.
Codex and Claude CLI subprocesses inherit `CODEX_HOME` and `CLAUDE_CONFIG_DIR`;
version inspection must honor these too or report a stale default installation.

Sources: local Claude installed registry; [Codex store implementation](https://github.com/openai/codex/blob/main/codex-rs/core-plugins/src/store.rs);
[Copilot configuration directory reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference).

**Why it is this way** — each agent owns its installation format and release
selection. The toolkit's product version belongs to installed metadata, not clio
or a remote marketplace listing.

**What breaks if you ignore it** — reporting the first cache directory can report
an obsolete version after an update, and reporting a marketplace version can claim
an update the user has not installed. Cache directory names may also be hashes
rather than product versions; read the selected payload's manifest.
