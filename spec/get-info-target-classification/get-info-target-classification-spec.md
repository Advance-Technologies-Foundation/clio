# get-info target classification - SPEC

> GitHub: [#390](https://github.com/Advance-Technologies-Foundation/clio/issues/390)

## Problem

`get-info` treats every failure from the required
`ApplicationInfoService.GetApplicationInfo` probe as a generic exception. Reachable non-Creatio
HTML therefore exposes a Newtonsoft.Json parser message, while transport failures can be mistaken
for missing ClioGate support. The broad failure path also lets optional ClioGate compatibility
checks replace an already successful base report.

## Requirements

R1. Validate that the configured application URI is an absolute HTTP or HTTPS URI before the base
probe.

R2. Classify required-probe failures into stable user-facing outcomes: invalid URI, unreachable or
timed-out target, authentication failure, reachable non-Creatio content, and unexpected Creatio
response.

R3. Normal CLI and MCP output must not expose response bodies, parser messages, stack traces,
credentials, cookies, tokens, or authorization data. Debug output may identify safe exception and
classification metadata only.

R4. Perform `GetSystemEnvironmentInfo` and ClioGate enrichment only after a valid base report is
available. Any optional-enrichment failure must preserve the base report and exit code 0.

R5. Preserve the successful report schema, command aliases, options, and MCP tool arguments.

## Acceptance criteria

- AC1. HTML or other clearly non-JSON content exits 1 with a `does not appear to be a Creatio
  application` error through `get-info` and every alias.
- AC2. Transport failures and timeouts exit 1 with a connection/application-unavailable error.
- AC3. Authentication failures exit 1 with credential/authentication guidance and are not
  classified as non-Creatio.
- AC4. Malformed JSON and valid JSON with an unusable ApplicationInfoService shape exit 1 with a
  stable unexpected-response error.
- AC5. Valid Creatio responses still return the base report when ClioGate is absent, incompatible,
  denied, malformed, or unreachable.
- AC6. CLI unit tests cover every classification and both enrichment paths.
- AC7. MCP unit and external-process E2E tests assert the same actionable error envelope.
- AC8. Command help, detailed docs, command index, Wiki anchors, MCP descriptions, guidance, and
  ClioRing consumption are reviewed and validated.

## Exclusions

- No successful report fields or MCP arguments are added or removed.
- No new Creatio endpoint, ClioGate package change, or local Creatio deployment is required.
- The shared `IApplicationClient` transport contract is not changed; classification remains scoped
  to `get-info` because other commands have different retry and side-effect semantics.
