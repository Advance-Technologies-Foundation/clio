# clio call-service: DELETE method support

## Summary
Add DELETE support to `clio call-service` so QA/Dev can exercise Creatio REST endpoints that require HTTP DELETE (currently only GET/POST are available). The change must preserve existing behavior and options.

## Goals
- Allow invoking Creatio services with HTTP DELETE via `clio call-service` (and aliases, if any).
- Keep request construction identical to GET/POST flows (headers, auth, cookies, JSON body where applicable).
- Ensure help/usage text and command validation mention DELETE.
- Provide tests that cover DELETE, including response handling and error paths.

## Non-Goals
- No change to default timeouts, retries, or authentication flows.
- No change to other HTTP methods beyond adding DELETE.

## User Stories
- As a QA engineer, I can run `clio call-service --method delete --url ...` to delete test data without external tools.
- As a developer, I can script DELETE calls in CI against sandbox Creatio using the same auth/session handling as existing GET/POST.

## Scope & Requirements
- New accepted method values: `delete` / `DELETE`. Case-insensitive, validated against existing allowed set.
- Syntax examples:
  - `clio call-service --method delete --url "https://host/0/rest/SomeService/Delete"`
  - `clio call-service -m delete -u "https://host/0/rest/SomeService/Delete" --headers "Authorization: Bearer ..."`
- Request building:
  - Use same header, cookie, and auth injection as current POST/GET pipeline.
  - Body: DELETE may include a JSON body if provided; do not block payloads. Respect `--body`/`--bodyFile` options if present.
- Output/exit codes: identical handling as other methods; propagate HTTP status and response content.
- Help/Docs: update command help text and `Commands.md` entry to list DELETE.

## Compatibility
- Backward compatible: existing scripts using GET/POST continue to work.
- If method is omitted, keep current default (likely GET or POST—confirm in implementation).

## Testing
- Unit tests in `clio.tests` covering:
  - Validation accepts DELETE and rejects unsupported methods.
  - Successful DELETE request constructs correct HttpRequestMessage with method DELETE and headers/body.
  - Error handling: non-2xx response returns same behavior as existing methods.
- If integration tests exist for call-service, add a DELETE case hitting a mock/fixture endpoint.

## Tasks (high level)
1) Update command options validation to include DELETE.
2) Update HTTP request factory to map method string to HttpMethod.Delete and allow body for DELETE.
3) Update help text and `Commands.md` entry to mention DELETE.
4) Add unit tests (NUnit + NSubstitute + FluentAssertions) for parsing and execution.

## Acceptance Criteria
- Running `clio call-service --method delete --url <endpoint>` issues an HTTP DELETE and returns the response.
- Body, headers, cookies, and auth options are honored for DELETE.
- Help and docs list DELETE as supported.
- Tests pass, showing DELETE is allowed and correctly mapped.
