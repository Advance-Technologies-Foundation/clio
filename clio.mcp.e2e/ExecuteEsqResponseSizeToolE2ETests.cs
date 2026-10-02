using System.Text;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>execute-esq</c> MCP response-size boundary.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqResponseSizeToolE2ETests {

	private const int OversizedDataBytes = 84 * 1024 * 1024;
	private static readonly byte[] DataChunk = Enumerable.Repeat((byte)'A', 64 * 1024).ToArray();
	private static readonly byte[] ResponsePrefix = Encoding.UTF8.GetBytes("{\"rows\":[{\"Data\":\"");
	private static readonly byte[] ResponseSuffix = Encoding.UTF8.GetBytes("\"}],\"success\":true}");
	private static long ExpectedResponseBytes => ResponsePrefix.Length + OversizedDataBytes + ResponseSuffix.Length;

	[Test]
	[Description("Runs execute-esq through clio-run against an oversized DataService response and verifies the structured result-too-large envelope plus continued MCP session usability.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq rejects oversized responses without closing the MCP session")]
	[AllureDescription("Streams an 84 MB chunked DataService response through the real clio MCP process, verifies a bounded result-too-large envelope, and proves the same MCP session remains usable.")]
	public async Task ClioRun_ShouldKeepSessionUsable_WhenExecuteEsqResponseExceedsByteBudget() {
		// Arrange
		await using SelectQueryStubServer creatioStub = SelectQueryStubServer.StartChunked(WriteOversizedBodyAsync);
		string tempHome = ScratchDirectory.CreateUnderPhysicalTemp("clio-execute-esq-size-e2e");
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "oversized-esq-e2e",
			  "Environments": {
			    "oversized-esq-e2e": {
			      "Uri": "{{creatioStub.ApplicationUri}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await AllureApi.Step(
			"Arrange the real MCP process and oversized DataService stub",
			async () => await McpServerSession.StartAsync(settings, cancellationTokenSource.Token));

		try {
			// Act
			CallToolResult callResult = await AllureApi.Step(
				"Act by invoking execute-esq through clio-run",
				async () => await session.CallToolAsync(
					ClioRunTool.ToolName,
					new Dictionary<string, object?> {
						["command"] = ExecuteEsqTool.ToolName,
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = "oversized-esq-e2e",
							["query"] = new Dictionary<string, object?> {
								["rootSchemaName"] = "SysPackageReferenceAssembly",
								["allColumns"] = true,
								["rowCount"] = 200
							}
						}
					},
					cancellationTokenSource.Token));
			AllureApi.Step("Assert the stub emitted the reported failure-scale response", () =>
				creatioStub.SelectQueryResponseBytes.Should().Be(ExpectedResponseBytes,
					because: "the process regression must exercise an 84 MB response rather than only the rejection boundary"));
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);
			IReadOnlyList<ToolContractIndexEntry> followUpIndex = await AllureApi.Step(
				"Act by invoking a follow-up tool on the same MCP session",
				async () => await session.GetToolContractIndexAsync(cancellationTokenSource.Token));

			// Assert
			AllureApi.Step("Assert the oversized result is not an MCP protocol error", () =>
				callResult.IsError.Should().NotBeTrue(
					because: "an oversized backend result is a structured tool failure, not an MCP invocation error"));
			AllureApi.Step("Assert execute-esq reports failure", () =>
				response.Success.Should().BeFalse(
					because: "the oversized DataService body must not cross the MCP serialization boundary"));
			AllureApi.Step("Assert execute-esq reports the stable result-too-large class", () =>
				response.ErrorClass.Should().Be(ExecuteEsqTool.ResultTooLargeErrorClass,
					because: "the caller needs a stable signal for narrowing or paging the query"));
			AllureApi.Step("Assert oversized rows are omitted", () =>
				response.Rows.Should().BeNull(
					because: "the oversized row payload must be replaced by the bounded error envelope"));
			AllureApi.Step("Assert the same MCP session remains usable", () =>
				followUpIndex.Should().Contain(entry => entry.Name == ExecuteEsqTool.ToolName,
					because: "a follow-up MCP call on the same session must succeed after the oversized result is rejected"));
		}
		finally {
			ScratchDirectory.TryDelete(tempHome);
		}
	}

	/// <summary>Streams the 84 MB SelectQuery response: one row whose Data value is <see cref="OversizedDataBytes"/> bytes.</summary>
	private static async Task WriteOversizedBodyAsync(SelectQueryStubServer.ChunkWriter write) {
		await write(ResponsePrefix).ConfigureAwait(false);
		int remaining = OversizedDataBytes;
		while (remaining > 0) {
			int count = Math.Min(remaining, DataChunk.Length);
			await write(DataChunk.AsMemory(0, count)).ConfigureAwait(false);
			remaining -= count;
		}
		await write(ResponseSuffix).ConfigureAwait(false);
	}
}
