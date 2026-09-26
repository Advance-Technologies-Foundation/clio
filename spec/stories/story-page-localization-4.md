# Story 4: Section title in another culture — `update-app-section --caption-culture`

**Feature**: page-localization
**Jira**: [ENG-90576](https://creatio.atlassian.net/browse/ENG-90576)
**Spec**: [spec-page-localization.md](../prd/spec-page-localization.md) — "page/section titles"
**ADR**: [adr-page-localization.md](../adr/adr-page-localization.md) — D11 (decides OQ-1), facts F10-F13, Evidence E7
**Test plan**: [tp-page-localization.md](../test-plans/tp-page-localization.md) (story-4 cases are listed below)
**Status**: review
**Size**: M
**Depends on**: story 1 — only its `ICreatioCultureCatalog` (already in `clio/Command/Localization/`)
**Blocks**: nothing

---

## Why this story is bigger than "add one argument"

Measured on Creatio 10.2.254 (ADR E7):

- The section title is `SysModule.Caption`; `en-US` lives in `SysModule`, every other culture in the `SysModule`
  localization table (F10).
- EVERY `update-app-section` call — even an `icon-background`-only one — deletes all non-default cultures of the
  section caption and description (F11, `AppSectionManager.ClearSysModuleLocalization`, RND-34912 workaround).
  A `caption-culture` argument alone would be undone by the next plain call.
- DataService `UpdateLocalizationQuery` on `SysModule` merges per culture and is the only write that targets a
  culture other than the connected user's (F12). It drops cultures that are not `SysCulture` rows with
  `success:true`.
- A direct localization write leaves the application package's `SysModule_<SectionCode>` data binding stale
  until the binding is re-saved (F13).

## As a

coding agent translating an application into an additional language

## I want

to set a section title in `es-ES` (or any culture the environment has) without touching the other languages

## So that

the translated application shows the right section name in every language, and a later metadata change does not
silently delete the translations.

---

## Acceptance criteria

- **AC-1** `update-app-section` accepts `caption-culture` on the CLI (`--caption-culture`) and on the MCP tool
  (`caption-culture`). Target culture = explicit value → the connected user's profile culture → `en-US`
  (`ICaptionCultureResolver`). The explicit value is canonicalized (`de-de` → `de-DE`).
- **AC-2** `caption-culture` without `caption` fails before any request: `caption-culture requires caption.`
- **AC-3** An explicit culture that is not a `SysCulture` row fails before any write with the
  `ICreatioCultureCatalog` message naming the Languages section and the available cultures. An inactive culture is
  written and the result carries a warning. A failed `SysCulture` read fails the command.
- **AC-4** A caption for a culture other than the profile culture is written through `UpdateLocalizationQuery` on
  `SysModule` only; it is not sent through the `ApplicationSection` update. The query carries the current
  profile-culture caption (required by the platform, F12).
- **AC-5** Every call snapshots the section's localization rows (`Caption`, `Description`, `ModuleHeader`) before
  any write and, when the `ApplicationSection` update ran, restores them afterwards (except the cells that call
  just wrote). An icon-only update keeps `es-ES` / `de-DE` (fixes F11).
- **AC-6** After any localization write the `SysModule_<SectionCode>` binding of the application package is
  re-saved unchanged (F13). No binding found, or a failed re-save → warning in the result; the command still
  succeeds because the values are stored.
- **AC-7** Readback: the target culture's caption equals the requested value and every snapshot culture still
  holds its old value; otherwise the command fails and names the cultures.
- **AC-8** The script guard checks `caption` against the target culture and `description` against the profile
  culture.
- **AC-9** The result adds the effective target culture, the stored caption in that culture, the kept non-default
  cultures and the warnings — MCP: `caption-culture`, `caption-culture-value`, `preserved-cultures`, `warnings`;
  CLI JSON: `CaptionCulture`, `CaptionCultureValue`, `PreservedCultures`, `Warnings` (inactive-culture and binding
  warnings are also printed as `[WAR]` lines). Existing fields are unchanged.
- **AC-10** Docs and MCP surface: `clio/help/en/update-app-section.txt`, `clio/docs/commands/update-app-section.md`,
  `clio/Commands.md` section, the MCP tool `[Description]`/argument description, and the
  `ToolContractGetTool` contract describe `caption-culture`. Localization maps stay rejected.

## Files to touch

| File | Change |
|---|---|
| `clio/Command/ApplicationSectionUpdateCommand.cs` | Options, request, service flow (D11 steps 1-5), result fields. |
| `clio/Command/ApplicationSectionLocalization.cs` (new) | `IApplicationSectionLocalizationClient`: read/write `SysModule` localization rows, re-save the package binding. |
| `clio/Common/ServiceUrlBuilder.cs` | `KnownRoute` `SelectLocalizationQuery`, `UpdateLocalizationQuery`, `GetSchemaDataDesignItem`. |
| `clio/BindingsModule.cs` | Register the new client (one line). |
| `clio/Command/McpServer/Tools/ApplicationTool.cs`, `ApplicationToolArgs.cs`, `ApplicationToolResponses.cs`, `ApplicationToolSupport.cs`, `ToolContractGetTool.cs` | MCP argument, catalog resolution, response fields, contract. |
| `clio/help/en/update-app-section.txt`, `clio/docs/commands/update-app-section.md`, `clio/Commands.md` | Docs. |
| `clio.tests/Command/ApplicationSectionUpdateServiceTests.cs`, `clio.tests/Command/UpdateAppSectionCommandTests.cs`, `clio.tests/Command/McpServer/ApplicationToolTests.cs` / `ToolContractGetToolTests.cs`, `clio.tests/Common/ServiceUrlBuilder.cs` | Unit tests. |
| `clio.mcp.e2e/ApplicationSectionUpdateToolE2ETests.cs` | E2E. |

## Test cases

- TC-U-40 `caption-culture=es-ES` + caption → one `UpdateLocalizationQuery` with `{en-US: <current>, es-ES: <new>}`
  for `Caption`; no `ApplicationSection` `UpdateQuery`.
- TC-U-41 icon-only update with a snapshot holding `es-ES` → `UpdateQuery` runs, then `UpdateLocalizationQuery`
  restores `es-ES` caption and description.
- TC-U-42 culture absent from `SysCulture` → fails with the Languages message; no write request is sent.
- TC-U-43 inactive culture → written; result carries the inactive warning.
- TC-U-44 `caption-culture` without `caption` → validation error before any request.
- TC-U-45 readback mismatch (target value not stored) → command fails naming the culture.
- TC-U-46 binding re-save: binding found → `GetSchema` + `GetBoundSchemaData` + `SaveSchema`; not found → warning.
- TC-U-47 `de-de` is canonicalized to the catalog spelling `de-DE`.
- TC-U-48 CLI command passes `--caption-culture` and the injected catalog to the service.
- TC-U-49 MCP tool maps `caption-culture` into the request and resolves the catalog for the call's environment.
- TC-U-50 `ToolContractGetTool` contract for `update-app-section` lists `caption-culture`.
- TC-U-51 route tests for the three new `KnownRoute` values (.NET Framework `0/` prefix and .NET Core).
- TC-E2E-40 `update-app-section` with `caption-culture` on a live stand: write `es-ES`, then an icon-only update,
  then read `list-app-sections` / localization rows — `es-ES` and `en-US` both present.
- TC-M-04 on stand `eng90576`: `es-ES` write keeps `en-US` and existing `de-DE`; `fi-FI` fails with the Languages
  message; icon-only update keeps `es-ES`; package binding snapshot holds the `es-ES` title.

## Validation

`dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=ApplicationCommand|Module=McpServer|Module=Command)" --no-build`
(`BindingsModule.cs` changes → the full unit suite before the PR).

## Definition of done

- [ ] AC-1..AC-10 met; TC-U-40..51 green; TC-E2E-40 added; TC-M-04 done on a live stand.
