using Allure.Net.Commons;
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

/// <summary>
/// End-to-end tests for the list-packages MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("list-packages")]
[NonParallelizable]
public sealed class GetPkgListToolE2ETests {
	private const string ToolName = GetPkgListTool.GetPkgListToolName;

	[Test]
	[Description("Starts the real clio MCP server, verifies the default package bound, then requests two explicit pages and verifies paging metadata and continuity.")]
	[AllureTag(ToolName)]
	[AllureName("Get package list returns bounded structured pages")]
	[AllureDescription("Uses the real clio MCP server to verify omitted paging defaults to 50, then calls list-packages with limit 1 and offsets 0 and 1 to prove total, truncated, and page metadata make the full package set enumerable.")]
	public async Task GetPkgList_ShouldReturnBoundedPages_WhenLimitAndOffsetAreSupplied() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to install cliogate and run list-packages end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using GetPkgListArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		GetPkgListActResult actResult = await ActAsync(arrangeContext, settings.Sandbox.EnvironmentName!);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertStructuredPagesReturned(actResult);
	}

	private static async Task<GetPkgListArrangeContext> ArrangeAsync(McpE2ESettings settings) {
		return await AllureApi.Step("Arrange list-packages MCP session", async () => {
			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
			await ClioCliCommandRunner.EnsureCliogateInstalledAsync(
				settings,
				settings.Sandbox.EnvironmentName!,
				cancellationTokenSource.Token);
			McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			return new GetPkgListArrangeContext(session, cancellationTokenSource);
		});
	}

	private static async Task<GetPkgListActResult> ActAsync(GetPkgListArrangeContext arrangeContext, string environmentName) {
		return await AllureApi.Step("Act by invoking list-packages through MCP", async () => {
			IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
			tools.Select(tool => tool.Name).Should().Contain(ToolName,
				because: "the list-packages MCP tool must be advertised before the end-to-end call can be executed");

			CallToolResult defaultCallResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			CallToolResult firstCallResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["limit"] = 1,
						["offset"] = 0
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			CallToolResult secondCallResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["limit"] = 1,
						["offset"] = 1
					}
				},
				arrangeContext.CancellationTokenSource.Token);

			GetPkgListResponseEnvelope defaultPage = GetPkgListResultParser.ExtractResponse(defaultCallResult);
			GetPkgListResponseEnvelope firstPage = GetPkgListResultParser.ExtractResponse(firstCallResult);
			GetPkgListResponseEnvelope secondPage = GetPkgListResultParser.ExtractResponse(secondCallResult);
			CallToolResult terminalCallResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["limit"] = 1,
						["offset"] = firstPage.Total
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			GetPkgListResponseEnvelope terminalPage = GetPkgListResultParser.ExtractResponse(terminalCallResult);
			return new GetPkgListActResult(defaultCallResult, firstCallResult, secondCallResult, terminalCallResult,
				defaultPage, firstPage, secondPage, terminalPage);
		});
	}

	[AllureStep("Assert MCP tool result is successful")]
	private static void AssertToolCallSucceeded(GetPkgListActResult actResult) {
		actResult.DefaultCallResult.IsError.Should().NotBeTrue(
			because: "list-packages should apply safe paging defaults when the caller omits limit and offset");
		actResult.FirstCallResult.IsError.Should().NotBeTrue(
			because: "the first list-packages page should be returned for a valid sandbox environment");
		actResult.SecondCallResult.IsError.Should().NotBeTrue(
			because: "the second list-packages page should be returned for a valid sandbox environment");
		actResult.TerminalCallResult.IsError.Should().NotBeTrue(
			because: "an offset at the filtered total should return a valid empty terminal page");
	}

	[AllureStep("Assert bounded package pages and completeness metadata")]
	private static void AssertStructuredPagesReturned(GetPkgListActResult actResult) {
		actResult.DefaultPage.Limit.Should().Be(GetPkgListTool.DefaultLimit,
			because: "the real MCP binder must preserve the tool's default payload bound when limit is omitted");
		actResult.DefaultPage.Offset.Should().Be(0,
			because: "the real MCP binder must start at the first package when offset is omitted");
		actResult.DefaultPage.Count.Should().BeLessThanOrEqualTo(GetPkgListTool.DefaultLimit,
			because: "the default MCP response must never exceed its advertised package bound");
		actResult.DefaultPage.Total.Should().BeGreaterThanOrEqualTo(actResult.DefaultPage.Count,
			because: "the full package count cannot be smaller than the returned default page");
		actResult.DefaultPage.Truncated.Should().Be(actResult.DefaultPage.Count < actResult.DefaultPage.Total,
			because: "the completeness flag must reflect whether the configured sandbox has packages beyond the default page");
		actResult.FirstPage.Packages.Should().ContainSingle(
			because: "limit 1 should bound the first response to one package");
		actResult.SecondPage.Packages.Should().ContainSingle(
			because: "the sandbox should contain enough packages to retrieve a second one-package page");
		actResult.FirstPage.Packages.Should().Contain(package =>
				!string.IsNullOrWhiteSpace(package.Name)
				&& !string.IsNullOrWhiteSpace(package.Version)
				&& package.Maintainer != null
				&& !string.IsNullOrWhiteSpace(package.UId),
			because: "the MCP tool should return at least one structured package record with usable fields for agents and assertions");
		actResult.FirstPage.Total.Should().BeGreaterThan(1,
			because: "the configured sandbox must expose more than one package to prove offset paging at the real MCP boundary");
		actResult.SecondPage.Total.Should().Be(actResult.FirstPage.Total,
			because: "total must describe the same full matching set independently of page offset");
		actResult.FirstPage.Count.Should().Be(1, because: "count must describe the first returned page");
		actResult.SecondPage.Count.Should().Be(1, because: "count must describe the second returned page");
		actResult.FirstPage.Offset.Should().Be(0, because: "the first page should start at offset zero");
		actResult.SecondPage.Offset.Should().Be(1, because: "the second page should apply the requested offset");
		actResult.FirstPage.Limit.Should().Be(1, because: "the first response should expose the applied limit");
		actResult.SecondPage.Limit.Should().Be(1, because: "the second response should expose the applied limit");
		actResult.FirstPage.Truncated.Should().BeTrue(
			because: "the first bounded page cannot be complete when the sandbox contains more than one package");
		actResult.FirstPage.Packages[0].UId.Should().NotBe(actResult.SecondPage.Packages[0].UId,
			because: "advancing the offset must return the next package rather than repeating the first page");
		actResult.TerminalPage.Packages.Should().BeEmpty(
			because: "an offset at the filtered total should produce a valid empty page rather than a parse failure");
		actResult.TerminalPage.Count.Should().Be(0,
			because: "count must describe the empty terminal page");
		actResult.TerminalPage.Total.Should().Be(actResult.FirstPage.Total,
			because: "the terminal page must preserve the full matching total");
		actResult.TerminalPage.Offset.Should().Be(actResult.FirstPage.Total,
			because: "the terminal response must echo the requested end offset");
		actResult.TerminalPage.Limit.Should().Be(1,
			because: "the terminal response must echo the requested page size even when no items remain");
		actResult.TerminalPage.Truncated.Should().BeFalse(
			because: "no matching packages remain after an offset at the filtered total");
	}

	private sealed record GetPkgListArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record GetPkgListActResult(
		CallToolResult DefaultCallResult,
		CallToolResult FirstCallResult,
		CallToolResult SecondCallResult,
		CallToolResult TerminalCallResult,
		GetPkgListResponseEnvelope DefaultPage,
		GetPkgListResponseEnvelope FirstPage,
		GetPkgListResponseEnvelope SecondPage,
		GetPkgListResponseEnvelope TerminalPage);
}
