using System;
using System.Collections.Generic;
using System.Linq;
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
[AllureFeature(GuidanceGetTool.ToolName)]
[NonParallelizable]
public sealed class GuidanceGetToolE2ETests {
	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance tool is advertised by the MCP server")]
	public async Task GuidanceGet_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(GuidanceGetTool.ToolName,
			because: "the MCP server should advertise get-guidance as the tool-native way to read canonical guidance");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical handler guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Handlers_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-handlers"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-handlers is a registered guidance name");
		response.Guidance.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Guidance!.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Guidance.Text.Should().Contain("clio MCP page-schema handlers guide",
			because: "the guidance tool should return the canonical handler guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical validator guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Validators_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-validators"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-validators is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema validators guide",
			because: "the guidance tool should return the canonical validator guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical sdk common guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Sdk_Common_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-sdk-common"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-sdk-common is a registered guidance name");
		response.Guidance.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Guidance!.Uri.Should().Be("docs://mcp/guides/page-schema-sdk-common",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Guidance.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guidance tool should return the canonical sdk common guide text");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<GuidanceGetResponse> CallAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
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
