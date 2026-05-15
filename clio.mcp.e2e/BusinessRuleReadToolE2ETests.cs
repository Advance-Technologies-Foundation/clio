using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for business-rule read MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("business-rule-read")]
[NonParallelizable]
public sealed class BusinessRuleReadToolE2ETests {
	[Test]
	[Description("Advertises list-business-rules and get-business-rule as read-only, non-destructive MCP tools through the real MCP server.")]
	[AllureTag(BusinessRuleReadTool.BusinessRuleListToolName)]
	[AllureTag(BusinessRuleReadTool.BusinessRuleGetToolName)]
	[AllureName("Business-rule read tools advertise read-only contracts")]
	[AllureDescription("Starts the real clio MCP server, lists tools, and verifies business-rule read tools expose scope parameters and safety annotations.")]
	public async Task BusinessRuleRead_Should_Advertise_ReadOnly_Tools() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);

		// Assert
		AssertReadTool(tools, BusinessRuleReadTool.BusinessRuleListToolName, ["environmentName", "scopeType", "schemaName"]);
		AssertReadTool(tools, BusinessRuleReadTool.BusinessRuleGetToolName, ["environmentName", "scopeType", "schemaName"]);
		JsonElement listInputSchema = JsonSerializer.SerializeToElement(
			tools.Single(tool => tool.Name == BusinessRuleReadTool.BusinessRuleListToolName).ProtocolTool.InputSchema);
		listInputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)
			.Should().NotContain("includeRawMetadata",
				because: "list-business-rules should expose only normalized MCP schema parameters");
		JsonElement getInputSchema = JsonSerializer.SerializeToElement(
			tools.Single(tool => tool.Name == BusinessRuleReadTool.BusinessRuleGetToolName).ProtocolTool.InputSchema);
		getInputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)
			.Should().Contain(["ruleUId", "ruleName", "caption"],
				because: "get-business-rule should advertise all deterministic selector options");
		getInputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)
			.Should().NotContain("includeRawMetadata",
				because: "get-business-rule should not expose raw Creatio metadata parameters");
	}

	[Test]
	[Description("Binds list-business-rules through the real MCP server and reports an invalid environment failure from command resolution.")]
	[AllureTag(BusinessRuleReadTool.BusinessRuleListToolName)]
	[AllureName("list-business-rules binds request payloads")]
	[AllureDescription("Starts the real clio MCP server, calls list-business-rules with an intentionally missing environment, then verifies the request reaches environment-aware command resolution.")]
	public async Task BusinessRuleList_Should_Bind_Request_And_Report_Invalid_Environment() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-business-rule-read-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			BusinessRuleReadTool.BusinessRuleListToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["scopeType"] = "entity",
				["schemaName"] = "Contact"
			},
			cancellationTokenSource.Token);
		BusinessRuleReadEnvelope envelope = ExtractEnvelope(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid list request should bind and return the structured read envelope");
		envelope.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail during command resolution");
		envelope.Error.Should().Contain(invalidEnvironmentName,
			because: "the failure should come from resolving the requested environment, not MCP payload binding");
	}

	[Test]
	[Description("Binds get-business-rule through the real MCP server and reports an invalid environment failure from command resolution.")]
	[AllureTag(BusinessRuleReadTool.BusinessRuleGetToolName)]
	[AllureName("get-business-rule binds selector payloads")]
	[AllureDescription("Starts the real clio MCP server, calls get-business-rule by ruleUId with an intentionally missing environment, then verifies the request reaches environment-aware command resolution.")]
	public async Task BusinessRuleGet_Should_Bind_Selector_And_Report_Invalid_Environment() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-business-rule-get-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			BusinessRuleReadTool.BusinessRuleGetToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["scopeType"] = "page",
				["schemaName"] = "Contact_FormPage",
				["ruleUId"] = "00000000-0000-0000-0000-000000000001"
			},
			cancellationTokenSource.Token);
		BusinessRuleReadEnvelope envelope = ExtractEnvelope(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid get request should bind and return the structured read envelope");
		envelope.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail during command resolution");
		envelope.Error.Should().Contain(invalidEnvironmentName,
			because: "the failure should come from resolving the requested environment, not selector binding");
	}

	private static void AssertReadTool(
		IList<McpClientTool> tools,
		string toolName,
		IReadOnlyCollection<string> requiredParameters) {
		McpClientTool tool = tools.Single(tool => tool.Name == toolName);
		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for client-side safety policies");
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(
			because: $"{toolName} reads business-rule metadata without mutation");
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse(
			because: $"{toolName} must not be advertised as destructive");
		tool.ProtocolTool.Annotations.IdempotentHint.Should().BeTrue(
			because: $"{toolName} can be called repeatedly with the same inputs");
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		inputSchema.GetProperty("required").EnumerateArray()
			.Select(item => item.GetString())
			.Should().Contain(requiredParameters,
				because: $"{toolName} should require environment and target business-rule scope");
	}

	private static BusinessRuleReadEnvelope ExtractEnvelope(CallToolResult callResult) {
		if (TryDeserialize(callResult.StructuredContent, out BusinessRuleReadEnvelope? structuredEnvelope)) {
			return structuredEnvelope!;
		}
		if (TryDeserialize(callResult.Content, out BusinessRuleReadEnvelope? contentEnvelope)) {
			return contentEnvelope!;
		}
		throw new InvalidOperationException("Could not parse business-rule read MCP result.");
	}

	private static bool TryDeserialize(object? value, out BusinessRuleReadEnvelope? envelope) {
		envelope = null;
		if (value is null) {
			return false;
		}

		JsonElement element = JsonSerializer.SerializeToElement(value);
		if (TryDeserializeElement(element, out envelope)) {
			return true;
		}
		if (element.ValueKind != JsonValueKind.Array) {
			return false;
		}

		foreach (JsonElement item in element.EnumerateArray()) {
			if (TryDeserializeElement(item, out envelope)) {
				return true;
			}
			if (item.TryGetProperty("text", out JsonElement textElement)
				&& textElement.ValueKind == JsonValueKind.String
				&& TryParseAndDeserialize(textElement.GetString(), out envelope)) {
				return true;
			}
		}

		return false;
	}

	private static bool TryParseAndDeserialize(string? text, out BusinessRuleReadEnvelope? envelope) {
		envelope = null;
		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(text);
			return TryDeserializeElement(document.RootElement, out envelope);
		} catch (JsonException) {
			return false;
		}
	}

	private static bool TryDeserializeElement(JsonElement element, out BusinessRuleReadEnvelope? envelope) {
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("success", out _)) {
			envelope = null;
			return false;
		}
		try {
			envelope = JsonSerializer.Deserialize<BusinessRuleReadEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return envelope is not null;
		} catch (JsonException) {
			envelope = null;
			return false;
		}
	}

	private sealed record BusinessRuleReadEnvelope(
		bool Success,
		string? Error);
}
