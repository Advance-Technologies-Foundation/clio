---
description: On Windows FileInfo.LinkTarget and DirectoryInfo.LinkTarget return null for a symbolic link whose target is missing (dangling) or unresolvable (a cycle), while File.GetAttributes still reports ReparsePoint; only the reparse tag tells a followable link from a placeholder
applies-to:
  - clio/Command/OutputPathConfinement.cs
  - clio/Common/WindowsConfinedFileAccess.cs
ticket: "1221"
date: 2026-09-04
---

**What is true** — on Windows, `FileSystemInfo.LinkTarget` (both the `FileInfo` and the `DirectoryInfo`
form) answers `null` for a symbolic link whose target does not exist and for every node of a link
cycle, exactly as it does for an ordinary file. The entry still carries `FileAttributes.ReparsePoint`,
and `Directory.Exists` is `true` for a dangling *directory* link. For a link whose target exists,
`LinkTarget` works. Measured on Windows 11 with .NET 10; on Linux and macOS `LinkTarget` reads the
link text regardless of the target.

**Why it is this way** — the runtime resolves the target as part of reading it and gives up silently
when that resolution fails. The reparse point itself is still readable: `GetFileInformationByHandleEx`
with `FileAttributeTagInfo` on a handle opened with `FILE_FLAG_OPEN_REPARSE_POINT` returns the tag,
and only `IO_REPARSE_TAG_SYMLINK` and `IO_REPARSE_TAG_MOUNT_POINT` are followed by a Windows pathname.
Other tags (cloud-files placeholders under a OneDrive-redirected folder, app-execution aliases, WSL
links) are ordinary entries at their own location.

**What breaks if you ignore it** — a confinement check that decides "is this a link" from `LinkTarget`
alone treats a dangling or cyclic link as a plain entry: the dangling link is appended as a lexical
tail segment and the cycle degrades to its lexical path, so both defenses are inert and the later
write follows the link at the OS level. `Resolve_ShouldReject_DanglingIntermediateSymlinkEscape` and
`Resolve_ShouldFailClosed_OnSymlinkCycle` fail on a Windows box that can create symlinks; hosted
GitHub Windows runners cannot, so the failure is invisible in CI. The opposite shortcut — refusing
every `ReparsePoint` whose `LinkTarget` is `null` — refuses every OneDrive placeholder instead.
