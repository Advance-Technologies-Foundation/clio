# ENG-107277 Web-service replies under Copilot's 20 KB spill threshold

Sub-task of ENG-106667 (Copilot CAADT run cost). Copilot CLI hands a tool result to the model whole up to 20,480 bytes
and writes a larger one to a file, which the agent reads back in pieces, one model request per piece.

## Measured before the change (2026-11-23, clio master 4fb70074c, stand `Creatio`, .NET Framework)

| Reply | Bytes |
|---|---|
| `read-wsdl` CountryInfoService WSDL (21 operations, SOAP 1.1 + SOAP 1.2 ports) | 28,624 |
| `describe-web-service`, largest service on the stand (6 methods) | 6,044 (~1 KB per method, no limit) |
| `list-web-services`, 30 services | 5,191 |
| `test-web-service-method` | not measured; the body was cut only at 20,000 characters, the parsed values separately at 20,000 |
| contracts of the six tools (`get-tool-contract`) | 1.4-4.8 KB each |

## Decisions

- One budget for every reply that grows with the data: `McpResultSize.InlineReplyBudgetBytes` = 18,000 bytes, measured
  as the agent receives the reply (MCP result serializer options, UTF-8). `ProcessDigest.AutoSummaryThresholdBytes`
  now points at it instead of repeating the number.
- `read-wsdl` narrows in steps instead of jumping to names only: every port in full; then, with several ports, one
  port in full and the others by operation name; then operation names only (fewer names, then fewer warnings, while it
  is still over). The port returned in full is the one the guidance tells the agent to register: the SOAP 1.1 port a
  warning names with "Register port ... instead" (`WsdlDocument.RegisterInstead`), else Studio's pick. Ports on one
  binding are near copies, so the two-port reply cost the agent the same operations twice.
  Rejected: always returning only Studio's pick. A small WSDL fits whole, and the agent shows the user every port
  before registering.
- The old 120,000-character limit stays as `MaxMappedTextCharacters`, a bound on the mapping work, and as
  `MaxNarrowestReplyBytes`: one port with one operation is returned whole up to it even over the budget, since reading
  again cannot narrow it and a spilled reply beats describing the method by hand.
- When the reply does not fit, warnings take at most half the budget (`MaxWarningBytes`); the rest are counted in one line with the ones the parser
  already left out. A generator that writes a long warning per element would otherwise leave no room even for the
  operation names an agent narrows a read by.
- The narrowing runs in the MCP tools, never in `WebServiceDesigner`: read-wsdl with `service-code` reads the existing
  service through `IWebServiceDesigner.Describe` and must see every method to keep its codes.
- `describe-web-service`, `register-web-service` and `modify-web-service` return as many methods in full as fit (a
  single method over the budget is still returned, or describing it by code would loop; with several, a large first one
  is listed by code rather than dragging the reply over), and list the rest
  in `methods-not-shown` (code, caption, parameter counts) with a notice; usages that still do not fit are cut last.
  `modify-web-service` keeps the methods its operations add or replace in full first. `describe-web-service` takes
  `methods` to read named methods.
- `test-web-service-method` cuts the masked body to the longest start that fits (a binary search: cutting characters by
  the bytes over emptied a Cyrillic or CJK body); when the parsed values alone leave no room they are omitted
  (`response-values-omitted`) and the body is cut again from its start.
- `list-web-services` takes `filter` (code, caption or package contains, ignoring case) and cuts a list over the
  budget with a notice to narrow it.

## Measured after the change

| Reply | Bytes |
|---|---|
| `read-wsdl` CountryInfoService WSDL | 15,361 (SOAP 1.2 port in full, SOAP 1.1 port by name) |
| `read-wsdl` `orders-two-ports.wsdl` fixture (22 operations, two ports) | ~14 KB (each port alone ~13 KB) |

Guidance changes (the read-wsdl reply shape, `methods`, `filter`, the split of the over-budget articles) are in
clio-knowledge under the same ticket.
