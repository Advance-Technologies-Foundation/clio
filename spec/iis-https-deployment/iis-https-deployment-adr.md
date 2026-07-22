# IIS HTTPS deployment ADR

Issue: #887

## Decision

Introduce an IIS certificate catalog/resolver abstraction in `Clio.Common.IIS`, backed by `X509Store(LocalMachine, My)` on Windows and an empty non-Windows implementation. Return immutable certificate metadata rather than live certificate handles. Eligibility and deterministic ordering are pure logic and therefore cross-platform-testable.

`IISDeploymentStrategy` resolves the actual protocol before IIS mutation. When HTTPS was requested but no usable certificate exists, it writes a warning and changes the execution options to the actual HTTP outcome. The actual strategy URL becomes the single source for deployment logging, environment registration, terminal receipts, and browser launch.

Extend `CreateIISSiteRequest` with protocol, host, and certificate thumbprint. The handler creates one binding in its initial `appcmd add site` command. A binding service backed by `Microsoft.Web.Administration` attaches the selected `LocalMachine/My` certificate. No HTTP binding is first created for an HTTPS deployment.

Before IIS creation, an `INetFrameworkHttpsConfigurator` validates and updates both XML documents in memory, then writes only changed documents through temporary files. It runs only when framework detection and actual protocol both select .NET Framework HTTPS.

Add `PinCertificateCommand` using an injected selection prompt and settings repository. Store normalized uppercase hex under `iis-certificate-thumbprint`. `--thumbprint` and `--clear` are mutually exclusive; no arguments invoke the selector. Pinning validates current usability and FQDN matching. Deployment treats a later-invalid pin as a warning and continues through fallback selection.

The generated JSON schema is a derived artifact. `SettingsRepository.SaveSchema` compares it with the bundled template and atomically replaces it when different instead of returning merely because it exists.

MCP receives optional `useHttps=false`. Ring adds `UseHttps` to its AOT-safe immutable plan and renders a Local-only toggle. Certificate management remains CLI-only because interactive machine-store mutation has no approved agent use case.

## Failure semantics

- No usable certificate: success over HTTP with warning.
- Stale pin: warn, then deterministic certificate or HTTP fallback.
- Explicit invalid `pin-certificate --thumbprint`: command error, no settings mutation.
- XML/configuration or IIS binding failure after a certificate was selected: deployment failure; do not silently claim an HTTP deployment that was not intentionally created.

## Compatibility

Existing calls omit `useHttps` and remain HTTP. Existing settings omit the pin and use deterministic discovery. Ring JSON adds one optional/default-false field. No credential, private key, or certificate bytes are logged or persisted.
