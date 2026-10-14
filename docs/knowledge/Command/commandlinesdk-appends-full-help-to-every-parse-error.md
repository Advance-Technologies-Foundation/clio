---
description: CommandLineSDK writes its full auto-built help after EVERY parse error and Parser.Default freezes HelpWriter, so Program.ExecuteCommands parses with a fresh Parser that buffers library output and renders verb option errors itself
applies-to:
  - clio/Program.cs
ticket: ENG-101526
date: 2026-09-29
---

**What is true** — `Program.ExecuteCommands` does not parse with `Parser.Default`. It builds a new
`Parser` per call that copies `ShowHeader` and `HelpDirectory` from `Parser.Default.Settings` (which is
still configured, because `CommandHelpRenderer` reads `HelpDirectory` from it) and replaces the other two:
`HelpWriter` becomes a `StringWriter`, and `CustomHelpViewer` becomes a `DeferredHelpViewer` around
`Parser.Default`'s viewer that records help requests instead of printing them. After the parse, a mistake in the options of a known
verb (unknown option, missing value, missing required option, bad format...) is rendered by clio in a short
form (`ERROR(S)`, `Did you mean --x?`, `See command help`) and both the buffer and the recorded viewer
help are dropped. Anything else — help, version, an unknown or missing verb, or an argv that really asks
for help — gets the viewer help replayed and the buffer flushed to `Console.Error` unchanged. "Really asks
for help" is decided by `ArgvRequestsUnclaimedHelp(..., includeLibraryOnlyAliases: true)`: a `-h`/`--help`
counts only when it is not the value of the preceding option and the verb does not claim that name
(`healthcheck`/`publish-app` bind `-h` to their own option); `-help`/`--h` always count. The claim is
matched the way the parser binds names, case-sensitively (`matchParserCase: true`): those verbs claim `-h`,
not `-H`, so `healthcheck -H` / `publish-app -H` count as help requests and keep showing the command help
(exit 1), as the released clio does. The pre-parse `TryHandleBuiltInHelp` keeps its case-insensitive claim
on purpose — it treats `-H` as claimed there and leaves it to the parser, which is the released behaviour.

**Why it is this way** — the library's `DisplayHelp` writes `HelpText.AutoBuild(...)` (errors plus the
entire option list) to `HelpWriter` for every not-parsed result; there is no setting that prints errors
without the option list. `HelpWriter` is a "popsicle" property: `Parser.Default` is constructed with
`Consumed = true`, so setting `Parser.Default.Settings.HelpWriter` throws `InvalidOperationException`. The
library also treats `-help`/`-h`/`--h` anywhere in argv as a user help request: it then shows help through
`CustomHelpViewer`, which writes straight to `Console.Out` and bypasses `HelpWriter`, and it does so even
when the verb claims `-h` for its own option and the parse error is real (no `HelpRequestedError` is
added, so the exit code stays 1). That viewer output is why the viewer is deferred, not just the writer.

**What breaks if you ignore it** — switching back to `Parser.Default.ParseArguments` silently restores
the full help dump after every typo (clio#1630: users bisected commands because the real error was buried
at the top of a screen of options). The `CommonProgramTest` "no option list" assertions cannot catch that
regression on their own: `Parser.Default` captured `Console.Error` when it was first built, so its dump
never reaches the writer a test installs — only the missing short-form lines fail. Trying to fix it by assigning `Parser.Default.Settings.HelpWriter`
crashes every invocation. Passing the real viewer instead of the deferred one brings the full help screen
back in front of the short error for `healthcheck -h <host> --typo`. Matching the claim case-insensitively
after the parse (as `TryHandleBuiltInHelp` does) silently turns `healthcheck -H` / `publish-app -H` from the
help screen into `Option 'H' is unknown.`, and only a run of the built binary shows it.
