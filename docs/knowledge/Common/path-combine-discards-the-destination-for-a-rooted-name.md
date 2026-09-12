---
description: Path.Combine returns only its last rooted argument, so an absolute item name wrote outside the destination directory instead of failing
applies-to:
  - clio/Project/VSProject.cs
  - clio/Command/AddItemCommand.cs
ticket: GH-1279
date: 2026-08-31
---

**What is true** — `Path.Combine(destination, name)` DISCARDS `destination` when `name` is rooted; it
returns `name`. Composing a write target from a name that clio did not itself construct therefore
needs the name validated as one plain file name first, and the composed absolute path re-checked
against the destination root afterwards. `VSProject.AddFile` does both.

**Why it is this way** — `add-item` takes the item name from the command line, and its model path
takes the names from the keys of a `GetEntitySchemaModels` response, which is data returned by the
target Creatio instance rather than clio's own. Neither is a trusted file name. One check is not
enough on its own: a name check alone still misses a destination that itself resolves elsewhere, and
a containment check alone accepts a backslash-bearing name on Unix, where `\` is an ordinary
character that Windows later reads as a separator.

**What breaks if you ignore it** — `add-item` given an absolute item name exited 0, created nothing
in the requested destination, and wrote the generated `.cs` file at that absolute path instead. On
the model path the same shape lets a compromised or hostile Creatio response overwrite any writable
`.cs` file the clio process can reach.
