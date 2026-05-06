using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the page business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreatePageBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class PageBusinessRuleToolE2ETests {
	private const string ToolName = CreatePageBusinessRuleTool.BusinessRuleCreateToolName;

	[Test]
	[Description("Advertises polymorphic anyOf runtime schema branches only for page show/hide actions through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool advertises page-only action schema")]
	[AllureDescription("Starts the real clio MCP server, lists tools, and verifies create-page-business-rule exposes only hide-element and show-element action branches.")]
	public async Task BusinessRuleCreate_Should_Advertise_Page_Only_Action_Runtime_Schema() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(tool => tool.Name == ToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement anyOf = inputSchema
			.GetProperty("properties")
			.GetProperty("rule")
			.GetProperty("properties")
			.GetProperty("actions")
			.GetProperty("items")
			.GetProperty("anyOf");
		anyOf.GetArrayLength().Should().Be(2,
			because: "the page tool should advertise only page show/hide action payload branches");
		anyOf.EnumerateArray().Select(GetActionType).Should().BeEquivalentTo(["hide-element", "show-element"],
			because: "entity-only actions should not appear in the page business-rule runtime schema");
	}

	[Test]
	[Description("Binds a show-element page business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds show/hide payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rule with a show-element payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_ShowElement_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-business-rule-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["pageSchemaName"] = "UsrCase_FormPage",
				["rule"] = CreateShowElementRule()
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid show-element payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the page action payload");
	}

	[Test]
	[Description("Creates a page business rule for Contact_FormPage in the Custom package through the real MCP server and Creatio environment.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool creates a Contact page rule")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Reads Contact_FormPage, builds a valid page show/hide rule from its bundle, and verifies that Creatio command execution succeeds.")]
	public async Task BusinessRuleCreate_Should_Create_Contact_Page_Rule_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive create-page-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		PageRuleTarget target = await ResolveContactPageRuleTargetAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName);
		// Intentional destructive fixture: Contact_FormPage in Custom is the canonical sandbox target.
		// There is no business-rule delete action yet, so each run writes a uniquely captioned rule and verifies readback.
		string caption = $"MCP E2E Contact page {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = environmentName,
				["packageName"] = packageName,
				["pageSchemaName"] = target.PageSchemaName,
				["rule"] = CreateContactPageRule(target, caption)
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact page show/hide rule should return the standard command execution envelope");
		execution.ExitCode.Should().Be(0,
			because: "the Contact page rule should be created in the configured Creatio sandbox");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful command-path MCP calls should include at least one info log message");
		execution.Output.Should().Contain(message => ContainsText(message.Value, "Rule name:"),
			because: "successful business-rule creation should report the generated rule name");
		await BusinessRuleAddonReadback.AssertPageRuleExistsAsync(
			settings,
			environmentName,
			packageName,
			target.RootSchemaUId,
			caption,
			"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionHideElement",
			[target.ElementName],
			target.AttributeName,
			arrangeContext.CancellationTokenSource.Token);
	}

	private static string? GetActionType(JsonElement branch) =>
		branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString();

	private static bool ContainsText(string? value, string expectedText) =>
		value != null && value.Contains(expectedText, StringComparison.OrdinalIgnoreCase);

	private static string ResolvePackageName(McpE2ESettings settings) =>
		string.IsNullOrWhiteSpace(settings.Sandbox.PackageName)
			? "Custom"
			: settings.Sandbox.PackageName;

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("create-page-business-rule MCP E2E requires McpE2E:Sandbox:EnvironmentName for destructive tests.");
			return string.Empty;
		}

		if (await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		Assert.Ignore(
			$"create-page-business-rule MCP E2E requires a reachable configured sandbox environment. Environment '{configuredEnvironmentName}' was not reachable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private static async Task<PageRuleTarget> ResolveContactPageRuleTargetAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		const string contactPageSchemaName = "Contact_FormPage";
		CallToolResult callResult = await session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = contactPageSchemaName,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		PageGetResponse response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
		response.Success.Should().BeTrue(
			because: "the sandbox must expose Contact_FormPage so the destructive page business-rule test can build a valid rule from its bundle");
		response.Files.Should().NotBeNull(
			because: "get-page should materialize the Contact page bundle for rule target discovery");
		File.Exists(response.Files.BundleFile).Should().BeTrue(
			because: "the Contact page bundle file should exist after get-page succeeds");

		JsonObject bundle = JsonNode.Parse(await File.ReadAllTextAsync(response.Files.BundleFile, cancellationToken))!.AsObject();
		string attributeName = ResolveContactAttributeName(bundle);
		string elementName = ResolvePageElementName(bundle, attributeName);
		return new PageRuleTarget(contactPageSchemaName, response.Page.RootSchemaUId, attributeName, elementName);
	}

	private static string ResolveContactAttributeName(JsonObject bundle) {
		JsonObject attributes = bundle["viewModelConfig"]?["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle["modelConfig"]?["dataSources"] as JsonObject ?? [];
		List<PageAttributeCandidate> candidates = [];
		foreach ((string attributeName, JsonNode? attributeNode) in attributes) {
			if (attributeNode is not JsonObject attribute
				|| !TryResolveDatasourcePath(attribute, out string datasourceName, out string columnName)
				|| !IsContactDatasource(dataSources, datasourceName)) {
				continue;
			}
			candidates.Add(new PageAttributeCandidate(attributeName, columnName));
		}

		PageAttributeCandidate? preferred = candidates.FirstOrDefault(candidate =>
			string.Equals(candidate.ColumnName, "Name", StringComparison.OrdinalIgnoreCase));
		(preferred ?? candidates.FirstOrDefault()).Should().NotBeNull(
			because: "Contact_FormPage should expose at least one datasource-bound Contact attribute for page business-rule conditions");
		return (preferred ?? candidates.First()).AttributeName;
	}

	private static string ResolvePageElementName(JsonObject bundle, string attributeName) {
		string? boundElement = EnumerateObjects(bundle["viewConfig"])
			.FirstOrDefault(obj => string.Equals(obj["control"]?.GetValue<string>(), $"${attributeName}", StringComparison.Ordinal))
			?["name"]?.GetValue<string>();
		if (!string.IsNullOrWhiteSpace(boundElement)) {
			return boundElement;
		}

		string? firstNamedElement = EnumerateObjects(bundle["viewConfig"])
			.Select(obj => obj["name"]?.GetValue<string>())
			.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
		firstNamedElement.Should().NotBeNullOrWhiteSpace(
			because: "Contact_FormPage viewConfig should expose at least one named element for show/hide actions");
		return firstNamedElement!;
	}

	private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node) {
		Stack<JsonNode?> pending = new();
		pending.Push(node);
		while (pending.Count > 0) {
			JsonNode? current = pending.Pop();
			switch (current) {
				case JsonObject obj:
					yield return obj;
					foreach (KeyValuePair<string, JsonNode?> property in obj) {
						pending.Push(property.Value);
					}
					break;
				case JsonArray array:
					foreach (JsonNode? item in array) {
						pending.Push(item);
					}
					break;
			}
		}
	}

	private static bool TryResolveDatasourcePath(
		JsonObject attribute,
		out string datasourceName,
		out string columnName) {
		datasourceName = string.Empty;
		columnName = string.Empty;
		string path = attribute["modelConfig"]?["path"]?.GetValue<string>() ?? string.Empty;
		string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length != 2) {
			return false;
		}
		datasourceName = parts[0];
		columnName = parts[1];
		return true;
	}

	private static bool IsContactDatasource(JsonObject dataSources, string datasourceName) =>
		string.Equals(
			dataSources[datasourceName]?["config"]?["entitySchemaName"]?.GetValue<string>(),
			"Contact",
			StringComparison.Ordinal);

	private static IReadOnlyDictionary<string, object?> CreateContactPageRule(PageRuleTarget target, string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression(target.AttributeName),
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "hide-element",
					["items"] = new object[] { target.ElementName }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateShowElementRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Show escalation for high priority",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS_Priority"),
						["comparisonType"] = "equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["value"] = "High"
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "show-element",
					["items"] = new object[] { "EscalateButton" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateAttributeExpression(string path) =>
		new Dictionary<string, object?> {
			["type"] = "AttributeValue",
			["path"] = path
		};

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record PageAttributeCandidate(string AttributeName, string ColumnName);

	private sealed record PageRuleTarget(string PageSchemaName, string RootSchemaUId, string AttributeName, string ElementName);
}
