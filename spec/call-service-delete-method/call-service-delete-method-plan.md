# Plan: add DELETE support to `clio call-service`

## Current state (quick scan)
- Command: `CallServiceCommand` in `clio/Query/DataServiceQuery.cs` uses `ExecuteServiceRequest` with a string `httpMethod`; switch only handles `"POST"` and `"GET"`, else defaults to POST. No validation/normalization of method.
- Request construction: body comes from `--body` or `--input`; variables replacement supported; output printed or written to file.
- Client API: `IApplicationClient` (adapter over `CreatioClient`) exposes only `ExecuteGetRequest`/`ExecutePostRequest`.
- Creatio client library (`creatioclient/creatioclient/CreatioClient.cs`) implements GET/POST with retry/cookies/CSRF; no DELETE helper.
- Docs/tests: `Commands.md` lists call-service but mentions only GET/POST; tests in `clio.tests/Query/CallServiceCommandTests.cs` cover options/shape, not method mapping nor DELETE.

## Implementation plan
1) **Creatio client library**
   - Add DELETE support to `CreatioClient`: new method (e.g., `ExecuteDeleteRequest(string url, string requestData, ...)`) reusing existing request creation (`CreateCreatioRequest`) and retry policy; allow optional body.
   - Expose DELETE in any relevant interface/abstraction (if an `ICreatioClient` interface exists, update it; otherwise just add the public method on `CreatioClient`).
   - Adjust `CreatioClientAdapter` to surface `ExecuteDeleteRequest` and wire through to `CreatioClient`.

2) **clio abstraction layer**
   - Extend `IApplicationClient` to include `ExecuteDeleteRequest` (with requestData and timeout/retry parameters similar to POST).
   - Implement the new method in `CreatioClientAdapter` forwarding to the underlying client.

3) **CallServiceCommand changes**
   - Normalize method to uppercase; accept `DELETE` alongside existing values; consider default when empty (keep current default to POST).
   - In `ExecuteServiceRequest`, route `"DELETE"` to the new `IApplicationClient.ExecuteDeleteRequest` and allow body.
   - Optionally tighten validation: if method provided and not in {GET, POST, DELETE} -> error.

4) **Docs/help**
   - Update `Commands.md` (call-service section) to list DELETE support and example.
   - Update command help text if any mentions methods.

5) **Tests**
   - Add unit tests in `clio.tests/Query/CallServiceCommandTests.cs` (or new file) verifying:
     - Method normalization accepts `delete` and calls `ExecuteDeleteRequest` on `IApplicationClient` with body and url.
     - Unsupported method raises error.
     - Default method remains POST when no method specified (regression guard).
   - If client library has tests, add coverage for the new DELETE method (happy path, retry path if applicable).

6) **Sanity/build**
   - Build both `creatioclient` and `clio` in Debug to ensure the conditional project reference picks up the new client API.
