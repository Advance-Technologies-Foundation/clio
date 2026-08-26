# Test plan: localization-ready packages

**Issue**: [#1178](https://github.com/Advance-Technologies-Foundation/clio/issues/1178)
**ADR**: [adr-localization-ready-packages.md](../adr/adr-localization-ready-packages.md)

## Strategy

Separate structural generation, stand-free resource assertions, and actual Creatio behavior. A passing
XML test does not prove Creatio loads a resource; a live fallback test does not prove Clio generates valid
metadata. Both boundaries are required.

## Clio unit tests

| ID | Scenario | Expected |
| --- | --- | --- |
| TC-U-01 | `PackageCreator.Create(..., asApp: true)` | One package-derived source-code schema and resource folder exist. |
| TC-U-02 | Generated schema inventory | Descriptor, metadata, properties, C# source, and en-US resource are present; no template macros remain. |
| TC-U-03 | Generated source | Root namespace is package-derived and ownership comment forbids page/process/schema resources. |
| TC-U-03A | Generated localization adapter | Interface and concrete adapter exist; only the adapter constructs `LocalizableString`. |
| TC-U-03B | Application composition root | `ILocalizableStringResolver` resolves to `LocalizableStringResolver`. |
| TC-U-04 | Generated resource | Caption and one `LocalizableStrings.PackageLevelExample.Value` item exist. |
| TC-U-05 | `asApp: false` | No localization schema is generated. |
| TC-U-06 | `new-pkg`/nullable path | No localization schema is generated. |
| TC-U-07 | `AddPackageCommand` | `options.AsApp` is forwarded unchanged to `IPackageCreator`. |
| TC-U-08 | Non-identifier maintainer | Generated localization schema still uses a valid package-derived namespace. |

## Lab stand-free tests

| ID | Scenario | Expected |
| --- | --- | --- |
| TC-S-01 | Backend resource inventory | Backend keys exist in the source schema resources for both cultures. |
| TC-S-02 | Freedom UI resource inventory | UI keys exist only in the page schema resources. |
| TC-S-03 | Ownership isolation | Backend keys are absent from page resources and UI keys are absent from the backend schema. |
| TC-S-04 | Thin web service | Invalid transport input is rejected before delegation; valid input is passed once to a DI-resolved domain service. |
| TC-S-05 | Domain composition | The lab domain service calls current, strict, and fallback operations through `ILocalizableStringResolver`. |
| TC-S-06 | Default-only and missing fixtures | Default-only key exists only in en-US; missing key exists nowhere. |
| TC-S-07 | Documented fallback model | Requested culture wins, then primary culture, then missing produces no value. |

## Creatio-backed lab tests

| ID | Scenario | Expected |
| --- | --- | --- |
| TC-I-01 | Current culture en-US | Shared backend key resolves to the English value. |
| TC-I-02 | Current culture secondary | The same key resolves to the secondary-language value. |
| TC-I-03 | Strict explicit lookup | Existing values resolve exactly; a default-only secondary lookup has no value. |
| TC-I-04 | Fallback lookup | Default-only key requested in the secondary culture resolves to the en-US value. |
| TC-I-05 | Missing key | Strict and fallback lookup both report no value; string presentation is explicitly recorded. |
| TC-I-06 | Freedom UI schema manager | Page-owned key resolves from the page schema in both cultures. |
| TC-I-07 | Generated Clio package | Generated schema installs and resolves its example value through the same runtime path. |

## Manual UI verification

- Open the lab Freedom UI page in the primary language and record the page-owned captions.
- Change the test user's active UI language to the secondary language, sign in again, and record the
  different captions for the same keys.
- Restore the user language after the test.

## Regression commands

Clio uses the smart module filters selected from the final diff. The lab documents separate `dotnet test`
commands for stand-free and Creatio-backed categories. `clio-knowledge` runs its producer contract suite and
bundle generation check.
