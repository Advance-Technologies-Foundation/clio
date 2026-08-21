---
description: a release-triggered workflow (.github/workflows/reliase-to-nuget.yml, note the misspelling) checks out the tag's commit, so a fix pushed to master after tagging is not picked up by re-running it - delete and recreate the release at the fixed full SHA
applies-to:
  - .github/workflows/reliase-to-nuget.yml
date: 2026-08-19
---

**What is true** — clio's NuGet publish runs on `release: [published]`, and such a workflow reads
its own definition and sources from the commit the tag points at. Re-running a failed release
publish therefore replays the OLD workflow file, even when master already contains the fix. The
recovery is to commit the fix to master, delete the tag and the release, then recreate the release
targeting the fixed commit:
`gh release create <tag> --target <full-40-char-sha>`. A short SHA or a stale tag fails with
`Release.target_commitish is invalid`. Publishing the recreated release re-fires
`release: published` and the run picks up the fixed file.

**Why it is this way** — GitHub resolves `release: published` against the release's
`target_commitish`, not against the default branch. This is deliberate: a release must build from
the code it names.

**What breaks if you ignore it** — you re-run the failed workflow, watch it fail identically, and
conclude the fix did not work. Meanwhile the version stays absent from nuget.org although the
GitHub tag and release exist, so `dotnet tool install clio` keeps resolving the previous version.
Second trap in the same area: the workflow file name is misspelled `reliase-to-nuget.yml` — grep
the workflow directory by content, not by the name you expect.
