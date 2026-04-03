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
/// End-to-end tests for the schema-sync composite MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("schema-sync")]
[NonParallelizable]
public sealed class SchemaSyncToolE2ETests {

	private const string ToolName = SchemaSyncTool.ToolName;
	private const string ReadSchemaToolName = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName;
	private const string ReadColumnToolName = GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName;

	[Test]
	[Description("Advertises schema-sync MCP tool in the server tool list so callers can discover and invoke it.")]
	[AllureTag(ToolName)]
	[AllureName("schema-sync tool is advertised by the MCP server")]
	[AllureDescription("Verifies that schema-sync appears in the MCP server tool manifest.")]
	public async Task SchemaSyncTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(t => t.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "schema-sync must be advertised so MCP clients can discover the composite tool");
	}

	[Test]
	[Description("Executes schema-sync on a real sandbox environment and keeps each result message list aligned with its own operation.")]
	[AllureTag(ToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureName("schema-sync keeps operation messages aligned on real environment")]
	[AllureDescription("Creates a temporary package in a reachable sandbox environment, runs schema-sync with create-entity, create-lookup with seed rows, and update-entity, then verifies both the remote side effects and that each result message list contains only its own operation evidence.")]
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
		JsonElement response = ExtractSchemaSyncResponse(callResult);
		JsonElement[] results = response.GetProperty("results").EnumerateArray().ToArray();
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
		callResult.IsError.Should().NotBeTrue(
			because: "schema-sync should return a structured success payload for a valid sandbox package");
		response.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the composite batch should succeed on the reachable sandbox environment");
		results.Should().HaveCount(4,
			because: "create-entity, create-lookup, seed-data, and update-entity should each produce one result");
		results.Select(result => result.GetProperty("type").GetString()).Should().OnlyContain(type =>
				!string.IsNullOrWhiteSpace(type),
			because: "schema-sync should expose the canonical type field on every result");
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
			because: "schema-sync should create the lookup with BaseLookup inheritance");
		registrationSnapshot.LookupRowCount.Should().Be(1,
			because: "schema-sync should register the created lookup exactly once in the Lookup entity");
		registrationSnapshot.LookupRowTitle.Should().Be("Schema Sync Lookup",
			because: "schema-sync should reuse the create-lookup title for the Lookup registration caption");
		registrationSnapshot.BindingCount.Should().Be(1,
			because: "schema-sync should create exactly one canonical package schema data binding for the lookup");
		registrationSnapshot.BindingEntitySchemaName.Should().Be("Lookup",
			because: "the lookup registration binding should target the Lookup entity");
		registrationSnapshot.BoundRecordIds.Should().Equal([registrationSnapshot.LookupRowId!],
			because: "the canonical lookup binding should point only to the created registration row");
		columnProperties.SchemaName.Should().Be(context.EntitySchemaName,
			because: "the added lookup column should be readable from the updated entity schema");
		columnProperties.ColumnName.Should().Be(context.LookupColumnName,
			because: "the updated entity should expose the lookup column that schema-sync added");
		columnProperties.ReferenceSchemaName.Should().Be(context.LookupSchemaName,
			because: "the added column should reference the lookup created in the same schema-sync batch");
	}

	[Test]
	[Description("Rejects inherited BaseLookup columns in create-lookup operations before environment resolution.")]
	[AllureTag(ToolName)]
	[AllureName("schema-sync rejects inherited BaseLookup columns before environment resolution")]
	[AllureDescription("Starts the real MCP server without requiring a reachable environment, invokes schema-sync with a create-lookup operation that tries to redefine Name, and verifies the tool returns a structured validation failure.")]
	public async Task SchemaSyncTool_Should_Reject_Inherited_BaseLookup_Columns_Before_Environment_Resolution() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);
		string invalidEnvironmentName = $"missing-schema-sync-env-{Guid.NewGuid():N}";
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "schema-sync must be advertised before the validation scenario can be invoked");

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
			because: "schema-sync should return a structured failure payload for inherited-column validation");
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
	[Description("Applies structured default-value-config through schema-sync update-entity and verifies the resulting DateTime column readback.")]
	[AllureTag(ToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureName("schema-sync applies structured system-value defaults on update-entity")]
	[AllureDescription("Creates a sandbox entity through schema-sync on a real environment, adds a DateTime column with default-value-config source SystemValue, and verifies the remote side effect plus structured readback metadata.")]
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
			because: "schema-sync should return a structured success payload when update-entity applies a valid system-value default");
		response.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the schema-sync batch should succeed when adding a DateTime column with a structured system-value default");
		results.Should().HaveCount(2,
			because: "the focused batch should produce one result for create-entity and one for update-entity");
		createEntityMessages.Should().Contain(message => message.Contains(context.EntitySchemaName!, StringComparison.Ordinal),
			because: "the create-entity result should keep its own schema creation evidence");
		updateMessages.Should().Contain(message => message.Contains(startDateColumnName, StringComparison.Ordinal),
			because: "the update-entity result should report the DateTime column mutated by the structured default flow");
		columnProperties.ColumnName.Should().Be(startDateColumnName,
			because: "the structured column readback should identify the DateTime column created by schema-sync");
		columnProperties.Type.Should().Be("DateTime",
			because: "the structured column readback should preserve the DateTime type created by schema-sync");
		columnProperties.DefaultValueSource.Should().Be("SystemValue",
			because: "legacy summary fields should expose the resolved system-value source for schema-sync updates");
		columnProperties.DefaultValue.Should().Be("CurrentDateTime",
			because: "legacy summary fields should expose the resolved system value name for schema-sync updates");
		columnProperties.DefaultValueConfig.Should().NotBeNull(
			because: "structured column readback should expose default-value-config metadata for schema-sync updates");
		columnProperties.DefaultValueConfig!.Source.Should().Be("SystemValue",
			because: "the structured default value config should preserve the resolved system-value source");
		columnProperties.DefaultValueConfig.ValueSource.Should().Be("CurrentDateTime",
			because: "the structured default value config should preserve the system value name");
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (requireEnvironment && !settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive schema-sync MCP end-to-end tests.");
		}

		string? environmentName = requireEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: null;
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-schema-sync-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(8));
		await CreateEmptyWorkspaceAsync(settings, rootDirectory, workspaceName, cancellationTokenSource.Token);

		string? packageName = null;
		string? entitySchemaName = null;
		string? lookupSchemaName = null;
		string lookupColumnName = "UsrStatus";
		if (requireEnvironment) {
			try {
				await ClioCliCommandRunner.EnsureCliogateInstalledAsync(
					settings,
					environmentName!,
					cancellationTokenSource.Token);
			}
			catch (Exception ex) {
				Assert.Ignore(
					$"Skipping destructive schema-sync MCP end-to-end test because cliogate could not be installed or verified for '{environmentName}'. {ex.Message}");
			}

			packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);
			entitySchemaName = $"Usr{Guid.NewGuid():N}".Substring(0, 22);
			lookupSchemaName = $"Usr{Guid.NewGuid():N}".Substring(0, 22);
			await AddPackageAsync(settings, workspacePath, packageName, cancellationTokenSource.Token);
			await PushWorkspaceAsync(settings, workspacePath, environmentName!, cancellationTokenSource.Token);
		}

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(
			rootDirectory,
			workspacePath,
			environmentName,
			packageName,
			entitySchemaName,
			lookupSchemaName,
			lookupColumnName,
			session,
			cancellationTokenSource);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"schema-sync MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
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
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["push-workspace", "-e", environmentName],
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
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "schema-sync must be advertised before the end-to-end call can be executed");

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
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "schema-sync must be advertised before the structured default-value scenario can be executed");

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

	[Test]
	[Description("Rejects flat seed-rows (missing 'values' wrapper) without requiring a reachable environment.")]
	[AllureTag(ToolName)]
	[AllureName("schema-sync rejects flat seed-rows format before environment resolution")]
	[AllureDescription("Starts the real MCP server without a reachable environment, invokes schema-sync with create-lookup using flat seed-rows (missing 'values' wrapper), and verifies the tool returns a structured failure on the seed-data step with a clear error about the missing 'values' map.")]
	public async Task SchemaSyncTool_Should_Reject_Flat_SeedRows_Before_Environment_Resolution() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);
		string missingEnv = $"missing-schema-sync-env-{Guid.NewGuid():N}";
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "schema-sync must be advertised before the flat seed-rows validation scenario can be invoked");

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
			because: "schema-sync should return a structured failure payload, not an MCP error envelope");
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

	private sealed record ArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string? EnvironmentName,
		string? PackageName,
		string? EntitySchemaName,
		string? LookupSchemaName,
		string LookupColumnName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {

		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}
}
