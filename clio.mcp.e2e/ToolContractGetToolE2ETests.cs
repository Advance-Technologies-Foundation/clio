using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature(ToolContractGetTool.ToolName)]
[NonParallelizable]
public sealed class ToolContractGetToolE2ETests {
	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get tool is advertised by the MCP server")]
	public async Task ToolContractGet_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolContractGetTool.ToolName,
			because: "the MCP server should advertise tool-contract-get as the bootstrap contract entry point");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns maintenance-oriented canonical flows")]
	public async Task ToolContractGet_Should_Return_Maintenance_Oriented_Canonical_Contracts() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					PageUpdateTool.ToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the requested maintenance-oriented tools are all registered by the MCP server");
		response.Tools.Should().NotBeNull(
			because: "a successful lookup should return the requested contract payload");
		response.Tools!.Select(tool => tool.Name).Should().Equal(
			new[] {
				ApplicationGetListTool.ApplicationGetListToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
			},
			because: "the response should preserve the requested tool order");
		response.Tools.Single(tool => tool.Name == ApplicationGetListTool.ApplicationGetListToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "application discovery should flow into application inspection for existing-app edits");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "single-page edits should read before write and read back after saving when verification is needed");
		response.Tools.Single(tool => tool.Name == ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
				},
				because: "single-column schema edits should inspect current metadata first and verify it again after saving");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns canonical required field name for modify-entity-schema-column")]
	public async Task ToolContractGet_Should_Return_Canonical_Required_Field_Name() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the modify-entity-schema-column contract should be readable through the MCP server");
		ToolContractDefinition contract = response.Tools!.Single();
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "required",
			because: "the contract should advertise the canonical required field name");
		contract.InputSchema.Properties.Should().NotContain(field => field.Name == "is-required",
			because: "legacy aliases should not be exposed as canonical request fields");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().Contain("required",
			because: "the examples should use the canonical required field name");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().NotContain("is-required",
			because: "the examples should not teach callers to use the removed legacy alias");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns structured unknown tool suggestions")]
	public async Task ToolContractGet_Should_Return_Structured_Unknown_Tool_Suggestions() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] { "page-updte" }
			});

		// Assert
		response.Success.Should().BeFalse(
			because: "a misspelled tool name should fail contract lookup");
		response.Error.Should().NotBeNull(
			because: "the MCP tool should return a structured error envelope for unknown names");
		response.Error!.Code.Should().Be("tool-not-found",
			because: "unknown tool names should map to the tool-not-found error code");
		response.Error.Suggestions.Should().Contain(PageUpdateTool.ToolName,
			because: "the error should suggest the closest matching registered tool name");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns field-level validation errors for blank tool names")]
	public async Task ToolContractGet_Should_Return_Field_Level_Validation_Error() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] { " " }
			});

		// Assert
		response.Success.Should().BeFalse(
			because: "blank tool names are invalid input");
		response.Error.Should().NotBeNull(
			because: "the MCP tool should return a structured validation error");
		response.Error!.Code.Should().Be("missing-required-parameter",
			because: "blank tool names should be treated as missing required values");
		response.Error.FieldErrors.Should().ContainSingle(
			because: "the validation error should identify the exact offending entry");
		response.Error.FieldErrors![0].Field.Should().Be("tool-names[0]",
			because: "the field path should point to the blank element inside tool-names");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<ToolContractGetResponse> CallAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "tool-contract-get should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
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
