---
description: ReferenceOptions.Path must be a .csproj file; a directory surfaces as "Access to the path is denied", which is not a permission problem
applies-to:
  - clio/Command/NewPkgCommand.cs
  - clio/Command/ReferenceCommand.cs
  - clio/Project/CreatioPkgProject.cs
ticket: GH-1279
date: 2026-08-30
---

**What is true** — `ReferenceOptions.Path` is loaded with `XElement.Load` (`CreatioPkgProject`'s
constructor), so it must be the package **project file**. Passing the package directory raises
`UnauthorizedAccessException`, and clio prints its message verbatim:

```
[ERR] - Access to the path '.../MyPkg' is denied.
```

**Why it is this way** — opening a directory as a file is a permission error at the OS level on every
platform, so the exception type says nothing about the real mistake.

**What breaks if you ignore it** — the message sends whoever reads it to file permissions, `sudo`, or
antivirus, none of which are involved. `new-pkg <name> -r bin` failed this way for every user on
every platform. `ReferenceCommand` now rejects a directory with an explicit message instead.
