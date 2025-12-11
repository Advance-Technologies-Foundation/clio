# Step 1 Plan: Extend Creatio client with DELETE support

## Objective
Add HTTP DELETE capability to the Creatio client library and surface it through the clio abstraction, enabling downstream use by `call-service`.

## Deliverables
- New DELETE method in `CreatioClient` (and any interface/adapter) that can send DELETE with optional JSON body, cookies/CSRF, auth, retry settings.
- Adapter in clio (`IApplicationClient` + `CreatioClientAdapter`) exposing the new DELETE method.

## Findings from code scan (creatioclient/CreatioClient.cs)
- HTTP helpers: `ExecuteGetRequest` and `ExecutePostRequest` rely on a shared `Retry<T>` wrapper, honoring `_retryCount`, `_delaySec`, and `_retryPolicy` (Simple/Progressive).
- Auth/CSRF: POST uses `HttpClient` with bearer token when `_oauthToken` is set, otherwise cookies + `BPMCSRF` header pulled from `AuthCookie`; `CreateCreatioRequest` sets method, content-type, cookies, CSRF.
- Defaults: POST timeout default 10_000 ms; GET 100_000 ms; retries default (1,1). `SetRetryPolicy` lets callers override.
- Body handling: POST uses `StringContent` with `application/json`; GET has no body. No DELETE helper exists.
- Adapter: `IApplicationClient` and `CreatioClientAdapter` expose only GET/POST today.

## Tasks
1. **CreatioClient implementation**
   - Add `ExecuteDeleteRequest(string url, string requestData, int requestTimeout = 10000, int retryCount = 1, int delaySec = 1)` (align defaults with POST unless a better default is chosen).
   - Reuse `Retry<T>` and `CreateCreatioRequest`/`CreateCreatioHandler` paths; mirror POST auth branches (bearer vs cookie + BPMCSRF).
   - Allow optional body: when provided, set content length/stream (HttpWebRequest) or use `HttpClient` with `StringContent` to keep parity with POST.
2. **Interface exposure**
   - If an interface exists, declare DELETE method (matching POST signature defaults).
   - Implement in `CreatioClientAdapter`, forwarding parameters to the underlying client.
3. **Validation and defaults**
   - Keep behavior consistent with GET/POST defaults (timeouts, retries).
   - Ensure no breaking changes to existing methods.
4. **Light tests (if present in client repo)**
   - Add unit/contract test for DELETE happy path (mock handler) and retry path if test infra exists.

## Acceptance Criteria
- Calling the new method sends HTTP DELETE; when body provided, it is transmitted.
- Auth/cookies/CSRF handling mirrors POST behavior.
- Method is accessible via `IApplicationClient` (or equivalent abstraction) for clio callers.
- Build succeeds for both `creatioclient` and `clio` projects with the new API.

## Risks / Notes
- Obsolete warnings should stay unchanged; no refactor required now.
- Ensure conditional project reference in `clio` picks up the new API (build both solutions after changes).
