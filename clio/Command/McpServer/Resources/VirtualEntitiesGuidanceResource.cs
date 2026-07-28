using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical guidance for creating Creatio virtual entities, implementing their query executors, and
/// handling version-gated writes.
/// </summary>
[McpServerResourceType]
public sealed class VirtualEntitiesGuidanceResource {
	private const string ResourceUri = "docs://mcp/guides/virtual-entities";

	/// <summary>
	/// Canonical virtual entity lifecycle guidance accessible through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       # clio MCP virtual entities guide

		       ## Scope
		       Use this guide to create a Creatio virtual entity object, implement its
		       `IEntityQueryExecutor`, bind the executor to the schema, materialize provider data, and
		       validate the complete runtime path. For Creatio 10.0 and later, it also covers virtual
		       writes through `EntityEventListener`. Filter creation and parsing remain in their dedicated
		       ESQ guides, and generic listener mechanics remain in
		       `configuration-entity-event-listener`.

		       ## CRITICAL COMPATIBILITY FINDING: virtual writes require Creatio 10.0 or later
		       Entity-level virtual CRUD support was introduced in Creatio 10.0. It does not exist in
		       Creatio 8.3.4 or earlier. On an older environment, do not claim that `Entity.Save()`,
		       DataService, or ATF.Repository can drive the virtual insert/update listener lifecycle.

		       Before implementing a virtual write path:
		       1. Read `describe-environment` and inspect the environment's core version.
		       2. Stop if the version is earlier than 10.0.
		       3. On 10.0 or later, verify that the Creatio feature
		          `EnableVirtualEntitySupport` is enabled. The feature metadata defaults to disabled.
		       4. Confirm the exact target environment and the required feature scope. There is currently no
		          dedicated MCP tool for remote Creatio feature state: `experimental` controls local clio
		          flags, and `clio-run` dispatches MCP tools rather than CLI verbs. Do not call either one.
		       5. When global enablement is approved and a shell is available, run:
		          `clio set-feature EnableVirtualEntitySupport 1 -e <environment>`.
		          The command invalidates the named feature cache. Do not substitute `clear-redis-db`. If a
		          different scope is required, inspect `clio set-feature -H` and stop rather than guessing;
		          if no shell is available, ask the environment operator to perform this step.
		       6. Restart only the confirmed target environment, then prove behavior with a real save.

		       The feature/version check applies specifically to Entity-level virtual CRUD. It does not
		       replace the schema and `IEntityQueryExecutor` prerequisites for virtual reads.

		       ## STOP: the virtual entity object must exist first
		       The virtual entity schema MUST already exist in Creatio before you start building its
		       `IEntityQueryExecutor`. Do not begin with a standalone executor class.

		       The existing object is the source of truth for:
		       - the exact schema name used by the `<EntitySchemaName>QueryExecutor` DI binding;
		       - the columns and data types available through `esq.RootSchema`;
		       - the primary key and primary display column the executor must materialize;
		       - the package metadata that must travel with the implementation.

		       Required order:
		       ```text
		       create virtual entity object
		         -> read back schema name, virtual flag, and columns
		         -> sync package files when linked
		         -> implement and bind IEntityQueryExecutor
		         -> build, restart, and execute a real query
		       ```

		       If the object does not exist or cannot be verified, stop. Do not guess the schema name,
		       create the executor binding from a caption, or invent columns in C# first.

		       ## 1. Create and verify the object
		       1. Read `app-modeling` and the installed contracts for `sync-schemas` or
		          `create-entity-schema`.
		       2. Read the user culture and schema-name prefix.
		       3. Search for the intended technical schema name to avoid duplicates.
		       4. Create the entity in the target package with:
		          - a prefixed technical schema name;
		          - `BaseEntity` as parent unless the design requires another parent;
		          - `is-virtual: true`;
		          - explicitly typed business columns.
		       5. Read the saved schema back and assert:
		          - the returned schema name is the intended technical name;
		          - `virtual=true`;
		          - every required column exists with the expected type and required state;
		          - the primary key and primary display column are correct.
		       6. In linked file-system mode, run `pkg-to-file-system` before editing package source.

		       A virtual schema normally has no physical table. Use schema APIs/MCP readback to prove
		       virtuality; use the database catalog only as an additional no-table assertion.

		       ## 2. Implement the executor
		       Put the executor in the package's C# source, for example:
		       ```text
		       Files/src/cs/VirtualEntities/<EntitySchemaName>QueryExecutor.cs
		       ```

		       Use a public class that implements `IEntityQueryExecutor`. Name the class after the
		       schema so the relationship remains obvious:
		       ```csharp
		       [DefaultBinding(
		           typeof(IEntityQueryExecutor),
		           Name = "UsrOrdersFeedQueryExecutor")]
		       public sealed class UsrOrdersFeedQueryExecutor : IEntityQueryExecutor {
		           public EntityCollection GetEntityCollection(EntitySchemaQuery esq) {
		               // Read provider data, apply the ESQ, and materialize esq.RootSchema entities.
		           }
		       }
		       ```

		       The binding name is exact and case-sensitive:
		       ```text
		       <EntitySchemaName>QueryExecutor
		       ```

		       For schema `UsrOrdersFeed`, the binding must be `UsrOrdersFeedQueryExecutor`. Use the
		       verified technical schema name from step 1. Do not use internal Creatio registration
		       attributes intended for core assemblies.

		       Add a unit test that reads `DefaultBindingAttribute.Name` and compares it with the schema
		       convention. A clean design may keep the entry executor thin and delegate example/provider
		       behavior to DI-resolved services, but the named `IEntityQueryExecutor` remains the schema's
		       stable Creatio entry point.

		       ## 3. Honor the incoming ESQ
		       Preserve this logical order:
		       ```text
		       provider data -> filters -> sorting -> paging -> selected-column materialization
		       ```
		       This is a semantic pipeline, not permission to enumerate an entire provider into memory.
		       Translate every supported predicate, order, and page operation into the provider query before
		       fetching rows. Enforce a documented maximum page size, provider timeout, and cancellation when
		       the provider API supports it. Reject non-pushable, unsupported, or unbounded requests rather
		       than silently loading and sorting the complete source.

		       - Filters: read `esq-filter-parsing`; it is the sole owner of runtime
		         `EntitySchemaQuery.Filters` traversal.
		       - Filter construction: enter through `esq-filters`, which routes to frontend JSON or
		         native backend C# construction.
		       - Sorting: preserve every requested order item and its direction. Use a stable final key
		         when deterministic paging requires one.
		       - Paging: apply row skip/count only after filtering and sorting.
		       - Columns: populate only columns selected by the incoming ESQ. If the request has no
		         selecting-column restriction, populate every supported schema column.

		       Filtering after paging returns incorrect pages. Materializing every provider field for a
		       narrow projection wastes provider work and can incorrectly mark unrequested values loaded.

		       ## 4. Materialize virtual records
		       For every provider row:
		       1. Create an entity from `esq.RootSchema` and the current `UserConnection`.
		       2. Set a stable, non-empty primary key.
		       3. Populate requested scalar values using the schema's verified column types.
		       4. For requested lookup columns, set the lookup Id and display value.
		       5. Set `StoringState = StoringObjectState.NotChanged`.
		       6. Add the entity to the returned `EntityCollection`.

		       Keep reusable projection/materialization plumbing outside focused example handlers when a
		       lab contains multiple scenarios. Each example should still own its small mock dataset so
		       its behavior and expected rows are reviewable in one place.

		       ## 5. Write through EntityEventListener (Creatio 10.0+ only)
		       Read `configuration-entity-event-listener` for the generic listener structure and
		       `configuration-entity-event-listener-tests` for its focused test patterns. Keep this guide
		       responsible only for the virtual-entity compatibility boundary and acceptance path.

		       Attach a thin listener to the verified virtual schema:
		       ```csharp
		       [EntityEventListener(SchemaName = "UsrOrdersFeed")]
		       public sealed class UsrOrdersFeedEntityEventListener : BaseEntityEventListener {
		           public override void OnInserting(object sender, EntityBeforeEventArgs e) {
		               base.OnInserting(sender, e);
		               Entity entity = (Entity)sender;
		               // Validate and delegate the provider create operation.
		           }
		       }
		       ```

		       With `EnableVirtualEntitySupport` enabled, a new virtual entity saved through the Entity API
		       follows this ordered lifecycle without inserting a row into a physical table:
		       ```text
		       OnSaving -> OnInserting -> OnInserted -> OnSaved
		       ```

		       A changed virtual entity follows:
		       ```text
		       OnSaving -> OnUpdating -> OnUpdated -> OnSaved
		       ```

		       Before any provider mutation, derive the caller identity from `UserConnection` and enforce
		       object/operation permission plus the same record and tenant scope promised by the virtual
		       entity. Fail closed when equivalent authorization cannot be established.

		       Delegate provider writes from the schema-bound listener to an injected service. Keep
		       external API/database logic, retries, and mapping out of the listener. Define transaction,
		       validation, cancellation, idempotency, and partial-failure semantics explicitly because
		       Creatio does not persist the virtual row for you.

		       Prove virtual create behavior through both entry paths:
		       1. Backend code creates the deployed schema's `Entity`, sets its values, and calls
		          `entity.Save()`.
		       2. ATF.Repository/DataService creates the generated model and calls `DataContext.Save()`.

		       Assert save success, the exact ordered listener events, the provider-side effect, and
		       query-executor readback of the written model. A listener unit test alone does not prove that
		       the environment version, feature state, DataService path, and platform event dispatcher work
		       together.

		       ## 6. Choose a provider
		       Prefer a separate physical Creatio backing entity/table for the first scalable provider.
		       It reuses the platform connection, pooling, backup, transaction, and multi-node
		       infrastructure. Keep the physical backing schema distinct from the virtual schema and map
		       its rows into virtual entities.

		       Never execute a normal ESQ rooted at the same virtual schema from inside its executor;
		       Creatio rejects SQL generation for a virtual root and recursive execution cannot provide
		       backing data. A mutable SQLite file packaged with the application adds writable-path,
		       upgrade, locking, backup, and multi-node responsibilities and is not the default scalable
		       choice.

		       Put provider access behind an interface so external APIs or databases can replace the
		       backing implementation without rewriting ESQ parsing and materialization. Low-level database
		       or external-provider access does not reproduce Creatio record permissions. Before every read
		       or write, derive the caller from `UserConnection`, enforce object/operation authorization and
		       the required record/tenant scope, and fail closed if the provider cannot express equivalent
		       authorization. Never fetch unrestricted rows and filter unauthorized records afterward.

		       ## 7. Build and validate
		       1. Build the linked workspace and run focused executor/unit tests.
		       2. Restart the Creatio environment so the new assembly and DI binding load.
		       3. Execute a real query through ATF.Repository/DataService or `execute-esq`.
		       4. Assert exact model values, selected columns, order, and page boundaries.
		       5. Use two callers with different permissions/tenant scopes and prove restricted records can
		          neither be read nor changed.
		       6. Use a large provider fixture and prove only the bounded requested page is fetched; also prove
		          oversized and non-pushable requests fail closed.
		       7. Inspect the package logger to prove the named executor ran.
		       8. When database inspection is available, confirm the virtual schema did not accidentally
		          acquire a physical table.

		       The real ESQ request is the decisive integration test: it proves schema discovery, exact DI
		       binding, executor invocation, request deserialization, query semantics, and virtual entity
		       materialization together.

		       ## Diagnostic-only shadow SQL
		       In an isolated lab, temporarily presenting `esq.RootSchema` as non-virtual before calling
		       `esq.GetSelectQuery(userConnection)` can reveal the SQL predicate Creatio would compile.
		       This is a semantic diagnostic, not a production execution strategy. Restore the original
		       `IsVirtual` value in `finally`. Treat `BuildParametersAsValue=true` output as sensitive.

		       ## Common failures
		       | Signal | Meaning | Action |
		       |---|---|---|
		       | Executor is being written before the schema exists | Binding and materialization contract have no verified source | Stop; create and read back the virtual object first. |
		       | `SelectFromVirtualSchemaException` | SQL generation was attempted against the virtual root | Query a distinct provider; do not recursively query the virtual schema. |
		       | Executor log is absent | Named DI binding did not resolve or request did not reach it | Verify exact `<EntitySchemaName>QueryExecutor`, build output, and restart. |
		       | Correct rows but wrong page | Operation order is wrong | Filter, sort, then page. |
		       | Correct values but projection is ignored | Every column is materialized unconditionally | Honor the incoming selected-column collection. |
		       | Schema exists in Creatio but not in package files | DB-first mutation was not flushed | Run `pkg-to-file-system` in linked mode. |
		       | Virtual `Entity.Save()` reports success but insert/update events are absent | Environment predates 10.0 or `EnableVirtualEntitySupport` is disabled | Read the core version, require 10.0+, enable the feature, restart, and rerun both save paths. |

		       ## Verification boundary
		       Verified in the guidance lab: schema creation/readback, no-table assertion, linked package
		       persistence, exact executor binding, selected/all-column projection, deterministic mock
		       records, sorting, paging, runtime logging, the filter group/Compare subset published by
		       `esq-filter-parsing`, and the Creatio 10.0+ virtual create lifecycle through both backend
		       `Entity.Save()` and ATF.Repository/DataService. Backend source tests establish the virtual
		       update lifecycle; live provider update and delete behavior remain unverified. Continue
		       expanding filter behavior in its dedicated guide only after the lab compares native C# and
		       DataService runtime shapes.
		       """
	};

	/// <summary>
	/// Returns canonical guidance for the complete Creatio virtual entity lifecycle.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "virtual-entities-guidance")]
	[Description("Returns canonical guidance for creating and verifying a virtual entity object, implementing its IEntityQueryExecutor, and version-gating EntityEventListener writes to Creatio 10.0+ with EnableVirtualEntitySupport enabled.")]
	public ResourceContents GetGuide() => Guide;
}
