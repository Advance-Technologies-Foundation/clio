# ENG-105475 Register a REST web service through the AI Toolkit: spec

Jira: ENG-105475 (epic ENG-92704, relates to ENG-92710 "Call web service element"). Research, options,
stand measurements and estimate: the research document linked from the PR (2026-11-05).

## Decision

Five non-resident clio MCP tools over the platform contracts the Studio web service designer already uses.
No CrtProcessBuilder operation (it would move the 8-operation / 6-gate security pins and need a rebundle)
and no cliogate endpoint (cliogate adds nothing but SQL, and is absent on many stands).

| Tool | Platform calls |
| --- | --- |
| `register-web-service` | `CheckUniqueSchemaName`, `SelectQuery` (SysPackage, SysSettings, OAuthApplications), `ServiceDataContract`, `ServiceSchemaRequest` |
| `list-web-services` | `SelectQuery` on `VwWebServiceV2` (no `MetaData`) |
| `describe-web-service` | `ServiceSchemaRequest`; with `usages`, paged `SelectQuery` on `SysSchema` process rows |
| `modify-web-service` | `ServiceSchemaRequest`, the usage scan, `ServiceDataContract` |
| `test-web-service-method` | `rest/CallServiceSchemaService/Execute` |

## Platform facts the design rests on

- The save (`ServiceSchemaDesigner.Save`) runs no validation and finds an existing schema by name first: a
  duplicate code overwrites that service, UId included. clio refuses a taken code (`CheckUniqueSchemaName`).
- `ServiceManagerService` has no `CanManageSolution` gate, unlike the entity designer. clio checks it first.
- `ServiceSchemaRequest` returns `authInfo` values (a Basic password included). Every outward view is built
  by `WebServiceSchemaView`, which masks constant credentials, credential-like constant defaults and
  `user:password@` in the base URI.
- The designer joins base URI and method URI verbatim; the base URI is stored with a trailing slash.
- A default bound to a missing system setting is dropped from the request without an error at run time
  (measured on 10.2.565); a reference to a missing setting is refused.
- A Call web service element stores the service UId in `IX1` and the method name in `IX2`; `SysSchema.MetaData`
  is JSON, so the usage scan matches the UId textually and parses only the matching rows.

## Deviations from the research recommendation

- Package data binding of referenced system settings is a notice, not an automatic binding (decision Q7):
  the rows Studio writes were not captured yet; `data-binding` does it on request.
- No `MetaDataModifiedOn` concurrency check: modify reads and writes in one call, so the window is the call.
- The usage scan uses ESQ paging only, no cliogate SQL path: measured at about 15 s for 1,298 process rows.

## Verification

- `clio.tests` `WebServiceDesigner*Tests` (24 cases): validation, credential refusals, UId preservation,
  secret stripping over a designer-shaped fixture, the usage guard, rights.
- `clio.mcp.e2e` `WebServiceToolsE2ETests` (Sandbox): register, list, describe, constant-header refusal and
  setMethod on a throwaway service that teardown removes; the usage guard over the shipped
  `BingSearchApiCall` / `BingSearchProcess` pair. Green on a 10.2.565 .NET Framework MSSQL stand.

## Follow-ups

- clio-knowledge article `process-web-service-registration` and the pointer from the ENG-92710
  `process-web-service` article's "task without details" section.
- Automatic package data binding once the designer's rows are captured.
