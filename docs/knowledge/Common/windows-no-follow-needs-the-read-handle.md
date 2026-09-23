---
description: On Windows a reparse-point check on a pathname does not constrain a later open of that name; the check must be made on the handle actually used - for the leaf a read uses AND for every directory handle the descent pins
applies-to:
  - clio/Common/WindowsConfinedFileAccess.cs
  - clio/Common/UnixConfinedFileAccess.cs
  - clio/Common/ConfinedFileAccess.cs
ticket: "1221"
date: 2026-09-02
---

**What is true** — `WindowsConfinedFileAccess.OpenRead` opens the final path component with `CreateFileW`
and `FILE_FLAG_OPEN_REPARSE_POINT`, inspects THAT handle with `GetFileInformationByHandle`, and reads
through the same handle. The share mask is `FILE_SHARE_READ` only, so the entry cannot be renamed or
deleted while the read runs. `PinnedPath.Descend` pins the directory ancestors, and it applies the SAME
rule to each of them: `PinnedPath.OpenDirectoryHandle` inspects every handle it takes with
`GetFileInformationByHandle` and refuses a reparse point or a non-directory BEFORE the handle joins the
pinned list and before the descent goes deeper. The pathname check (`RejectReparsePoint`) stays, but it is
the cheap first filter, never the guarantee.

**Why it is this way** — `File.GetAttributes(path)` followed by `new FileStream(path, ...)` are two
operations against a NAME, not against an object. A writable parent directory can replace the final
component between them; the attribute check saw a regular file and the `FileStream` follows the symbolic
link that took its place. Pinning the ancestors does not help against a leaf swap, because the ancestors
never changed — only the leaf did. The converse is equally true and was the gap: pinning an ancestor by
NAME does not constrain what the pinned handle refers to, so a junction swapped in between
`RejectReparsePoint(component)` and `CreateFileW` is pinned as if it were the approved directory.

**What breaks if you ignore it** — two distinct escapes, one per direction.

Leaf: `rows-file` or `output-file` reads content from outside every allowed root and forwards it to
Creatio, with nothing in the pinned descent looking wrong.
`ConfinedFileAccessTests.OpenRead_ShouldNeverReturnSwappedContent_WhenTheFinalComponentIsReplacedDuringTheRead`
is the proof: it swaps the leaf between a real file and a link to other content while reads run, and the
name-check-then-reopen shape returns the other file's content within about 80 ms.

Ancestor: the descent pins a junction, then creates the missing inner segment and writes the payload
underneath it, OUTSIDE the allowed root. `Reverify` reports the swap afterwards, but the out-of-root
directory already exists and cannot be taken back — a failed call that still left a side effect where none
was permitted.
`ConfinedFileAccessTests.WriteNew_ShouldNeverCreateAnythingOutsideTheRoot_WhenAnIntermediateComponentIsSwappedDuringTheDescent`
is the proof for that direction.

In both directions the static "the component is already a link" case passes either way, which is why
neither can be the only test here: the pathname check catches the pre-planted link and nothing else.
