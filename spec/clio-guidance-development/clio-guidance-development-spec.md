# Clio guidance development framework specification

## Goal

Provide a repeatable, evidence-driven way to research unfamiliar Creatio behavior and publish reliable clio MCP guidance that agents can use without the original lab or proprietary source tree.

## Requirements

- Keep one canonical guidance-development skill under `.ai`, with portable Claude and Codex entry points.
- Separate chronological lab evidence from projected agent instructions.
- Assign every rule one canonical guidance owner; routing contains names rather than duplicated rules.
- Publish separate ESQ guidance for frontend construction, backend C# construction, and runtime parsing.
- Publish a virtual-entity lifecycle guide covering schema-first creation, executor binding, bounded/authorized providers, and Creatio 10.0 write compatibility.
- Fail closed for unverified filter shapes, excessive filter complexity, missing authorization, non-pushable provider queries, and unsupported feature-management paths.
- Preserve stable guide names/URIs and decision-point links from relevant MCP tools.
- Add unit and real-process MCP E2E coverage for discovery and retrieval.

## Out of scope

- Shipping the lab package in clio.
- Claiming unverified ESQ operators, value types, update/delete write paths, or provider implementations.
- Adding a new remote Creatio feature-management MCP tool.
