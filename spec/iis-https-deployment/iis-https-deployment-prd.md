# IIS HTTPS deployment PRD

Issue: #887

## Problem

`deploy-creatio --use-https` reports an HTTPS URL but the IIS binding, registered environment, and browser launch remain HTTP. Corporate Windows hosts often have AD-issued machine certificates, sometimes more than one, while developer machines may have none. .NET Framework additionally needs HTTPS-specific Creatio configuration.

## Requirements

- FR-01: `--use-https` requests HTTPS only for the selected deployment; the site has exactly one HTTP or HTTPS binding.
- FR-02: A usable certificate is in `LocalMachine/My`, matches the machine FQDN, is time-valid, has a private key, and permits server authentication.
- FR-03: Prefer a usable pinned thumbprint; otherwise choose the usable candidate with the latest expiration, then latest start date, then thumbprint.
- FR-04: No usable certificate is a warning, not a failure. Deploy over HTTP and report/register/open the actual HTTP URL.
- FR-05: For actual .NET Framework HTTPS, switch the root ServiceModel behavior/binding config sources to `https` and set the inner Microsoft `wsService encrypted="true"`; changes are XML-aware and idempotent.
- FR-06: .NET 8 IIS HTTPS changes only the IIS binding.
- FR-07: `pin-certificate --thumbprint`, interactive `pin-certificate`, and `pin-certificate --clear` manage the root `iis-certificate-thumbprint` setting.
- FR-08: The appsettings JSON schema documents the setting and existing generated schema files refresh atomically when the bundled template changes.
- FR-09: MCP `deploy-creatio` gains additive `useHttps`; ClioRing local deploy gains an HTTPS choice and remains NativeAOT-compatible.
- FR-10: No TeamCity E2E may require a Creatio ZIP or mutate local IIS/database state.

## Acceptance criteria

- AC-01: One matching certificate produces one HTTPS binding with the matching machine-store thumbprint.
- AC-02: Multiple certificates honor a usable pin; absent/stale pins use the deterministic fallback.
- AC-03: No usable certificate completes as HTTP with a warning and no HTTPS file changes.
- AC-04: .NET Framework HTTPS transforms all three required attributes and a second pass is a no-op.
- AC-05: .NET 8 application files are unchanged by IIS HTTPS setup.
- AC-06: registration, terminal receipt, and browser launch use the actual selected scheme.
- AC-07: pin, interactive selection, clear, validation, settings reload, and schema refresh are covered.
- AC-08: MCP/Ring contracts are additive and validated through unit, safe stdio E2E, Ring tests, and Windows x64 NativeAOT publish.
- AC-09: The two user-approved archives are validated only through explicit disposable local Windows runs and fully cleaned afterward.

## Exclusions

- Importing certificate files into Windows stores.
- Changing non-IIS `dotnet` HTTPS hosting.
- Adding a certificate-management MCP tool.
- ZIP-backed deployment tests to TeamCity.
