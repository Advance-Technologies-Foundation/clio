---
description: Publish the next clio release from tested master and verify NuGet delivery.
---

Read and follow the shared release procedure in
[SKILL.md](../../.agents/skills/clio-release/SKILL.md) and
[RELEASE.md](../../RELEASE.md).

A request to make a release authorizes tag creation, GitHub publication and the
automatic NuGet publish. Select the exact verified `origin/master` SHA rather
than the current checkout, increment the last component of the highest stable
clio release version, and write meaningful English release notes with PR links.
The release workflow sets the assembly/package version from the tag; do not edit
the project version. Wait for the exact commit's master-push Build to pass before
publication, then monitor release-to-nuget and verify delivery.
