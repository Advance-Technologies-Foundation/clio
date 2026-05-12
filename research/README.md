# Research: MCP components and versioning

Research on the evolution of the Freedom UI component catalog that is exposed to AI through the clio MCP server.

## Context

The clio MCP server exposes to AI agents a set of tools for working with Creatio: creating pages, schemas, components, managing environments. One of the key tools — `get-component-info` — returns to AI a curated catalog of Freedom UI components with property descriptions.

Current catalog:
- 92 components in 5 categories
- Hardcoded into [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json)
- No version metadata at all

This research defines the **target architecture**: how to maintain the catalog as the platform evolves (new versions add/change/remove components and properties), how to make this maintenance automatic based on the platform code, and how to choose the optimal storage and distribution channels.

## Documents

- [architecture.md](architecture.md) — target SoT architecture with three domains: creatio-ui (source) → composer-repo (integration) → clio (consumer). NuGet distribution, ownership matrix, failure-mode design, versioning of `Creatio.ComponentRegistry`.
- [clio-target-structure.md](clio-target-structure.md) — target registry structure in clio: per-entry `availability`, version resolver, `target-version` resolution stack, fallback policy, NuGet-based loader.
- [extractor-analysis.md](extractor-analysis.md) — extractor filters, the `**/designtime/**` invariant, the 192/92/100 numbers, the full list of 100 new candidates for seeding `overrides.json`.

## Target architecture

```
                  ┌──────────── creatio-ui ────────────┐
                  │   @CrtViewElement + *ViewConfig    │
                  │   + JSDoc (@since, @aiCategory)    │
                  │             │                      │
                  │   AST extractor (Jenkins on        │
                  │   branch-cut / push / GA-tag)      │
                  └─────────────┼──────────────────────┘
                                │ npm publish (GA only)
                                ▼
              ┌─────── @creatio/component-registry ─────┐
              │   per-version sharded JSON              │
              └─────────────┼───────────────────────────┘
                            │ npm install (composer CI)
                            ▼
   ┌────── creatio-component-registry-composer ────────┐
   │   supported-versions.json (manual PR)             │
   │   overrides.json (AI team owned)                  │
   │   ──────────────────────                          │
   │   diff snapshots → availability ranges            │
   │   merge overrides → unified bundle                │
   │   stamp metadata.json (provenance)                │
   │   dotnet pack + nuget push                        │
   └─────────────┼─────────────────────────────────────┘
                 │ NuGet publish (independent semver)
                 ▼
       ┌─── Creatio.ComponentRegistry NuGet pkg ───┐
       │   ComponentRegistry.json (unified)        │
       │   metadata.json (provenance)              │
       └─────────────┼─────────────────────────────┘
                     │ <PackageReference> in Directory.Packages.props
                     ▼
           ┌─────── clio.csproj ────────┐
           │   ComponentInfoCatalog     │
           │     (Assembly.GetManifest  │
           │      ResourceStream)       │
           │   IPlatformVersionResolver │
           │   MCP get-component-info   │
           └────────────────────────────┘
```

## Key architectural decisions

- **Three isolated domains** with an ownership matrix: creatio-ui (Platform-UI team), composer-repo (AI / clio team), clio (clio team). None of them has write access to another beyond the standard PR flow.
- **Distribution**: NPM (UI → composer) + NuGet (composer → clio). Independent semver on both chains.
- **Storage in clio = NuGet embedded resource.** The file `clio/Command/McpServer/Data/ComponentRegistry.json` is removed from the clio repo; the loader reads via `Assembly.GetManifestResourceStream`.
- **Runtime serving — 100% offline.** No network calls during an AI MCP session.
- **Version resolution**: explicit > environment-name + GetSysInfo probe > latest-fallback. Always returns `resolvedTargetVersion` + `resolvedFrom`.
- **Fallback policy**: `latest known` (the maximum `since` version, computed by the composer phase). Not `unrestricted`, not `error`, not `oldest`.
- **The composer lives in its own repo** `creatio-component-registry-composer` — single responsibility, isolated CI, separate from UI source and consumer.

## Status

The documents describe the **target state**. Implementation has not started yet; the current `ComponentInfoCatalog` remains unchanged.

Delivery stages are in [clio-target-structure.md](clio-target-structure.md#delivery-stages-target-architecture). There is no intermediate pilot stage; each stage finishes with a release of a NuGet/NPM package to the production feed.
