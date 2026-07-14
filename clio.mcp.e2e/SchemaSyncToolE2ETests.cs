using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the sync-schemas composite MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("sync-schemas")]
[NonParallelizable]
public sealed class SchemaSyncToolE2ETests : McpContractFixtureBase {

	private const string ToolName = SchemaSyncTool.ToolName;
	private const string AddPackageDependencyToolName = AddPackageDependencyTool.AddPackageDependencyToolName;
	private const string ReadSchemaToolName = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName;
	private const string ReadColumnToolName = GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName;
	private const string CurrentDateTimeSystemValueUId = "d7c295d3-3146-4ee1-ac49-3a7bd0edc45d";

	// ENG-92459: one shared workspace+package+push for the whole fixture instead of one push-workspace
	// round-trip per environment-bound test. Lazily initialized by the first requireEnvironment arrange so
	// its Assert.Ignore on a missing destructive opt-in / unreachable environment / cliogate only skips the
	// environment-bound tests; the non-environment tests need no package and stay green. The fixture is
	// [NonParallelizable], so the lazy init runs without a race; each environment-bound test still creates
	// its own unique schemas (entity + lookup) inside the shared package, preserving per-test isolation.
	private string? _sharedRootDirectory;
	private string? _sharedWorkspacePath;
	private string? _sharedEnvironmentName;
	private string? _sharedPackageName;

	[OneTimeTearDown]
	public void CleanupSharedSandboxPackage() {
		if (_sharedRootDirectory is not null && Directory.Exists(_sharedRootDirectory)) {
			Directory.Delete(_sharedRootDirectory, recursive: true);
		}
	}

	[Test]
	[Description("Exposes sync-schemas via the get-tool-contract compact index so callers can discover and invoke it on the lazy surface.")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas tool is discoverable on the lazy surface")]
	[AllureDescription("Verifies that sync-schemas is discoverable via the get-tool-contract compact index of the MCP server.")]
	public async Task SchemaSyncTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "sync-schemas must be discoverable on the lazy surface (get-tool-contract compact index) so MCP clients can find the composite tool even though it is not resident in tools/list");
	}

	[Test]
	[Description("Returns a binding-layer error when sync-schemas is called without the required args wrapper on the lazy surface.")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas returns invocation error when args wrapper is missing")]
	[AllureDescription("Starts the real MCP server, invokes sync-schemas without the args wrapper, and verifies that binding fails before sync-schemas can produce a structured SchemaSyncResponse payload. On the lazy surface the call is dispatched via clio-run, so the SDK binding failure of the target's args record is wrapped in the executor's \"Error: tool 'sync-schemas' failed:\" text.")]
	public async Task SchemaSyncTool_Should_Return_Invocation_Error_When_Args_Wrapper_Is_Missing() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		// sync-schemas is hidden from tools/list, so the session routes this call through clio-run with the
		// `args` payload omitted. The SDK still fails binding the target's args record INSIDE the dispatch;
		// the executor wraps that failure ("Error: tool 'sync-schemas' failed: ..."). A native resident call
		// would have surfaced "An error occurred invoking 'sync-schemas'." — both shapes are the same
		// binding-layer contract (IsError=true, no structured payload), so either diagnostic is accepted.
		AssertInvocationFailure(
			callResult,
			because: "missing args should fail during MCP binding before sync-schemas can produce a structured tool response");
	}

	[Test]
	[Description("Returns a binding-layer error when sync-schemas args has the wrong type on the lazy surface.")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas returns invocation error when args has invalid type")]
	[AllureDescription("Starts the real MCP server, invokes sync-schemas with args set to a string instead of an object, and verifies that the binding layer rejects the payload before sync-schemas can produce a structured SchemaSyncResponse payload. On the lazy surface the call is dispatched via clio-run, whose args-shape validation rejects the non-object payload.")]
	public async Task SchemaSyncTool_Should_Return_Invocation_Error_When_Args_Has_Invalid_Type() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = "invalid"
			},
			context.CancellationTokenSource.Token);

		// Assert
		// sync-schemas is hidden from tools/list, so a present-but-wrong-type `args` no longer reaches the
		// SDK's per-tool argument deserializer ("Failed to deserialize argument 'args' for MCP tool
		// 'sync-schemas'"). The session routes the call through clio-run, which validates the args shape
		// itself and rejects the non-object payload ("'args' for tool 'sync-schemas' must be a JSON object
		// ..."). Either shape is the same binding-layer contract: the failure happens before the target tool
		// executes, IsError=true, and no structured SchemaSyncResponse payload is produced.
		AssertInvocationFailure(
			callResult,
			because: "wrong-type args should fail at the binding layer before sync-schemas can produce a structured tool response");
	}

	[Test]
	[Description("Executes sync-schemas on a real sandbox environment and keeps each result message list aligned with its own operation.")]
	[AllureTag(ToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureName("sync-schemas keeps operation messages aligned on real environment")]
	[AllureDescription("Creates a temporary package in a reachable sandbox environment, runs sync-schemas with create-entity, create-lookup with seed rows, and update-entity, then verifies both the remote side effects and that each result message list contains only its own operation evidence.")]
	public async Task SchemaSyncTool_Should_Keep_Messages_On_The_Correct_Operation() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: true);

		// Act
		CallToolResult callResult = await CallSchemaSyncAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			context.LookupSchemaName!,
			context.LookupColumnName,
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "sync-schemas should return a structured success payload for a valid sandbox package");
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		JsonElement[] results = response.GetProperty("results").EnumerateArray().ToArray();
		string responsePayload = FormatPayload(response);
		response.GetProperty("success").GetBoolean().Should().BeTrue(
			because: $"the composite batch should succeed on the reachable sandbox environment. Payload: {responsePayload}");
		results.Should().HaveCount(4,
			because: $"create-entity, create-lookup, seed-data, and update-entity should each produce one result. Payload: {responsePayload}");
		JsonElement createLookupResult = FindResult(results, "create-lookup", context.LookupSchemaName!);
		JsonElement seedResult = FindResult(results, "seed-data", context.LookupSchemaName!);
		JsonElement updateResult = FindResult(results, "update-entity", context.EntitySchemaName!);
		string[] createLookupMessages = GetMessageValues(createLookupResult);
		string[] seedMessages = GetMessageValues(seedResult);
		string[] updateMessages = GetMessageValues(updateResult);
		EntitySchemaPropertiesInfo lookupProperties = await GetSchemaPropertiesAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.LookupSchemaName!,
			context.CancellationTokenSource.Token);
		LookupRegistrationSnapshot registrationSnapshot = LookupRegistrationProbe.Read(
			context.EnvironmentName!,
			context.PackageName!,
			context.LookupSchemaName!);
		EntitySchemaColumnPropertiesInfo columnProperties = await GetColumnPropertiesAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			context.LookupColumnName,
			context.CancellationTokenSource.Token);

		// Assert
		results.Select(result => result.GetProperty("type").GetString()).Should().OnlyContain(type =>
				!string.IsNullOrWhiteSpace(type),
			because: "sync-schemas should expose the canonical type field on every result");
		createLookupMessages.Should().Contain(message => message.Contains(context.LookupSchemaName!, StringComparison.Ordinal),
			because: "create-lookup should keep its schema creation message on its own result");
		createLookupMessages.Should().NotContain(message => message.Contains("Created row:", StringComparison.Ordinal),
			because: "seed-data messages must not leak into the create-lookup result");
		createLookupMessages.Should().NotContain(message => message.Contains(context.LookupColumnName, StringComparison.Ordinal),
			because: "update-entity messages must not leak into the create-lookup result");
		seedMessages.Should().Contain(message => message.Contains("Created row:", StringComparison.Ordinal),
			because: "seed-data should keep the inserted row messages on its own result");
		seedMessages.Should().Contain(message => message.Contains("Name=New", StringComparison.Ordinal),
			because: "the seed-data result should surface the seeded lookup row names");
		seedMessages.Should().NotContain(message => message.Contains("Entity schema", StringComparison.Ordinal),
			because: "schema creation messages must not leak into the seed-data result");
		seedMessages.Should().NotContain(message => message.Contains(context.LookupColumnName, StringComparison.Ordinal),
			because: "update-entity messages must not leak into the seed-data result");
		updateMessages.Should().Contain(message => message.Contains(context.EntitySchemaName!, StringComparison.Ordinal),
			because: "update-entity should keep its column mutation message on its own result");
		updateMessages.Should().Contain(message => message.Contains(context.LookupColumnName, StringComparison.Ordinal),
			because: "update-entity should report the added lookup column");
		updateMessages.Should().NotContain(message => message.Contains("Created row:", StringComparison.Ordinal),
			because: "seed-data messages must not leak into the update-entity result");
		updateMessages.Should().NotContain(message => message.Contains("Entity schema", StringComparison.Ordinal),
			because: "schema creation messages must not leak into the update-entity result");
		lookupProperties.ParentSchemaName.Should().Be("BaseLookup",
			because: "sync-schemas should create the lookup with BaseLookup inheritance");
		registrationSnapshot.LookupRowCount.Should().Be(1,
			because: "sync-schemas should register the created lookup exactly once in the Lookup entity");
		registrationSnapshot.LookupRowTitle.Should().Be("Schema Sync Lookup",
			because: "sync-schemas should reuse the create-lookup title for the Lookup registration caption");
		registrationSnapshot.BindingCount.Should().Be(1,
			because: "sync-schemas should create exactly one canonical package schema data binding for the lookup");
		registrationSnapshot.BindingEntitySchemaName.Should().Be("Lookup",
			because: "the lookup registration binding should target the Lookup entity");
		registrationSnapshot.BoundRecordIds.Should().Equal([registrationSnapshot.LookupRowId!],
			because: "the canonical lookup binding should point only to the created registration row");
		columnProperties.SchemaName.Should().Be(context.EntitySchemaName,
			because: "the added lookup column should be readable from the updated entity schema");
		columnProperties.ColumnName.Should().Be(context.LookupColumnName,
			because: "the updated entity should expose the lookup column that sync-schemas added");
		columnProperties.ReferenceSchemaName.Should().Be(context.LookupSchemaName,
			because: "the added column should reference the lookup created in the same sync-schemas batch");
	}

	[Test]
	[Description("Runs sync-schemas on a real sandbox environment with an IProgress sink and verifies it streams per-operation stage markers (e.g. '1/2: create-entity ...'), so MCP clients see semantic progress instead of one silent await (ENG-93087).")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas streams per-operation stage markers")]
	[AllureDescription("Runs sync-schemas with two operations through the real clio MCP server with an IProgress sink and asserts the client observed a per-operation stage marker naming the operation index and type — proving the tool-level progress path is wired end to end (ENG-93087).")]
	public async Task SchemaSyncTool_Should_Stream_Per_Operation_Progress_Markers() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: true);
		MessageCollectingProgress progress = new();

		// Act — a two-operation batch; each operation must push a stage marker before it runs.
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName!,
					["package-name"] = context.PackageName!,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = context.EntitySchemaName!,
							["title-localizations"] = BuildLocalizations("Schema Sync Entity"),
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = "UsrTitle",
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Title")
								}
							}
						},
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							["schema-name"] = context.LookupSchemaName!,
							["title-localizations"] = BuildLocalizations("Schema Sync Lookup")
						}
					}
				}
			},
			progress,
			context.CancellationTokenSource.Token);

		// Diagnostic: surface the exact progress stream the client received (markers + heartbeats).
		foreach (string progressMessage in progress.Messages) {
			TestContext.Out.WriteLine($"[progress] {progressMessage}");
		}

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "sync-schemas should return a structured payload on the reachable sandbox environment");
		progress.Messages.Should().Contain(
			message => message.Contains("1/", StringComparison.Ordinal) && message.Contains("create-entity", StringComparison.Ordinal),
			because: "sync-schemas must stream a per-operation stage marker naming the operation index and type so the client can show which operation is running");
		progress.Messages.Should().Contain(
			message => message.Contains("2/", StringComparison.Ordinal) && message.Contains("create-lookup", StringComparison.Ordinal),
			because: "sync-schemas must also stream a marker for the second operation naming its index and type");
		List<string> orderedMessages = progress.Messages.ToList();
		int firstOperationMarkerIndex = orderedMessages.FindIndex(message => message.Contains("1/", StringComparison.Ordinal));
		int secondOperationMarkerIndex = orderedMessages.FindIndex(message => message.Contains("2/", StringComparison.Ordinal));
		firstOperationMarkerIndex.Should().BeLessThan(secondOperationMarkerIndex,
			because: "the first operation's stage marker must reach the client before the second operation's, matching batch execution order");
	}

	[Test]
	[Description("Creates a virtual entity through sync-schemas and verifies readback plus absence of a PostgreSQL table.")]
	[AllureTag(ToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureName("sync-schemas creates a virtual entity without a physical table")]
	[AllureDescription("Runs a real sync-schemas create-entity operation with is-virtual=true, verifies the schema readback, and checks the disposable PostgreSQL catalog for table absence.")]
	public async Task SchemaSync_CreateEntity_Should_Create_Virtual_Schema_Without_Physical_Table() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.RequirePostgreSqlSandbox(settings);
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: true);
		SandboxEnvironmentContext sandbox = SandboxEnvironmentResolver.Resolve(settings);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["package-name"] = context.PackageName,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = context.EntitySchemaName,
							["title-localizations"] = BuildLocalizations("Virtual schema sync entity"),
							["is-virtual"] = true
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		EntitySchemaPropertiesInfo schemaProperties = await GetSchemaPropertiesAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			context.CancellationTokenSource.Token);
		bool physicalTableExists = PostgresTableProbe.Exists(
			sandbox.DatabaseConnectionString,
			context.EntitySchemaName!);

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeTrue(
			because: $"sync-schemas should create the virtual schema successfully. Payload: {FormatPayload(response)}");
		schemaProperties.Virtual.Should().BeTrue(
			because: "get-entity-schema-properties must read back the virtual flag created through sync-schemas");
		physicalTableExists.Should().BeFalse(
			because: "sync-schemas must not cause Creatio to materialize a PostgreSQL table for a virtual entity");
	}

	[Test]
	[Description("Rejects seed rows for virtual entity creation before environment resolution.")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas rejects seed rows for virtual entities before mutation")]
	[AllureDescription("Starts the real MCP server without a reachable environment and verifies that is-virtual plus seed-rows returns a structured validation failure before any schema is created.")]
	public async Task SchemaSyncTool_Should_Reject_SeedRows_For_Virtual_Entity_Before_Environment_Resolution() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);
		string invalidEnvironmentName = $"missing-sync-schemas-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = "UsrVirtualItem",
							["title-localizations"] = BuildLocalizations("Virtual item"),
							["is-virtual"] = true,
							["seed-rows"] = new object?[] {
								new Dictionary<string, object?> {
									["values"] = new Dictionary<string, object?> { ["Name"] = "Unavailable" }
								}
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		JsonElement result = response.GetProperty("results").EnumerateArray().Single();

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "invalid field combinations should use the normal structured MCP result envelope");
		response.GetProperty("success").GetBoolean().Should().BeFalse(
			because: "virtual entities cannot accept table-backed seed data");
		result.GetProperty("error").GetString().Should().Contain("cannot include seed-rows",
			because: "the caller must receive actionable guidance before any remote mutation");
		result.GetProperty("error").GetString().Should().NotContain(invalidEnvironmentName,
			because: "local validation must happen before environment resolution");
	}

	[Test]
	[Description("Rejects inherited BaseLookup columns in create-lookup operations before environment resolution.")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas rejects inherited BaseLookup columns before environment resolution")]
	[AllureDescription("Starts the real MCP server without requiring a reachable environment, invokes sync-schemas with a create-lookup operation that tries to redefine Name, and verifies the tool returns a structured validation failure.")]
	public async Task SchemaSyncTool_Should_Reject_Inherited_BaseLookup_Columns_Before_Environment_Resolution() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);
		string invalidEnvironmentName = $"missing-sync-schemas-env-{Guid.NewGuid():N}";
		IReadOnlyCollection<string> reachableToolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		reachableToolNames.Should().Contain(ToolName,
			because: "sync-schemas must be discoverable via the get-tool-contract compact index before the validation scenario can be invoked");

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							["schema-name"] = "UsrTodoStatus",
							["title-localizations"] = BuildLocalizations("Todo Status"),
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = "Name",
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Name")
								}
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		JsonElement[] results = response.GetProperty("results").EnumerateArray().ToArray();
		JsonElement createLookupResult = results.Single();
		string error = createLookupResult.GetProperty("error").GetString()!;

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "sync-schemas should return a structured failure payload for inherited-column validation");
		response.GetProperty("success").GetBoolean().Should().BeFalse(
			because: "the batch should fail when create-lookup tries to redefine inherited BaseLookup columns");
		results.Should().HaveCount(1,
			because: "validation should stop the batch on the rejected create-lookup operation");
		createLookupResult.GetProperty("type").GetString().Should().Be("create-lookup",
			because: "the failed result should identify the rejected operation through the canonical type field");
		error.Should().Contain("BaseLookup",
			because: "the failure should explain the inherited-column guardrail");
		error.Should().Contain("Name",
			because: "the failure should identify the rejected inherited column");
		error.Should().NotContain(invalidEnvironmentName,
			because: "validation should happen before environment resolution");
	}

	[Test]
	[Description("Applies structured default-value-config through sync-schemas update-entity and verifies the resulting DateTime column readback.")]
	[AllureTag(ToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureName("sync-schemas applies structured system-value defaults on update-entity")]
	[AllureDescription("Creates a sandbox entity through sync-schemas on a real environment, adds a DateTime column with default-value-config source SystemValue, and verifies the remote side effect plus structured readback metadata.")]
	public async Task SchemaSyncTool_Should_Apply_Structured_DefaultValueConfig_On_UpdateEntity() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: true);
		const string startDateColumnName = "UsrStartDate";

		// Act
		CallToolResult callResult = await CallSchemaSyncWithStructuredDefaultValueAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			startDateColumnName,
			context.CancellationTokenSource.Token);
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		JsonElement[] results = response.GetProperty("results").EnumerateArray().ToArray();
		JsonElement createEntityResult = FindResult(results, "create-entity", context.EntitySchemaName!);
		JsonElement updateResult = FindResult(results, "update-entity", context.EntitySchemaName!);
		string[] createEntityMessages = GetMessageValues(createEntityResult);
		string[] updateMessages = GetMessageValues(updateResult);
		EntitySchemaColumnPropertiesInfo columnProperties = await GetColumnPropertiesAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			startDateColumnName,
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "sync-schemas should return a structured success payload when update-entity applies a valid system-value default");
		response.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the sync-schemas batch should succeed when adding a DateTime column with a structured system-value default");
		results.Should().HaveCount(2,
			because: "the focused batch should produce one result for create-entity and one for update-entity");
		createEntityMessages.Should().Contain(message => message.Contains(context.EntitySchemaName!, StringComparison.Ordinal),
			because: "the create-entity result should keep its own schema creation evidence");
		updateMessages.Should().Contain(message => message.Contains(startDateColumnName, StringComparison.Ordinal),
			because: "the update-entity result should report the DateTime column mutated by the structured default flow");
		columnProperties.ColumnName.Should().Be(startDateColumnName,
			because: "the structured column readback should identify the DateTime column created by sync-schemas");
		columnProperties.Type.Should().Be("DateTime",
			because: "the structured column readback should preserve the DateTime type created by sync-schemas");
		columnProperties.DefaultValueSource.Should().Be("SystemValue",
			because: "legacy summary fields should expose the resolved system-value source for sync-schemas updates");
		columnProperties.DefaultValue.Should().Be(CurrentDateTimeSystemValueUId,
			because: "legacy summary fields should expose the canonical resolved system value guid for sync-schemas updates");
		columnProperties.DefaultValueConfig.Should().NotBeNull(
			because: "structured column readback should expose default-value-config metadata for sync-schemas updates");
		columnProperties.DefaultValueConfig!.Source.Should().Be("SystemValue",
			because: "the structured default value config should preserve the resolved system-value source");
		columnProperties.DefaultValueConfig.ValueSource.Should().Be(CurrentDateTimeSystemValueUId,
			because: "the structured default value config should preserve the canonical system value guid");
		columnProperties.DefaultValueConfig.ResolvedValueSource.Should().Be(CurrentDateTimeSystemValueUId,
			because: "structured default value readback should include the resolved system value guid");
	}

	private async Task<ArrangeContext> ArrangeAsync(bool requireEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (requireEnvironment && !settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive sync-schemas MCP end-to-end tests.");
		}

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(8));
		McpServerSession session = Session;
		const string lookupColumnName = "UsrStatus";

		if (!requireEnvironment) {
			// Non-environment tests only need a live MCP session; they never touch the workspace or a
			// package, so skip the shared sandbox provisioning entirely and use a throwaway root that is
			// owned (and deleted) by the per-test context.
			string throwawayRoot = Path.Combine(Path.GetTempPath(), $"clio-sync-schemas-e2e-{Guid.NewGuid():N}");
			return new ArrangeContext(
				throwawayRoot,
				WorkspacePath: throwawayRoot,
				EnvironmentName: null,
				PackageName: null,
				EntitySchemaName: null,
				LookupSchemaName: null,
				lookupColumnName,
				OwnsRootDirectory: true,
				session,
				cancellationTokenSource);
		}

		(string environmentName, string packageName) =
			await EnsureSharedSandboxPackageAsync(settings, cancellationTokenSource.Token);

		// Each environment-bound test gets its own entity and lookup schemas inside the shared package so
		// concurrent creates never collide; the schema names are unique guids while the column names are
		// constants that live in distinct schemas, so they do not clash either.
		string entitySchemaName = $"Usr{Guid.NewGuid():N}";
		string lookupSchemaName = $"Usr{Guid.NewGuid():N}";
		return new ArrangeContext(
			_sharedRootDirectory!,
			_sharedWorkspacePath!,
			environmentName,
			packageName,
			entitySchemaName,
			lookupSchemaName,
			lookupColumnName,
			OwnsRootDirectory: false,
			session,
			cancellationTokenSource);
	}

	/// <summary>
	/// Lazily provisions a single sandbox workspace + package (created, pushed and unlocked once) that every
	/// environment-bound sync-schemas test shares, replacing the former per-test push-workspace round trip.
	/// Subsequent calls return the cached package. Guarded by Assert.Ignore so only the environment-bound
	/// tests skip when the stand is unavailable; relies on the fixture being [NonParallelizable].
	/// </summary>
	private async Task<(string environmentName, string packageName)> EnsureSharedSandboxPackageAsync(
		McpE2ESettings settings,
		CancellationToken cancellationToken) {
		if (_sharedPackageName is not null) {
			return (_sharedEnvironmentName!, _sharedPackageName);
		}

		string environmentName = await ResolveReachableEnvironmentAsync(settings, cancellationToken);
		try {
			await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName, cancellationToken);
		}
		catch (Exception ex) {
			Assert.Ignore(
				$"Skipping destructive sync-schemas MCP end-to-end test because cliogate could not be installed or verified for '{environmentName}'. {ex.Message}");
		}

		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-sync-schemas-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);

		await CreateEmptyWorkspaceAsync(settings, rootDirectory, workspaceName, cancellationToken);
		await AddPackageAsync(settings, workspacePath, packageName, cancellationToken);
		await PushWorkspaceAsync(settings, workspacePath, environmentName, packageName, cancellationToken);

		_sharedRootDirectory = rootDirectory;
		_sharedWorkspacePath = workspacePath;
		_sharedEnvironmentName = environmentName;
		_sharedPackageName = packageName;
		return (environmentName, packageName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(
		McpE2ESettings settings,
		CancellationToken cancellationToken) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName, cancellationToken)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName, cancellationToken)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"sync-schemas MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName, "--timeout", "30000"],
			cancellationToken: cancellationToken);
		return result.ExitCode == 0;
	}

	private static async Task CreateEmptyWorkspaceAsync(
		McpE2ESettings settings,
		string rootDirectory,
		string workspaceName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			workingDirectory: rootDirectory,
			cancellationToken: cancellationToken);
	}

	private static async Task AddPackageAsync(
		McpE2ESettings settings,
		string workspacePath,
		string packageName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static async Task PushWorkspaceAsync(
		McpE2ESettings settings,
		string workspacePath,
		string environmentName,
		string packageName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["push-workspace", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["pkg-hotfix", packageName, "true", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static async Task<CallToolResult> CallSchemaSyncAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string entitySchemaName,
		string lookupSchemaName,
		string lookupColumnName,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> reachableToolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		reachableToolNames.Should().Contain(ToolName,
			because: "sync-schemas must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

		return await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = entitySchemaName,
							["title-localizations"] = BuildLocalizations("Schema Sync Entity"),
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = "UsrTitle",
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Title")
								}
							}
						},
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							["schema-name"] = lookupSchemaName,
							["title-localizations"] = BuildLocalizations("Schema Sync Lookup"),
							["seed-rows"] = new object?[] {
								new Dictionary<string, object?> {
									["values"] = new Dictionary<string, object?> {
										["Name"] = "New"
									}
								},
								new Dictionary<string, object?> {
									["values"] = new Dictionary<string, object?> {
										["Name"] = "Done"
									}
								}
							}
						},
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = entitySchemaName,
							["update-operations"] = new object?[] {
								new Dictionary<string, object?> {
									["action"] = "add",
									["column-name"] = lookupColumnName,
									["type"] = "Lookup",
									["title-localizations"] = BuildLocalizations("Status"),
									["reference-schema-name"] = lookupSchemaName,
									["required"] = true
								}
							}
						}
					}
				}
			},
			cancellationToken);
	}

	private static async Task<CallToolResult> CallSchemaSyncWithStructuredDefaultValueAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string entitySchemaName,
		string columnName,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> reachableToolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		reachableToolNames.Should().Contain(ToolName,
			because: "sync-schemas must be discoverable via the get-tool-contract compact index before the structured default-value scenario can be executed");

		return await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = entitySchemaName,
							["title-localizations"] = BuildLocalizations("Schema Sync Entity"),
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = "UsrTitle",
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Title")
								}
							}
						},
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = entitySchemaName,
							["update-operations"] = new object?[] {
								new Dictionary<string, object?> {
									["action"] = "add",
									["column-name"] = columnName,
									["type"] = "DateTime",
									["title-localizations"] = BuildLocalizations("Start date"),
									["default-value-config"] = BuildSystemValueDefaultValueConfig("CurrentDateTime")
								}
							}
						}
					}
				}
			},
			cancellationToken);
	}

	private static async Task<EntitySchemaPropertiesInfo> GetSchemaPropertiesAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		CancellationToken cancellationToken) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ReadSchemaToolName,
			because: "the get-entity-schema-properties MCP tool must be advertised before readback verification");

		CallToolResult callResult = await session.CallToolAsync(
			ReadSchemaToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName
				}
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<EntitySchemaPropertiesInfo>(callResult);
	}

	private static async Task<EntitySchemaColumnPropertiesInfo> GetColumnPropertiesAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		string columnName,
		CancellationToken cancellationToken) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ReadColumnToolName,
			because: "the get-entity-schema-column-properties MCP tool must be advertised before column readback verification");

		CallToolResult callResult = await session.CallToolAsync(
			ReadColumnToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName,
					["column-name"] = columnName
				}
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<EntitySchemaColumnPropertiesInfo>(callResult);
	}

	private static JsonElement ExtractSchemaSyncResponse(CallToolResult callResult) {
		if (TryExtractSchemaSyncResponse(callResult.StructuredContent, out JsonElement structuredPayload)) {
			return structuredPayload;
		}

		if (TryExtractSchemaSyncResponse(callResult.Content, out JsonElement contentPayload)) {
			return contentPayload;
		}

		throw new InvalidOperationException("Could not parse SchemaSyncResponse MCP result.");
	}

	private static bool TryExtractSchemaSyncResponse(object? value, out JsonElement payload) {
		if (value is null) {
			payload = default;
			return false;
		}

		JsonElement element = JsonSerializer.SerializeToElement(value);
		if (TryExtractPayloadElement(element, out payload)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload, out JsonElement parsedPayload) &&
					TryExtractPayloadElement(parsedPayload, out payload)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement parsedPayload) &&
				TryExtractPayloadElement(parsedPayload, out payload)) {
				return true;
			}
		}

		payload = default;
		return false;
	}

	private static bool TryExtractPayloadElement(JsonElement element, out JsonElement payload) {
		if (element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty("success", out JsonElement successElement) &&
			successElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
			element.TryGetProperty("results", out JsonElement resultsElement) &&
			resultsElement.ValueKind == JsonValueKind.Array) {
			payload = element;
			return true;
		}

		payload = default;
		return false;
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (element.TryGetProperty("text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String) {
			textPayload = textElement.GetString();
			return true;
		}

		return false;
	}

	private static bool TryParseJson(string value, out JsonElement element) {
		try {
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException) {
			element = default;
			return false;
		}
	}

	private static JsonElement FindResult(IEnumerable<JsonElement> results, string operation, string schemaName) {
		return results.Single(result =>
			string.Equals(result.GetProperty("type").GetString(), operation, StringComparison.Ordinal) &&
			string.Equals(result.GetProperty("schema-name").GetString(), schemaName, StringComparison.Ordinal));
	}

	private static string FormatPayload(JsonElement payload) =>
		JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

	private static string[] GetMessageValues(JsonElement result) {
		if (!result.TryGetProperty("messages", out JsonElement messagesElement) ||
			messagesElement.ValueKind != JsonValueKind.Array) {
			return [];
		}

		return [
			.. messagesElement
				.EnumerateArray()
				.Select(message => message.TryGetProperty("value", out JsonElement valueElement)
					? valueElement.GetString() ?? string.Empty
					: string.Empty)
		];
	}

	private static LogDecoratorType[] GetMessageTypes(JsonElement result) {
		if (!result.TryGetProperty("messages", out JsonElement messagesElement) ||
			messagesElement.ValueKind != JsonValueKind.Array) {
			return [];
		}

		return [
			.. messagesElement
				.EnumerateArray()
				.Select(message => message.TryGetProperty("message-type", out JsonElement typeElement)
					? ParseMessageType(typeElement)
					: LogDecoratorType.None)
		];
	}

	private static LogDecoratorType ParseMessageType(JsonElement typeElement) {
		if (typeElement.ValueKind == JsonValueKind.Number && typeElement.TryGetInt32(out int typeValue) &&
			Enum.IsDefined(typeof(LogDecoratorType), typeValue)) {
			return (LogDecoratorType)typeValue;
		}

		return typeElement.ValueKind == JsonValueKind.String &&
			Enum.TryParse(typeElement.GetString(), ignoreCase: true, out LogDecoratorType messageType)
			? messageType
			: LogDecoratorType.None;
	}

	// Binding-layer failures reach the client through several equivalent surfaces on the lazy tool
	// surface: a native resident call fails with the SDK diagnostics ("An error occurred invoking
	// 'sync-schemas'." / "Failed to deserialize argument 'args' for MCP tool 'sync-schemas'"), while a
	// clio-run-dispatched call can fail at TWO different layers — (a) the executor's own dispatch wraps
	// a target-side failure and names the target ("Error: tool 'sync-schemas' failed: ..." / "'args' for
	// tool 'sync-schemas' must be a JSON object ..."), or (b) a payload that clio-run's OWN `args`
	// parameter (typed `Dictionary<string, JsonElement>`) cannot bind (e.g. a JSON string instead of an
	// object) fails the SDK's per-parameter deserializer for clio-run itself, BEFORE dispatch ever runs —
	// that diagnostic names 'clio-run', not the target tool, because the target was never reached. All
	// four shapes mean the same contract — the failure happened before sync-schemas executed — so the
	// assert accepts either tool name being identified.
	private static void AssertInvocationFailure(CallToolResult callResult, string because) {
		callResult.IsError.Should().BeTrue(
			because: because);
		callResult.StructuredContent.Should().BeNull(
			because: "binding-layer failures should not return a structured sync-schemas payload");
		string diagnostics = string.Join(
			Environment.NewLine,
			(callResult.Content ?? []).Select(content => content.ToString()));
		(diagnostics.Contains(ToolName, StringComparison.Ordinal)
			|| diagnostics.Contains(ClioRunTool.ToolName, StringComparison.Ordinal))
			.Should().BeTrue(
				because: "the binding-layer failure diagnostic should identify either the sync-schemas tool or, when clio-run's own argument binding rejected the payload before dispatch, clio-run itself");
		diagnostics.Should().MatchRegex(
			"(An error occurred invoking|Failed to deserialize argument|failed:|must be a JSON object)",
			because: "the failure should surface a binding-layer diagnostic — either the SDK's native message or the clio-run executor's wrapped equivalent");
	}

	[Test]
	[Description("Rejects flat seed-rows (missing 'values' wrapper) without requiring a reachable environment.")]
	[AllureTag(ToolName)]
	[AllureName("sync-schemas rejects flat seed-rows format before environment resolution")]
	[AllureDescription("Starts the real MCP server without a reachable environment, invokes sync-schemas with create-lookup using flat seed-rows (missing 'values' wrapper), and verifies the tool returns a structured failure on the seed-data step with a clear error about the missing 'values' map.")]
	public async Task SchemaSyncTool_Should_Reject_Flat_SeedRows_Before_Environment_Resolution() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);
		string missingEnv = $"missing-sync-schemas-env-{Guid.NewGuid():N}";
		IReadOnlyCollection<string> reachableToolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		reachableToolNames.Should().Contain(ToolName,
			because: "sync-schemas must be discoverable via the get-tool-contract compact index before the flat seed-rows validation scenario can be invoked");

		// Act - seed-rows use the flat {"Name":"New"} format (missing "values" wrapper)
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = missingEnv,
					["package-name"] = "UsrPkg",
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							["schema-name"] = "UsrTodoStatus",
							["title-localizations"] = BuildLocalizations("Todo Status"),
							["seed-rows"] = new object?[] {
								new Dictionary<string, object?> { ["Name"] = "New" },
								new Dictionary<string, object?> { ["Name"] = "Done" }
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		JsonElement[] results = response.GetProperty("results").EnumerateArray().ToArray();

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "sync-schemas should return a structured failure payload, not an MCP error envelope");
		response.GetProperty("success").GetBoolean().Should().BeFalse(
			because: "flat seed-rows without the 'values' wrapper must cause the batch to fail");
		JsonElement seedResult = results.Single(r =>
			string.Equals(r.GetProperty("type").GetString(), "seed-data", StringComparison.Ordinal));
		seedResult.GetProperty("type").GetString().Should().Be("seed-data",
			because: "failed seed-data results should still expose the canonical type field");
		seedResult.GetProperty("success").GetBoolean().Should().BeFalse(
			because: "the seed-data step must report failure when rows have a null values map");
		string seedError = seedResult.GetProperty("error").GetString()!;
		seedError.Should().Contain("values",
			because: "the error must name the missing 'values' wrapper so the caller can correct the format");
	}

	[Test]
	[Description("Round-trips the get-entity read shape through sync-schemas update-entity: adds via the columns coercion path and the data-value-type alias, modifies via the name alias, and removes via the name alias, then verifies all three on a real environment (ENG-90313).")]
	[AllureTag(ToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureName("sync-schemas round-trips the read shape for add, modify, and remove")]
	[AllureDescription("Creates an entity with two columns, reads the column shape back, then sends that read shape back to sync-schemas update-entity — adding a column through the columns coercion path (using the data-value-type alias), modifying an existing column via the name alias, and removing one via the name alias — and verifies the materialized add/modify/remove via get-entity-schema-properties.")]
	public async Task SchemaSyncTool_Should_RoundTrip_Read_Shape_For_Add_Modify_Remove() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: true);
		string keepColumnName = "UsrKeep";
		string dropColumnName = "UsrDrop";
		string addedColumnName = "UsrAdded";

		// Seed an entity with two columns to later modify and remove.
		CallToolResult seedResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName!,
					["package-name"] = context.PackageName!,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = context.EntitySchemaName!,
							["title-localizations"] = BuildLocalizations("Round Trip Entity"),
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = keepColumnName,
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Keep")
								},
								new Dictionary<string, object?> {
									["name"] = dropColumnName,
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Drop")
								}
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		ExtractSchemaSyncResponse(seedResult).GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the entity and its initial columns must be created before the read-modify-write round trip");

		// Capture the read shape that an agent would echo back.
		EntitySchemaPropertiesInfo beforeReadback = await GetSchemaPropertiesAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			context.CancellationTokenSource.Token);
		beforeReadback.Columns.Should().Contain(column => column.Name == keepColumnName,
			because: "the column to modify must exist before the round trip");

		// Act - send the read shape back verbatim: columns-coercion add (data-value-type alias),
		// name-alias modify, and name-alias remove, all in one sync-schemas call.
		CallToolResult roundTripResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName!,
					["package-name"] = context.PackageName!,
					["operations"] = new object?[] {
						// Add via the columns coercion path using the read shape verbatim: the data-value-type
						// alias for the type, the scalar caption (promoted to title-localizations), and the
						// is-required alias for the required flag.
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = context.EntitySchemaName!,
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = addedColumnName,
									["data-value-type"] = "Integer",
									["caption"] = "Added",
									["is-required"] = true
								}
							}
						},
						// Modify + remove echoing only the read-shape "name" identity field.
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = context.EntitySchemaName!,
							["update-operations"] = new object?[] {
								new Dictionary<string, object?> {
									["action"] = "modify",
									["name"] = keepColumnName,
									["required"] = true
								},
								new Dictionary<string, object?> {
									["action"] = "remove",
									["name"] = dropColumnName
								}
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		JsonElement roundTripResponse = ExtractSchemaSyncResponse(roundTripResult);
		roundTripResponse.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the get-entity read shape must round-trip into update-entity without manual field translation");

		EntitySchemaPropertiesInfo afterReadback = await GetSchemaPropertiesAsync(
			context.Session,
			context.EnvironmentName!,
			context.PackageName!,
			context.EntitySchemaName!,
			context.CancellationTokenSource.Token);
		afterReadback.Columns.Should().Contain(
			column => column.Name == addedColumnName && column.Type == "Integer" && column.Required,
			because: "the columns-coercion add path using the data-value-type alias, scalar caption, and is-required alias must materialize a required new column");
		afterReadback.Columns.Should().Contain(
			column => column.Name == keepColumnName && column.Required,
			because: "the name-alias modify must set the required flag on the existing column");
		afterReadback.Columns.Should().NotContain(
			column => column.Name == dropColumnName,
			because: "the name-alias remove must drop the existing column");
	}

	[Test]
	[Description("Preserves the inherited Id primary column when sync-schemas creates a BaseEntity-derived schema with a custom Guid, and accepts an ordered remove/re-add of that Guid in one update batch.")]
	[AllureTag(ToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureName("sync-schemas preserves inherited primary column and verifies final remove-readd state")]
	[AllureDescription("Creates a BaseEntity-derived schema with text and Guid custom columns on a real sandbox, verifies inherited Id remains primary, then removes and re-adds the Guid in one ordered update-entity batch and verifies both success and final readback.")]
	public async Task SchemaSyncTool_ShouldPreserveInheritedPrimaryColumn_WhenCustomGuidIsRemovedAndReadded() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: true);
		const string nameColumnName = "UsrName";
		const string externalIdColumnName = "UsrExternalRecordId";
		CallToolResult dependencyResult = await context.Session.CallToolAsync(
			AddPackageDependencyToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName!,
					["package-name"] = context.PackageName!,
					["dependencies"] = new object?[] {
						new Dictionary<string, object?> { ["name"] = "Base" }
					}
				}
			},
			context.CancellationTokenSource.Token);
		dependencyResult.IsError.Should().NotBeTrue(
			because: "the sandbox package must depend on Base before it can extend BaseEntity");
		CommandExecutionEnvelope dependencyExecution = McpCommandExecutionParser.Extract(dependencyResult);
		dependencyExecution.ExitCode.Should().Be(0,
			because: "the Base package dependency is required for Creatio to save a BaseEntity-derived schema");

		// Act
		CallToolResult createResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName!,
					["package-name"] = context.PackageName!,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = context.EntitySchemaName!,
							["title-localizations"] = BuildLocalizations("Inherited Primary Entity"),
							["parent-schema-name"] = "BaseEntity",
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = nameColumnName,
									["type"] = "MediumText",
									["title-localizations"] = BuildLocalizations("Name"),
									["required"] = true
								},
								new Dictionary<string, object?> {
									["name"] = externalIdColumnName,
									["type"] = "Guid",
									["title-localizations"] = BuildLocalizations("External record Id")
								}
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		createResult.IsError.Should().NotBeTrue(
			because: "valid derived schema creation should return a structured MCP success response");
		JsonElement createResponse = ExtractSchemaSyncResponse(createResult);
		createResponse.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "a custom Guid must not prevent creation of a BaseEntity-derived schema");
		JsonElement createOperationResult = FindResult(
			createResponse.GetProperty("results").EnumerateArray(), "create-entity", context.EntitySchemaName!);
		GetMessageTypes(createOperationResult).Should().Contain(LogDecoratorType.Info,
			because: "successful create-entity output should carry at least one informational execution message");
		EntitySchemaPropertiesInfo createdSchema = await GetSchemaPropertiesAsync(
			context.Session, context.EnvironmentName!, context.PackageName!, context.EntitySchemaName!,
			context.CancellationTokenSource.Token);

		CallToolResult updateResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName!,
					["package-name"] = context.PackageName!,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = context.EntitySchemaName!,
							["update-operations"] = new object?[] {
								new Dictionary<string, object?> {
									["action"] = "remove",
									["column-name"] = externalIdColumnName
								},
								new Dictionary<string, object?> {
									["action"] = "add",
									["column-name"] = externalIdColumnName,
									["type"] = "Guid",
									["title-localizations"] = BuildLocalizations("External record Id")
								}
							}
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		updateResult.IsError.Should().NotBeTrue(
			because: "the remove/re-add batch should return a structured MCP success response");
		JsonElement updateResponse = ExtractSchemaSyncResponse(updateResult);
		updateResponse.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "post-save verification must evaluate the final ordered batch state");
		JsonElement updateOperationResult = FindResult(
			updateResponse.GetProperty("results").EnumerateArray(), "update-entity", context.EntitySchemaName!);
		GetMessageTypes(updateOperationResult).Should().Contain(LogDecoratorType.Info,
			because: "successful update-entity output should carry at least one informational execution message");
		EntitySchemaPropertiesInfo updatedSchema = await GetSchemaPropertiesAsync(
			context.Session, context.EnvironmentName!, context.PackageName!, context.EntitySchemaName!,
			context.CancellationTokenSource.Token);

		// Assert
		createdSchema.ParentSchemaName.Should().Be("BaseEntity",
			because: "the regression requires a schema that inherits its primary column from BaseEntity");
		createdSchema.PrimaryColumnName.Should().Be("Id",
			because: "a custom Guid on a derived schema must not replace the inherited Id primary column");
		createdSchema.Columns.Should().Contain(column =>
				column.Name == externalIdColumnName && column.Source == "own" && column.Type == "Guid",
			because: "the custom Guid should be persisted as an ordinary own column");

		updatedSchema.PrimaryColumnName.Should().Be("Id",
			because: "removing and re-adding the ordinary custom Guid must not change the inherited primary column");
		updatedSchema.Columns.Should().ContainSingle(column =>
				column.Name == externalIdColumnName && column.Source == "own" && column.Type == "Guid",
			because: "the final schema should contain exactly one re-added custom Guid column");
	}

	private static Dictionary<string, string> BuildLocalizations(string enUs, string? ukUa = null) {
		Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase) {
			["en-US"] = enUs
		};
		if (!string.IsNullOrWhiteSpace(ukUa)) {
			result["uk-UA"] = ukUa;
		}
		return result;
	}

	private static Dictionary<string, object?> BuildSystemValueDefaultValueConfig(string valueSource) {
		return new Dictionary<string, object?> {
			["source"] = "SystemValue",
			["value-source"] = valueSource
		};
	}

	private new sealed record ArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string? EnvironmentName,
		string? PackageName,
		string? EntitySchemaName,
		string? LookupSchemaName,
		string LookupColumnName,
		bool OwnsRootDirectory,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {

		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			// Environment-bound contexts reuse the shared fixture workspace (see EnsureSharedSandboxPackageAsync),
			// which is deleted once in [OneTimeTearDown]; only the per-test throwaway roots are owned here.
			if (OwnsRootDirectory && Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
			return ValueTask.CompletedTask;
		}
	}
}
