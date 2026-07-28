# ClioRing app-settings schema test plan

## Automated

- Parse the shipped sample and schema as JSON.
- Assert the sample points to `./app-settings.schema.json`.
- Assert schema descriptions explain `Channel`, `DevClioPath`, and `ClioIpc` semantics.
- Assert the schema is copied beside Ring test and publish outputs.
- Run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`.
- Run the mandatory Windows x64 NativeAOT publish.

## Local installation

- Confirm no Ring process holds the existing executable.
- Preserve `app-settings.json`, `Logs`, and `measurements`.
- Replace published program files from the verified NativeAOT output.
- Add `$schema` to the preserved settings without changing its `Channel` or Clio IPC launch configuration.
- Run a non-destructive IPC proof or startup harness against the configured development clio build.

