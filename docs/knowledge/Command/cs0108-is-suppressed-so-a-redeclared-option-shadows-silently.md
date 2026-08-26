---
description: CS0108 is in clio.csproj NoWarn, so an options class redeclaring an [Option] already inherited from EnvironmentOptions shadows it with no compiler warning - and a shadow with a different type makes the flag appear twice in help
applies-to:
  - clio/clio.csproj
  - clio/Command/InstallProcessBuilderCommand.cs
ticket: ENG-94385
date: 2026-08-19
---

**What is true** — `clio/clio.csproj` lists `CS0108` in `<NoWarn>` alongside CS0659/CS0661/CS8632.
So declaring a property on an options class that an inherited options base already declares - the
common case being `--force`, which `EnvironmentOptions` declares as "Force restore" - compiles
completely silently, with or without the `new` keyword. The only in-tree example that spells this out
is `InstallProcessBuilderOptions.Force`.

**Why it is this way** — the suppression is long-standing and repo-wide, kept because the options
hierarchy has many benign shadows. Redeclaring an inherited `[Option]` is legitimate when the verb
needs its own help text for a flag whose inherited meaning does not apply to it.

**What breaks if you ignore it** — two distinct failures, neither of which the compiler mentions.
Redeclare a flag unintentionally and you have silently changed which property the parser binds.
Redeclare it with a **different type**
and the flag appears TWICE in rendered help, once per description: `CommandHelpRenderer.BuildOptions`
enumerates `Type.GetProperties()` with no de-duplication, and reflection hides a shadowed base
property only when the name *and* signature match. Whenever you add an `[Option]`, grep the inherited
options bases for the same long name first, and mark a deliberate shadow `new` with the reason.
