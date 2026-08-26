---
description: <verb> --help printing 0 bytes with exit 0 is a CommandLineSDK dispatch defect, not Default=0 or a missing [Verb] Aliases - Program.TryHandleBuiltInHelp intercepts it, and both narrower fixes were tested and disconfirmed
applies-to:
  - clio/Program.cs
  - clio/HelpSystem/CommandHelpCatalog.cs
ticket: ENG-93886
date: 2026-08-19
---

**What is true** — `Program.TryHandleBuiltInHelp` short-circuits `<verb> --help` / `<verb> -h` to the
same `CommandHelpRenderer.TryRenderCommandHelp` the `help <verb>` branch uses, before CommandLineSDK's
parser runs. Without that intercept the parser writes nothing and exits 0 for a verb whose name is a
case-insensitive prefix of another registered verb's name, when the shorter verb has no `[Verb]`
Aliases and its options class inherits `EnvironmentOptions` (`create-data-binding` vs
`create-data-binding-db`, `create-app` vs `create-app-section`). Two narrower fixes were tried against
a built clio and **disconfirmed**: removing `Default = 0` from `CreateDataBindingOptions.InstallType`
(`create-app` has no int option at all and is broken identically), and adding an `Aliases` entry —
invoking through the new alias still produced 0 bytes.

**Why it is this way** — the trigger lives inside the closed-source CommandLineSDK dispatch, so it
cannot be fixed at the attribute level from this repository. The intercept is the workaround, and it is
deliberately narrow: it defers to the parser for `--WEB`/`-W`, for verbs the renderer does not
recognise (typo suggestions, feature-toggled-off commands), and for a `-h`/`--help` token the target
verb has claimed as its own option name (`healthcheck -h <host>`, `publish-app -h <url>`) or that sits
in the value position of a preceding value-taking option.

**What breaks if you ignore it** — the failure mode is silent success: no output, exit 0, nothing in
the logs. Removing the intercept as "a workaround for an attribute bug" re-breaks help for those verbs,
and widening it with a blind array-wide token scan replaces a real invocation with a help screen for
every verb that owns a `-h` option. Unrelated to
`docs/knowledge/Command/option-default-attribute-applies-only-in-the-parser.md`, which is about
`Default` on in-code-constructed options.
