# Story 6: Follow-up checks of SOAP auth, WCF SOAP 1.2 and the usage guard

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST, SOAP web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - platform facts
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md) - D15
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-U-F*, TC-I-F*
**Status**: review
**Size**: S
**Depends on**: stories 2, 3
**Blocks**: nothing

---

## As a / I want / So that

As a coding agent, I want `modify-web-service` to stop me from repointing or re-authenticating a service a process
calls without the user's yes, and `read-wsdl` to tell me which port of a WSDL the platform can actually call, so that
a change I make does not silently alter or break the user's processes.

## Acceptance criteria

1. A `setBaseUri`, `setNamespaces` or `setType` that leaves the service different after the whole call is refused
   while a process calls the service, until `acknowledge-usages` names it; a `setAuth` likewise, for the methods with
   `use-auth`; `setRetryCount` never. The refusal names what changes.
2. After a change of the authentication, a notice names the methods with `use-auth` false.
3. `read-wsdl` warns about SOAP 1.2 ports, ports requiring WS-Addressing and ports requiring WS-Security message
   security, naming a SOAP 1.1 port to register instead (same service, scheme and host preferred), else the measured
   workaround.
4. `read-wsdl` takes `code` for a new service's code; `service-code` only for an existing service, checked before
   the download (ADR D16).
5. Knowledge records, ADR D15, capability map and the clio-knowledge guidance state the measured platform facts.

## Definition of done

- [x] Unit tests TC-U-F01..F03 pass.
- [x] E2E TC-I-F01..F03 pass on stand `Creatio`.
- [x] Pre-PR review gate run; Blocker/High fixed.
- [x] Guidance PR in clio-knowledge.
