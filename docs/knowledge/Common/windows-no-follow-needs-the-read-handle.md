---
description: On Windows a reparse-point check on a pathname does not constrain a later reopen of that name; the check must be made on the handle the read uses
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
deleted while the read runs. Directory ancestors are pinned separately by `PinnedPath`.

**Why it is this way** — `File.GetAttributes(path)` followed by `new FileStream(path, ...)` are two
operations against a NAME, not against an object. A writable parent directory can replace the final
component between them; the attribute check saw a regular file and the `FileStream` follows the symbolic
link that took its place. Pinning the ancestors does not help, because the ancestors never changed — only
the leaf did.

**What breaks if you ignore it** — `rows-file` or `output-file` reads content from outside every allowed
root and forwards it to Creatio, with nothing in the pinned descent looking wrong.
`ConfinedFileAccessTests.OpenRead_ShouldNeverReturnSwappedContent_WhenTheFinalComponentIsReplacedDuringTheRead`
is the proof: it swaps the leaf between a real file and a link to other content while reads run, and the
name-check-then-reopen shape returns the other file's content within about 80 ms. The static
"final component is already a link" case passes either way, which is why it cannot be the only test here.
