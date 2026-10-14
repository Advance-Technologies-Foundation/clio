---
description: Chrome for Testing's JSON index lists only download URLs, no checksum, so page-smoke's headless-shell download is verified against a size and SHA-256 measured by hand when the version was pinned
applies-to:
  - clio/Common/BrowserSession/HeadlessShellRelease.cs
  - clio/Common/BrowserSession/HeadlessShellInstaller.cs
ticket: ENG-103287
date: 2026-10-14
---

**What is true** — `last-known-good-versions-with-downloads.json` (and every other Chrome for Testing endpoint)
gives `{platform, url}` per download and nothing else: no size, no hash, no signature. The archives on
`storage.googleapis.com/chrome-for-testing-public` carry only GCS's own `x-goog-hash` (CRC32C/MD5). So the size and
SHA-256 in `HeadlessShellRelease.Archives` were measured on 2026-10-14 by downloading each of the six
`chrome-headless-shell-<platform>.zip` archives of 155.0.8059.79 and hashing them.

**Why it is this way** — the download is started by an agent after the user said yes, possibly through a mirror
(`CLIO_CHROME_FOR_TESTING_MIRROR`); a pin in clio's own source is the only check a wrong mirror or a tampered
archive cannot also change.

**What breaks if you ignore it** — raising `HeadlessShellRelease.Version` without re-measuring all six archives
makes every download fail verification ("did not verify: its size is ..."), and page-smoke answers
browser-download-failed for every user who chose a download. Re-pin size AND hash for every platform together.
