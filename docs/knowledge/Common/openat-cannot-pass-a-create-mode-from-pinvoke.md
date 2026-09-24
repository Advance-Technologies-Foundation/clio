---
description: openat is variadic so a P/Invoke cannot pass O_CREAT's mode; the confined write stages inside a 0700 mkdirat directory instead, because a later fchmod cannot revoke a descriptor another account already opened
applies-to:
  - clio/Common/UnixConfinedFileAccess.cs
  - clio.tests/Common/ConfinedFileAccessTests.cs
ticket: "1221"
date: 2026-09-21
---

**What is true** — `UnixConfinedFileAccess` creates its staged payload with a THREE-argument
`openat` and no mode, then narrows the open descriptor with `fchmod(0600)`. The file's initial
permission bits are therefore whatever libc reads off the stack or a register: a Linux x64 probe of
this exact P/Invoke observed `0040`, and the value is ABI- and register-state dependent, so it is not
a constant you can rely on either. The confidentiality boundary is NOT that mode — it is the
`<target>.<guid>.tmp` **directory**, created `0700` by `mkdirat` in one call, that the payload is
staged inside. `mkdirat` is `int mkdirat(int, const char *, mode_t)`, not variadic, so its mode really
is applied at creation.

**Why it is this way** — `openat` is declared `int openat(int, const char *, int, ...)` and the mode
is the variadic part. On Apple silicon a variadic argument is passed on the stack while a P/Invoke
that declares it as an ordinary fourth parameter puts it in a register, so the four-parameter
declaration hands libc garbage. That is a SILENT wrong answer on macOS and a correct one on Linux,
which is the worst shape a platform difference can take. Passing the mode is therefore not available
at all, on any platform, from a single declaration.

**What breaks if you ignore it** — narrowing the file after the fact does not close the window.
`fchmod` changes the inode's mode; it does not revoke a descriptor another local account already
opened while the bits were wide. Under the shared OS temp root — a legitimate output location for a
raw OData response — anyone allowed by those initial bits can open the empty staged file, hold the
descriptor, and read every business byte written afterwards. Reasoning that "the file is empty until
`fchmod` returns" answers the bytes and not the handle. Removing the staging directory, or creating
it with anything wider than `0700`, reopens that. It fails silently: every assertion made on the
FINISHED file still passes, because the published inode is owner-only either way. Only
`WriteNew_ShouldStageInsideAnOwnerOnlyDirectory_BeforeWritingAnyByte` catches it, by reading the
directory's mode from inside the write, while the payload stream is still open.

**A known limit, deliberately not closed in code.** The reopen of the staging directory is
`O_NOFOLLOW | O_DIRECTORY`, so a symlink substituted for the name is refused — but it does not prove
the directory it got back is the same INODE `mkdirat` just created. An account with write access to
the parent could remove the empty directory and put its own there in between. Traced to the end, that
buys nothing: the staged file is still created `O_EXCL`, owned by clio and narrowed to `0600` before
the first byte, and `linkat`'s destination is the already-fixed parent descriptor, so neither the
payload nor the publish is reachable — the worst outcome is a failed call. It also needs a parent
directory writable by another account, which the OS temp root is not (sticky bit). Closing it would
mean an `fstat` P/Invoke whose `struct stat` layout differs per platform and per architecture — the
same class of ABI hazard this record is about. If you add one, prove it on Linux x64, Linux arm64 and
Apple silicon, or it will fail in the direction that reads as success.
