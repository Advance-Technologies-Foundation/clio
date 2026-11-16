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
| `list-web-services` | `SelectQuery` on `VwWebServiceV2`; `MetaData` read only for the base URI, user information removed |
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
- Studio's method and parameter pages validate the method code and every parameter code against
  `SchemaNamePrefix` unconditionally (`RestWebServiceMethodPage` / `ServiceParameterPage` `_prefixValidator`):
  a method whose codes lack it opens but cannot be saved (measured 2026-11-11 on a .NET Framework stand with
  prefix `Usr`). clio requires the prefix on every method and parameter code it introduces, and keeps a code
  the service already has, since renaming a called method breaks its processes. No prefix set: no rule.
- A single (non-array) Object parameter is refused. Studio forbids it ("You can select Object type only if Is
  Array attribute set"), its JSON quick setup flattens a nested object into fields with dotted paths, a Call web
  service element maps a single Object to a plain Object parameter with no fields, and CrtProcessBuilder refuses
  it. A dotted request path renders as a nested body object (Terrasoft.Services JsonRequestBodyRenderer);
  measured 2026-11-11 with a test call and a process run.
- Response parameters are read from the body, a header or a cookie (Studio offers the three; the platform reads
  header and cookie values by path from an ordinal dictionary keyed as the server sent the name, so the letter
  case must match - measured: x-wsm read, X-Wsm null). describe reports the real placement, so a setMethod
  rebuilt from it no longer rewrites them as body values. As Studio, a GET has no body request parameter and an
  xml method no body parameter in request or response (RestWebServiceMethodPage._contentTypeValidator); the
  platform builds REST bodies as JSON only, so such calls failed ("Rest:Xml", "Cannot send a content-body").
- test-web-service-method masks a JSON body value by value (numbers and booleans whole) and every property name
  that carries a secret (fail closed; colliding masked keys get a #n suffix), and keeps a key without a secret
  readable; JSON the walk rejects is masked as plain text.
- describe-web-service output is valid modify input: the auth block (oauth-app-id, read-only flag) and method
  parameters (default-masked) are accepted back. A kept parameter whose secret constant describe hid keeps it when
  no default is sent, so a setMethod rebuilt from describe does not erase a Studio-set key; default-value "" clears
  it, and moving it to a place where describe would show it is refused.
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

- `clio.tests` `WebServiceDesigner*Tests`: validation, the prefix and path rules, credential refusals, UId preservation,
  secret stripping over a designer-shaped fixture, the usage guard, rights.
- `clio.mcp.e2e` `WebServiceToolsE2ETests` (Sandbox): register, list, describe, constant-header refusal,
  unprefixed-code refusal and setMethod on a throwaway service that teardown removes; the usage guard over a
  throwaway service and a process the test builds with a Call web service element (CrtProcessBuilder 1.6.6.150
  or later, else Ignored), so it no longer depends on a shipped package being installed. The OAuth 2.0 case
  creates and removes its own OAuth application (placeholder client id, no secret).

## Follow-ups

- clio-knowledge article `process-web-service-registration` and the pointer from the ENG-92710
  `process-web-service` article's "task without details" section.
- Automatic package data binding once the designer's rows are captured.
