---
description: CommandLineSDK writes its full auto-built help after EVERY parse error and Parser.Default freezes HelpWriter, so Program.ExecuteCommands parses with a fresh Parser that buffers library output and renders verb option errors itself
applies-to:
  - clio/Program.cs
ticket: ENG-101526
date: 2026-09-29
---

**What is true** — `Program.ExecuteCommands` does not parse with `Parser.Default`. It builds a new
`Parser` per call whose `HelpWriter` is a `StringWriter`, copying `ShowHeader`, `HelpDirectory` and
`CustomHelpViewer` from `Parser.Default.Settings` (which is still configured, because
`CommandHelpRenderer` reads `HelpDirectory` from it). After the parse, a mistake in the options of a known
verb (unknown option, missing value, missing required option, bad format...) is rendered by clio in a short
form (`ERROR(S)`, `Did you mean --x?`, `See command help`) and the buffer is dropped. Anything else — help,
version, an unknown or missing verb, or any argv containing a help alias — gets the buffer flushed to
`Console.Error` unchanged.

**Why it is this way** — the library's `DisplayHelp` writes `HelpText.AutoBuild(...)` (errors plus the
entire option list) to `HelpWriter` for every not-parsed result; there is no setting that prints errors
without the option list. `HelpWriter` is a "popsicle" property: `Parser.Default` is constructed with
`Consumed = true`, so setting `Parser.Default.Settings.HelpWriter` throws `InvalidOperationException`. The
library also treats `--help`/`-help`/`-h`/`--h` anywhere in argv as a help request and renders help
instead of errors, which is why those argvs keep the library output.

**What breaks if you ignore it** — switching back to `Parser.Default.ParseArguments` silently restores
the full help dump after every typo (clio#1630: users bisected commands because the real error was buried
at the top of a screen of options). The `CommonProgramTest` "no option list" assertions cannot catch that
regression on their own: `Parser.Default` captured `Console.Error` when it was first built, so its dump
never reaches the writer a test installs — only the missing short-form lines fail. Trying to fix it by assigning `Parser.Default.Settings.HelpWriter`
crashes every invocation.
