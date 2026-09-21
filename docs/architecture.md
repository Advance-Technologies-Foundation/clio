# Architecture

## Dependency and ownership boundaries

Product selects adapters; adapters translate input and present results. Composition owns policy. Core resolves an environment, selects a bundle, takes a same-target gate and opens a session. The root owns it until completion. Nested workflows borrow it and never acquire another root gate. Recursive calls fail.

Composition setup references Primitives for defaults; workflows consume Contracts interfaces. Core references Contracts/DI, never concrete Primitives or SDKs. Adapters reference Composition only among Clio projects. Contracts has no implementation dependency. Partner.Composition references Contracts and Microsoft DI only; it does not depend on the vendor workflows, Core, Primitives or Creatio SDK.

Aliases of a normalized application URI share a gate within one Core. Different Core instances/processes do not coordinate. Await children sequentially and finish them before returning. Overlapping children fail with concurrent-child-not-supported. If a parent returns early, Composition drains its outstanding child before disposing the scope/session and reports unawaited-child-outcome-unknown. This prevents disposal during child execution, but a child that ignores cancellation can delay completion. Async disposal completes before releasing the gate. All roots for the same target remain serialized, including reads. This is deliberately conservative, not a general resource scheduler.

## Capabilities and versions

Core exposes a capability provider through the borrowed workflow context. `context.Get<IFileSystemPrimitive>()` or `context.Get<IClioPrimitive>()` resolves a typed interface marked with its capability name. Contracts is shared for type identity. Core verifies declared capability implementations when opening a session; construction should be cheap and external clients lazy. No Core property is needed per capability. Changing a public capability interface requires explicit ABI compatibility treatment.

One complete bundle satisfies the root requirement and stays pinned for all children. A child requiring a missing capability or incompatible version fails before that child executes. Core never mixes bundles mid-workflow. Authors declare the union of capabilities their workflow may need. Context access enforces the executing workflow declaration even when a bundle contains extra capabilities; child declarations must fit the root declaration. An undeclared access returns capability-undeclared. These are runtime checks, not static proof that arbitrary partner code follows its declaration. HTTP-only fixture V1 and HTTP+filesystem V2 test this distinction.

Sessions own capabilities and authentication. Initial successful HTTP login is memoized within one sequential session; SDK expiry recovery remains SDK-owned. Local-only environments can omit BaseUri and credentials. Their gate is keyed by environment name; file conflicts across differently named environments are not inferred.

Each immediate child of a configured bundle directory contains complete dependency output, .deps.json and bundle.json:

```json
{
  "Version": "10.0.0.0",
  "ContractVersion": 2,
  "Capabilities": ["http"],
  "Assembly": "Clio10.Primitives.dll",
  "EntryType": "Clio10.Primitives.HttpPrimitiveBundle"
}
```

Numeric versions normalize missing build/revision components to zero, so 10.0 and 10.0.0 select 10.0.0.0. ABI 2 introduces capability-based sessions; ABI 1 is rejected. Core filters manifests before activation and verifies loaded metadata. Broken candidates may fall back to older compatible releases; exact selection cannot choose another version. Matching duplicate versions fail. Failed activation is cached for the catalog lifetime. New immutable directories are discovered at the next root selection; changed existing directories are not reloaded. An explicit bundle directory replaces injected defaults as the catalog source. A missing directory returns bundle-directory-unavailable. Manifests must explicitly name their assembly and entry type; Core has no vendor implementation name default.

AssemblyLoadContext/AssemblyDependencyResolver isolates managed dependencies while sharing Contracts. This is trusted code loading, not a sandbox. Collectible contexts permit eventual unloading, not immediate unload/hot replacement. Fixtures prove same-name assemblies with different capabilities coexist; they do not prove conflicting private dependency versions or arbitrary native-library compatibility.

## Workflow contract

WorkflowRegistration supplies immutable descriptor and requirement metadata. IClioWorkflow supplies only asynchronous execution. Discovery reads metadata without activating handlers. Implementations use keyed scoped DI registration under the descriptor name; each root creates one execution scope shared by its children. DI owns and disposes the handlers. An unresolvable constructor fails only that operation with workflow-unavailable. Duplicate names fail discovery and execution with duplicate-operation; register a module once, since duplicate registration is intentionally an error. CompositionRequest carries operation name, environment and portable arguments: strings, bool, int/long, decimal/finite double, nested string-key dictionaries and arrays. Snapshots copy containers before execution, with a depth bound of 32. Descriptors define allowed fields, required values, types and optional nested object fields. Composition checks these before creating a session; workflows validate domain values. This intentionally small input model is not a general schema language.

JSON remains an adapter concern. Both supplied adapters omit Password when serializing a ClioEnvironment record, including nested payloads. Custom hosts choose their own presentation policy; a trusted workflow can still deliberately copy a secret into another value. Adapters convert integers to Int64, otherwise attempt Decimal, then finite Double outside Decimal range. Numeric precision is bounded by those types. Non-finite values and duplicate keys visible to conversion are rejected. Both CLI and MCP preserve JSON objects through conversion and reject duplicate keys at every level, including the top-level arguments object. Child InvokeAsync takes replacement arguments; omission means empty input, never parent inheritance.

OperationResult returns acceptance, code, endpoint response, selected version, optional accepted-step names and data-only Payload. Managed callers can consume a typed record from a workflow package; adapters serialize the same record. Do not return service instances or serializer-specific nodes. Workflow authors own payload shape and transport serializability. Progress is separate. Partial acceptance is not rollback or durable workflow execution.

Partner workflows demonstrate reuse of vendor flush/restart and real filesystem capability access. A two-file composition calls the same child twice with different inputs and returns typed summaries. A host registers these through DI; vendor composition has no partner reference. Plugin-folder discovery is deferred.

## Adapter discovery

CLI uses generic --list/--execute dispatch. MCP exposes list-operations/execute. Newly registered workflows appear without per-command adapter edits. Applications register partner modules through AddClioCli/AddClioMcp callbacks.

This changes the experimental MCP contract: call execute with operation=restart or flush-redis instead of the earlier resident tool names. It does not prove that a running agent refreshes resident tool definitions. No Clio 8 contract changes are involved.

## Root lifetime and outcome ownership

Core.RunAsync owns the context around a callback; callers cannot release the gate themselves. A per-Core asynchronous execution marker rejects nested roots, including a different environment, with nested-root-not-supported. Multi-environment applications orchestrate separate top-level roots after each prior root completes; no atomicity is promised across them. Trusted code can bypass the marker by suppressing execution-context flow or creating another Core; this is not a security boundary.

The callback result or exception takes precedence over cleanup failure. Core records a safe session-cleanup-failed diagnostic and releases the gate; workflow-scope cleanup logs a safe type-only diagnostic. Unexpected exceptions during workflow execution become unexpected-failure, which does not establish whether effects occurred. Exceptions before workflow activation become workflow-unavailable. Only cancellation requested by the caller is classified as cancellation.

Adapters preserve Accepted, Code, Response, PrimitiveVersion and AcceptedSteps when a payload cannot be serialized. They add ReportingError=result-serialization-failed and RetryAdvice=do-not-retry-automatically. CLI success status and MCP IsError remain tied to acceptance: reporting failure does not undo accepted work. Unknown execution outcomes must not trigger automatic retries.

## Bundle release boundary

One complete runtime release contains Composition workflows and primitives. Core captures that release for each root; workflows and children cannot mix generations. The existing primitive-only mode remains available for static Composition hosts. See [complete runtime update proof](runtime-update-proof.md) for the borrowed host contract, private runtime container, compatibility checks, atomic acquisition, failure behavior and evidence. Core and transport adapters remain running. Automatic unloading and independently versioned capability packages remain deferred.

## Settings

IEnvironmentResolver hides storage. Defaults use in-memory environments; SettingsPath selects a JSON dictionary:

```json
{
  "development": {
    "BaseUri": "https://creatio.example/",
    "UserName": "example-user",
    "Password": "example-only",
    "IsNetCore": true,
    "AllowUntrustedCertificate": false
  }
}
```

Core reads snapshots per root invocation. Invalid settings yield safe codes. Editing/migration and cross-process synchronization are not implemented. Hosts can substitute secret-store resolvers. Filesystem protection and secret lifecycle are host responsibilities.

## Simplicity check

Intent: reusable workflows, replaceable primitives and minimal host burden. Flow: adapter -> workflow -> one Core context -> primitive -> data result. The registry enables partners, shared context prevents nested deadlock, manifests support version selection, and the resolver hides storage. The optional updater uses NuGet V3 and publishes complete directories. No broker, workflow engine, cross-process lock or additional execution protocol connects these layers.
