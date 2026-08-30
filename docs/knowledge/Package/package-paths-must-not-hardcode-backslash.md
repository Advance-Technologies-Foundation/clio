---
description: a hard-coded backslash in a package path silently creates a directory literally named "Files\cs" on macOS and Linux instead of failing
applies-to:
  - clio/Package/CreatioPackage.cs
  - clio/Project/VSProject.cs
ticket: GH-1279
date: 2026-08-30
---

**What is true** — `\` is a legal file-name character on Unix. A path built as `"Files\\cs"` therefore
does not fail on macOS or Linux: `Directory.CreateDirectory` creates one directory whose *name* is
the six-character string `Files\cs`, next to the real `Files` directory. Every package path must be
built with `Path.Combine`, never with a literal separator.

**Why it is this way** — the package layout was written on Windows, where `Files\cs` and
`Files/cs` denote the same nested path, so the defect is invisible there and no exception is raised
on any platform.

**What breaks if you ignore it** — `new-pkg` produces a package with both `Files/cs` (holding
`EmptyClass.cs`, created through `Path.Combine`) and a junk `Files\cs` directory holding the
placeholder. The package still installs, so the damage surfaces later, in the repository and in the
package archive.

A separate trap in the same command: `ReferenceOptions.Path` is loaded with `XElement.Load`, so it
must be the package **project file**, not the package directory. Passing the directory raises
`UnauthorizedAccessException` — reported as `Access to the path ... is denied`, which reads like a
permission problem and is not one.
