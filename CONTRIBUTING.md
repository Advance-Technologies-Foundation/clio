# Contributing to Clio 10

This is a fresh architecture. Use Clio 8 as behavioral evidence; do not restore its source or historical dependencies.

Follow the [architectural principles and class-placement rules](docs/architectural-principles.md). They include design checkpoints and the per-feature porting process. Routine work stays within these boundaries; proposed boundary changes need an explicit rationale before implementation grows around them.

- Workflow, sequence or policy: Composition or a partner library implementing IClioWorkflow. Declare primitive requirements and register through DI.
- External capability: Primitives behind runtime-owned PrimitiveContracts, with real-system and controlled-failure tests.
- Environment/version/session lifetime: Core, independent of adapters and concrete primitives.
- Arguments, MCP mapping and output: adapter.
- Surface selection/shutdown: Clio10 product.

Test first-step failure, uncertain outcomes and partial acceptance. Nested calls borrow Core context, never acquire another root. Await children sequentially. Validate per-call arguments; avoid mutable singleton credentials.

Use interface-based DI, public XML documentation, NUnit/FluentAssertions, AAA, descriptions and explanatory assertions. Keep dependencies minimal and centrally versioned. Partner operations must not require vendor catalog changes.

Run affected tests during development, solution tests for shared contracts/lifetimes, pack/local install after packaging changes. Real restart/flush requires authorization for that environment. CI redesign is deferred; future selection must follow transitive impact, not just changed folders.

## Register a workflow

Publish metadata independently of the implementation. Register each module once:

```csharp
services.AddKeyedScoped<IClioWorkflow, MyWorkflow>("partner.my-operation");
services.AddSingleton(new WorkflowRegistration(
    new OperationDescriptor("partner.my-operation", "My operation."),
    new PrimitiveRequirement(new Version(10, 0), new Version(11, 0), Capabilities: ["http"])));
```

Do not use TryAddEnumerable for WorkflowRegistration records: it deduplicates by implementation type. Adapter callbacks register modules and service overrides; do not call AddClioComposition again inside the callback. The adapter already installs the standard composition once. Duplicate operation names intentionally fail instead of silently shadowing a vendor or partner operation. Constructors should only receive dependencies. Put operation work in ExecuteAsync. Core supplies one session per root, and Composition supplies one DI scope shared with children. Register custom defaults before AddClioComposition; adapter callbacks run before default registration. Logging services are available in both adapters without forcing a console sink.

Return data-only payloads and retain partial receipts when a child fails. Do not invoke a new root from inside a workflow or start concurrent children. Use context.InvokeAsync and await it. See docs/architecture.md for error categories and the complete-bundle compatibility limit.

Feature API changes belong to PrimitiveContracts and travel with complete runtimes. Core/Contracts must not acquire a dependency on them. Partner workflows may reference PrimitiveContracts; partners own compatibility with the runtime release they join. See docs/primitive-contract-boundary.md.
