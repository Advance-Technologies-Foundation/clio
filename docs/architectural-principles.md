# Architectural principles

These are the working rules for Clio 10. Contributors follow them when placing code and reviewing changes. They can evolve through an explicit architectural decision; a feature implementation must not silently change a boundary. Clio 8 supplies evidence of expected behavior, not the structure of the new implementation.

## Choose a layer by responsibility

| Responsibility or example | Owner | Boundary |
|---|---|---|
| Select CLI or MCP and manage application shutdown | Product (`src/Clio10`) | No command-specific behavior or primitive wiring. |
| Parse CLI input, expose MCP tools, serialize results, present progress | Adapter (`src/Cli`, `src/Mcp`) | Calls Composition. Does not reference concrete primitives or implement workflows. |
| Restart, deploy, choose deployment steps, interpret service responses, stop after a failed step | Composition | Uses declared capabilities through the managed context. No direct filesystem, database, process or network I/O. |
| Resolve settings/environment, select a compatible bundle, own the target gate and session | Core | Independent of concrete primitives, workflow identities, CLI, MCP and Creatio SDK types. |
| Make an HTTP request, restore a database, read/write a file, interact with a process | Primitives | Provides reusable external capabilities. Does not choose the overall deployment workflow. |
| Define invocation, results, lifecycle and compatibility metadata | Contracts | Stable resident host language without feature APIs. |
| Define HTTP, filesystem, archive and other feature interfaces/DTOs | PrimitiveContracts | Runtime-owned language used by Composition, Primitives and partner compositions. |
| Partner-specific sequencing or policy | Partner composition library | Registers independently; vendor composition has no partner reference. |

A primitive can contain several low-level calls needed to fulfill one capability. It need not be a wrapper around one HTTP verb. Composition decides why, when and in what sequence capabilities run.

Core's own settings and bundle-file access is an intentional exception to the external-I/O placement rule: it manages execution infrastructure. Application files, package contents and database operations belong to primitives. For example, deployment policy can ask a capability for platform information; it must not inspect the operating system or filesystem directly.

## Dependency rules

1. The shipped product chooses adapters. Each adapter references Composition only among Clio projects. A managed application can consume Composition without shipping CLI or MCP dependencies.
2. Composition's setup code may reference concrete primitives to supply defaults. Workflow implementation code consumes Contracts through its context; it must not resolve a concrete SDK or primitive implementation.
3. Core depends on Contracts and general hosting infrastructure. Adding a command must not require a Core edit. Adding an adapter must not require primitive edits.
4. Contracts stays serializer-neutral. Adapter JSON presentation and Core's settings format are separate concerns. Typed results return through ordinary asynchronous calls; logs are never the result transport.
5. Contracts is the stable host/runtime ABI (10.4.0.0 after this separation). Keep feature interfaces and DTOs in PrimitiveContracts, private to each complete runtime load context. Adding or changing a feature capability must not change the host ABI. Actual changes to host lifecycle/context/invocation still require a deliberate ABI change and host upgrade.
   See [primitive contract boundary](primitive-contract-boundary.md). Previously packaged releases remain unchanged; this relocation requires one host upgrade. Static primitive-only embedding explicitly shares its typed feature contracts and therefore retains exact feature-ABI compatibility requirements.
6. Defaults make ordinary embedding simple. Hosts override only what they need, before defaults are registered. Register a module once; duplicate operation names fail rather than silently overriding an implementation.

## Execution and ownership rules

- Discovery uses data-only metadata. Constructors do not perform operation work, and discovery does not construct workflow handlers.
- One root has one target, one selected complete bundle and one execution DI scope. In runtime-update mode its workflow registry and primitives belong to that same release. Core owns the session and gate; Composition owns the workflow scope. Children borrow both.
- Declare the union of required child capabilities and a compatible version range. Undeclared capability access and incompatible child requirements fail explicitly. Newest does not mean compatible.
- Await child calls sequentially through the context. Do not call a new root from a workflow, retain borrowed services, or return while background work still uses them.
- Keep identity and authentication state scoped to execution. Do not use mutable process-wide environment, credentials, current directory or workflow state.
- Use async APIs with cancellation. Preserve partial acceptance and distinguish known rejection from an uncertain outcome. A lost response does not prove failure; HTTP acceptance does not prove readiness.
- Cleanup and reporting failures must not erase known execution outcomes. Return data-only payloads; keep secrets out of results and progress. Adapter redaction is a safeguard, not permission to return credentials.

## When to stop and reconsider placement

Before implementing a class, state its responsibility and layer. A short explanation in the change description is sufficient for routine work. Split a class when its responsibilities belong to different rows above.

Discuss the design before expanding the implementation when a change would:

- add a forbidden project dependency or leak an SDK/transport type into a shared API;
- make Core know command names or business-specific sequencing;
- move settings paths, bundle selection or concurrency ownership into a workflow;
- require shared mutable state, concurrent children, multiple targets inside a root or mixed bundle versions;
- change a public capability interface, compatibility interpretation, ownership or retry semantics;
- require partner code to edit the vendor registry or introduce an abstraction used only by a hypothetical future feature.

Record the concrete requirement, why the existing boundary cannot satisfy it, the smallest alternative, compatibility effects and required proof. Update the architecture document when the decision changes a rule. This is a design checkpoint, not a requirement for a second reviewer on every class.

## Testing follows the boundary

Composition tests supply controlled primitive responses and verify policy, sequencing, cancellation and partial outcomes. Primitive tests exercise actual external interaction, with controlled failures and selected real-system checks. Core tests exercise version selection, settings, lifetime and coordination. Adapter tests exercise real input/output and protocol behavior. A packaged-product smoke test proves the pieces ship together.

Choose tests using dependency impact, not just the changed folder. Shared contract/lifetime changes require broader checks. A mocked response is not proof that a live platform exhibits that response. CI selection automation remains separate work.

## Porting from Clio 8

The blueprint is ready for incremental porting, not bulk source migration. Use this sequence for each capability:

1. Record the Clio 8 master commit used as evidence and the expected inputs, defaults, external effects, outputs and failure behavior. Do not modify the main checkout to obtain that evidence.
2. Identify reusable primitives already available. Add a primitive only for missing external capability; keep workflow policy in Composition.
3. Define the operation metadata, arguments, result and compatibility requirement. Decide intentionally which legacy behavior is retained or corrected.
4. Implement one complete feature slice with async execution and the established default wiring. Extend the generic adapters only if the interaction surface itself needs something new.
5. Verify workflow behavior with controlled responses, the external path with appropriate integration tests, and the packaged interaction. Record remaining parity gaps rather than implying full compatibility.

Restart and Redis flush are the reference slices. Their live checks establish endpoint acceptance, not readiness or exhaustive equivalence to Clio 8. Choose the next concrete capability and complete it through these layers before widening the port.

The opt-in update unit is a complete runtime containing Composition and primitives, described in runtime-update-proof.md. Runtime release wiring borrows the original Core through Contracts and must never create a second Core. Host replacement, automatic unloading, plugin-folder discovery, cross-process coordination and NativeAOT dynamic loading are not established by this blueprint. Do not smuggle those projects into an ordinary command port.

See [architecture](architecture.md) for mechanics and [design convergence](design-convergence.md) for review and verification evidence.
