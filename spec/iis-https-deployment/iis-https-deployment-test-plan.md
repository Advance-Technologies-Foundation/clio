# IIS HTTPS deployment test plan

Issue: #887

## Automated tests

- TC-U-01: certificate eligibility accepts exact/wildcard DNS server certificates and rejects expired, not-yet-valid, missing-private-key, wrong-host, and incompatible-EKU certificates.
- TC-U-02: a usable pin wins; stale pin warns and deterministic latest-expiration ordering wins.
- TC-U-03: no candidate resolves actual HTTP and emits the certificate fallback warning.
- TC-U-04: IIS request contains exactly one HTTP or HTTPS binding; HTTPS attaches the expected `My` thumbprint.
- TC-U-05: .NET Framework XML transformation changes the two root config sources and `wsService encrypted`, is idempotent, and is skipped for HTTP/.NET 8.
- TC-U-06: registration, terminal receipt, and launch receive the actual strategy URL.
- TC-U-07: `pin-certificate` explicit, interactive, clear, invalid, conflict, and no-candidate paths persist or avoid mutation correctly.
- TC-U-08: settings pin round-trips and the schema template documents uppercase 40-hex thumbprints.
- TC-I-01: generated schema is created, unchanged when current, and atomically refreshed when stale under an isolated `CLIO_HOME`.
- TC-U-09: MCP argument mapping defaults `useHttps` false and maps true.
- TC-E-01: a real stdio MCP server advertises optional `useHttps` and safely rejects an invalid archive before any local lifecycle mutation.
- TC-U-10: Ring plan validation/JSON/summary and view model preserve Local HTTPS and ignore it for Rancher.

## Developer-local validation (never TeamCity)

These are explicit manual destructive checks, marked LocalOnly/Explicit or executed directly; they must hard-skip when `TEAMCITY_VERSION` is present.

- TC-L-01: deploy the approved .NET Framework archive with HTTPS, verify one HTTPS binding/certificate, three XML changes, registered HTTPS URI, and endpoint; uninstall and verify settings/IIS/path/database cleanup.
- TC-L-02: deploy the approved .NET 8 archive with HTTPS, verify one HTTPS binding/certificate, no application XML changes, registered HTTPS URI, and endpoint; uninstall and verify complete cleanup.
- TC-L-03: with certificate discovery replaced/disabled in a local harness, prove requested HTTPS falls back to HTTP without deployment failure. Do not alter the workstation certificate store.

## Required commands

- Build affected projects with analyzers.
- Full `Category=Unit` suite because `BindingsModule.cs`, `Program.cs`, and `Common` change.
- Relevant no-environment MCP E2E fixture on net8.0 and net10.0.
- `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`.
- Windows x64 NativeAOT publish required by policy.
