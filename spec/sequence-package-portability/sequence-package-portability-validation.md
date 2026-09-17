# Validation evidence

Validated against Creatio 10.1.752.0, .NET 8, PostgreSQL, with Sales Engagement installed. A separate fresh target received a native package containing one sequence, three ordered steps, one custom ruleset, one delivery schedule and seven schedule slots. The package contained no participants, activities or mailbox credentials.

- All five definition sections read back complete; the sequence arrived Draft.
- All selected step fields, including rich-text Description/Body and lookup values, matched the source after installation.
- Reinstall preserved row identities, counts and the five definition sections. This was an unchanged-package reinstall, not an upgrade-overwrite test.
- Reopening the standard designer showed Task, Call and Manual Email in order, their delays, descriptions and email subject/body.
- After explicit target-local owner configuration and UI activation, native enrollment of one synthetic target contact returned one Active participant. Readback and the standard Tasks tab showed exactly one first-step task, with the first step description in Notes.
- No actual email was sent. Cross-version portability, DST combinations and two-user assignment remain outside this proof.

Regression: runtime type 43 maps to RichText and type 30 to LongText; both preserve their text values. `dotnet test clio.tests/clio.tests.csproj -c Release --filter "Category=Unit&(Module=ProcessModel|Module=Command)"`: 5,205 passed, 13 skipped. `CreateDataBinding_ShouldPreserveRichText_WhenSequenceSchemaIsAvailable` passed over the actual MCP process on both net8.0 and net10.0.

Docs and MCP reviewed: existing create-data-binding contract remains unchanged; help/details and actual MCP coverage updated. Command index and aliases remain accurate. ClioRing compatibility reviewed, no Ring-consumed contract changed: inspected ClioRing.Ipc, ClioRing and ClioRing.Desktop/actions.json for the binding commands.
