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
		columnProperties.SchemaName.Should().Be(context.EntitySchemaName,
			because: "the added lookup column should be readable from the updated entity schema");
		columnProperties.ColumnName.Should().Be(context.LookupColumnName,
			because: "the updated entity should expose the lookup column that schema-sync added");
		columnProperties.ReferenceSchemaName.Should().Be(context.LookupSchemaName,
			because: "the added column should reference the lookup created in the same schema-sync batch");
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
							["title"] = "Schema Sync Entity",
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = "UsrTitle",
									["type"] = "Text",
									["title"] = "Title"
								}
							}
						},
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							["schema-name"] = lookupSchemaName,
							["title"] = "Schema Sync Lookup",
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
									["title"] = "Status",
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
			string.Equals(result.GetProperty("operation").GetString(), operation, StringComparison.Ordinal) &&
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
