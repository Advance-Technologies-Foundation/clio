---
description: the ?hash=<md5> query on a theme's cssFilePath is a browser cache-buster, not a version selector - the server serves the theme's current CSS for any hash value, so a read after update-theme needs no cache-flush step
applies-to:
  - clio/Command/Theming/GetThemeCommand.cs
ticket: ENG-93991
date: 2026-08-25
---

**What is true** — the catalog publishes a theme's CSS path as
`Terrasoft.Configuration/Pkg/<Package>/Files/themes/<themeId>/theme.css?hash=<md5>`. The server
serves the theme's **current** content for that path regardless of the hash value; the hash exists
to bust the browser cache. After `update-theme`, a fresh `list-themes` returns a new hash for the
same theme. `get-theme` therefore re-resolves the catalog on every call, and read-after-update is
correct with no cache-flush and no session refresh in between.

**Why it is this way** — a platform detail of how the Shell loads themes, verified live during the
ENG-93991 spike (`spec/adr/adr-theming.md` E-D1).

**What breaks if you ignore it** — reading the hash as a version leads two ways, both wrong. Caching
the resolved `cssFilePath` across calls looks safe (the hash "identifies" the content) and is
harmless only by accident; inserting a cache-clear step before the read to "get the new version"
adds a privileged call the read does not need and does not have. The visible symptom of treating the
hash as meaningful is a comparison of two hashes concluding a theme changed when only the catalog
was re-read.
