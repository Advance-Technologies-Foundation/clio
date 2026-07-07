using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the full infrastructure assert MCP tool.
/// </summary>
[TestFixture]
// NoEnvironment tier: the arrange path is environment-free and the assert-infrastructure tool
// degrades gracefully on a host with no Kubernetes/Docker/local DB — each section catches its own
// failure and the tool always returns a structured aggregate result rather than an MCP protocol
// error. (Before ENG-91830 the no-Kubernetes fallback IKubernetes client threw from Dispose during
// per-request DI-scope teardown, surfacing as an opaque InternalError; that is fixed, so the test
// is now deterministically green env-free and belongs in the merge-blocking NoEnvironment gate.)
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("assert")]
public sealed class AssertInfrastructureToolE2ETests
{
	private const string ToolName = AssertInfrastructureTool.AssertInfrastructureToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes assert-infrastructure, and verifies that the structured aggregate payload contains Kubernetes, local, filesystem, and database candidate sections.")]
	[AllureTag(ToolName)]
	[AllureName("Assert Infrastructure tool returns full structured infrastructure result")]
	[AllureDescription("Uses the real clio MCP server to invoke assert-infrastructure and verifies the aggregate structured payload shape without assuming that every host environment section passes.")]
	public async Task AssertInfrastructure_Should_Return_Full_Structured_Result()
	{
		// Arrange
		await using AssertInfrastructureArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		AssertInfrastructureActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertAggregatePayloadShape(actResult);
		AssertSectionStatusesArePresent(actResult);
		AssertDatabaseCandidatesAreNormalized(actResult);
	}

	private static async Task<AssertInfrastructureArrangeContext> ArrangeAsync()
	{
		return await AllureApi.Step("Arrange assert-infrastructure MCP session", async () =>
		{
			McpE2ESettings settings = TestConfiguration.Load();
			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
			McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			return new AssertInfrastructureArrangeContext(session, cancellationTokenSource);
		});
	}

	private static async Task<AssertInfrastructureActResult> ActAsync(AssertInfrastructureArrangeContext arrangeContext)
	{
		return await AllureApi.Step("Act by invoking assert-infrastructure through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(ToolName,
				because: "the assert-infrastructure MCP tool must be discoverable via the get-tool-contract compact index on the lazy surface before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?>(),
				arrangeContext.CancellationTokenSource.Token);

			AssertInfrastructureEnvelope execution = AssertInfrastructureResultParser.Extract(callResult);
			return new AssertInfrastructureActResult(callResult, execution);
		});
	}

	[AllureStep("Assert MCP tool result is successful at protocol level")]
	[AllureDescription("Assert that assert-infrastructure returns a normal MCP tool result even when one or more infrastructure sections fail")]
	private static void AssertToolCallSucceeded(AssertInfrastructureActResult actResult)
	{
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "section failures should be represented inside the structured payload rather than as MCP transport failures");
	}

	[AllureStep("Assert aggregate payload shape is present")]
	[AllureDescription("Assert that the structured aggregate payload exposes status, exit code, summary, sections, and database candidate fields")]
	private static void AssertAggregatePayloadShape(AssertInfrastructureActResult actResult)
	{
		new[] { "pass", "partial", "fail" }.Should().Contain(actResult.Execution.Status,
			because: "the full infrastructure sweep should report one of the defined aggregate statuses");
		new[] { 0, 1 }.Should().Contain(actResult.Execution.ExitCode,
			because: "the aggregate tool should use exit code 0 for full success and 1 for section-level failures");
		actResult.Execution.Summary.Should().NotBeNullOrWhiteSpace(
			because: "the structured payload should include a human-readable summary");
		actResult.Execution.Sections.Should().NotBeNull(
			because: "the aggregate payload should always include the per-section results");
		actResult.Execution.DatabaseCandidates.Should().NotBeNull(
			because: "the aggregate payload should always include the normalized database candidate collection");
	}

	[AllureStep("Assert all per-section statuses are present")]
	[AllureDescription("Assert that Kubernetes, local, and filesystem sections are all present and each reports pass or fail")]
	private static void AssertSectionStatusesArePresent(AssertInfrastructureActResult actResult)
	{
		new[] { "pass", "fail" }.Should().Contain(actResult.Execution.Sections.K8.Status,
			because: "the Kubernetes section should always report a concrete assertion result");
		new[] { "pass", "fail" }.Should().Contain(actResult.Execution.Sections.Local.Status,
			because: "the local section should always report a concrete assertion result");
		new[] { "pass", "fail" }.Should().Contain(actResult.Execution.Sections.Filesystem.Status,
			because: "the filesystem section should always report a concrete assertion result");
	}

	[AllureStep("Assert normalized database candidates use the public MCP contract")]
	[AllureDescription("Assert that every database candidate returned by the full infrastructure sweep uses the normalized source, engine, host, and port shape expected by downstream agents")]
	private static void AssertDatabaseCandidatesAreNormalized(AssertInfrastructureActResult actResult)
	{
		foreach (AssertInfrastructureDatabaseCandidateEnvelope candidate in actResult.Execution.DatabaseCandidates)
		{
			new[] { "k8", "local" }.Should().Contain(candidate.Source,
				because: "database candidates should identify whether they were discovered from Kubernetes or local infrastructure");
			candidate.Engine.Should().NotBeNullOrWhiteSpace(
				because: "every database candidate should expose a normalized engine value");
			candidate.Name.Should().NotBeNullOrWhiteSpace(
				because: "every database candidate should expose a normalized name");
			candidate.Host.Should().NotBeNullOrWhiteSpace(
				because: "every database candidate should expose a resolved host");
			candidate.Port.Should().BeGreaterThan(0,
				because: "every database candidate should expose a resolved TCP port");
		}
	}

	private sealed record AssertInfrastructureArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record AssertInfrastructureActResult(
		CallToolResult CallResult,
		AssertInfrastructureEnvelope Execution);
}
