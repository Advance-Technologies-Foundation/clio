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
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		tools.Select(tool => tool.Name).Should().Contain(ToolContractGetTool.ToolName);
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns canonical contracts for application-create schema-sync and page-sync")]
	public async Task ToolContractGet_Should_Return_Canonical_Contracts() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ApplicationCreateTool.ApplicationCreateToolName,
					SchemaSyncTool.ToolName,
					PageSyncTool.ToolName
				}
			});

		response.Success.Should().BeTrue();
		response.Tools.Should().NotBeNull();
		response.Tools!.Select(tool => tool.Name).Should().Equal(
			ApplicationCreateTool.ApplicationCreateToolName,
			SchemaSyncTool.ToolName,
			PageSyncTool.ToolName);
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageSyncTool.ToolName,
				PageGetTool.ToolName);
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns canonical required field name for modify-entity-schema-column")]
	public async Task ToolContractGet_Should_Return_Canonical_Required_Field_Name() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
				}
			});

		response.Success.Should().BeTrue();
		ToolContractDefinition contract = response.Tools!.Single();
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "required");
		contract.InputSchema.Properties.Should().NotContain(field => field.Name == "is-required");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().Contain("required");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().NotContain("is-required");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns structured unknown tool suggestions")]
	public async Task ToolContractGet_Should_Return_Structured_Unknown_Tool_Suggestions() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] { "page-updte" }
			});

		response.Success.Should().BeFalse();
		response.Error.Should().NotBeNull();
		response.Error!.Code.Should().Be("tool-not-found");
		response.Error.Suggestions.Should().Contain(PageUpdateTool.ToolName);
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns field-level validation errors for blank tool names")]
	public async Task ToolContractGet_Should_Return_Field_Level_Validation_Error() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] { " " }
			});

		response.Success.Should().BeFalse();
		response.Error.Should().NotBeNull();
		response.Error!.Code.Should().Be("missing-required-parameter");
		response.Error.FieldErrors.Should().ContainSingle();
		response.Error.FieldErrors![0].Field.Should().Be("tool-names[0]");
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
		callResult.IsError.Should().NotBeTrue();
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
