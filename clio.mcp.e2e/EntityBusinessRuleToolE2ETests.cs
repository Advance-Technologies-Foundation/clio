using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the entity business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateEntityBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class EntityBusinessRuleToolE2ETests {
	private const string ToolName = CreateEntityBusinessRuleTool.BusinessRuleCreateToolName;

	[Test]
	[Description("Advertises polymorphic anyOf runtime schema branches for business-rule actions through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool advertises polymorphic action schema")]
	[AllureDescription("Starts the real clio MCP server, lists tools, and verifies create-entity-business-rule exposes anyOf action branches for field-state actions and Set values assignments.")]
	public async Task BusinessRuleCreate_Should_Advertise_Polymorphic_Action_AnyOf_Runtime_Schema() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(tool => tool.Name == ToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement actionSchema = inputSchema
			.GetProperty("properties")
			.GetProperty("rule")
			.GetProperty("properties")
			.GetProperty("actions")
			.GetProperty("items");
		JsonElement anyOf = actionSchema.GetProperty("anyOf");
		anyOf.GetArrayLength().Should().Be(5,
			because: "the real MCP tools/list schema should describe each supported business-rule action subtype");
		anyOf.EnumerateArray().Should().Contain(branch =>
				branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "make-required"
				&& branch.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("type").GetString() == "string",
			because: "field-state action branches should use string-array action items");
		JsonElement setValuesItemsSchema = anyOf.EnumerateArray()
			.Single(branch => branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "set-values")
			.GetProperty("properties")
			.GetProperty("items")
			.GetProperty("items");
		setValuesItemsSchema.GetProperty("properties").EnumerateObject()
			.Select(property => property.Name).Should().Contain(["expression", "value"],
				because: "Set values action items should advertise target and value expression objects");
	}

	[Test]
	[Description("Binds a Set values business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds Set values payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with a Set values constant payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SetValues_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-business-rule-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["entitySchemaName"] = "UsrOrder",
				["rule"] = CreateSetValuesRule()
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid Set values payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the Set values payload");
	}

	private static bool ContainsText(string? value, string expectedText) =>
		value != null && value.Contains(expectedText, StringComparison.OrdinalIgnoreCase);

	private static IReadOnlyDictionary<string, object?> CreateSetValuesRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Populate defaults",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("Name"),
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "set-values",
					["items"] = new object[] {
						CreateSetValuesItem("UsrTextResult", "Ready"),
						CreateSetValuesItem("UsrScore", 42),
						CreateSetValuesItem("UsrCompleted", true),
						CreateSetValuesItem("UsrPlannedOn", "2025-01-01T00:00:00Z"),
						CreateFormulaSetValuesItem("UsrTotalScore", "UsrScore + UsrBonusScore")
					}
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateSetValuesItem(string path, object value) =>
		new Dictionary<string, object?> {
			["expression"] = CreateAttributeExpression(path),
			["value"] = new Dictionary<string, object?> {
				["type"] = "Const",
				["value"] = value
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateFormulaSetValuesItem(string path, string formula) =>
		new Dictionary<string, object?> {
			["expression"] = CreateAttributeExpression(path),
			["value"] = new Dictionary<string, object?> {
				["type"] = "Formula",
				["expression"] = formula
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
}
