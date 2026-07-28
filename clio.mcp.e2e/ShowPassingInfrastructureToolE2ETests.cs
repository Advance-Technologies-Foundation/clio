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

[TestFixture]
// NoEnvironment tier: show-passing-infrastructure degrades gracefully on a host with no Kubernetes
// (the discovery service catches per-section failures) and always returns a structured availability
// payload rather than an MCP protocol error. The opaque InternalError that previously forced this
// onto the Sandbox tier was the no-Kubernetes fallback IKubernetes client throwing from Dispose
// during per-request DI-scope teardown — fixed under ENG-91830, so the test is now deterministically
// green env-free and belongs in the merge-blocking NoEnvironment gate.
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("show-passing-infrastructure")]
public sealed class ShowPassingInfrastructureToolE2ETests
{
	private const string ToolName = ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes show-passing-infrastructure, and verifies the structured passing-only deployment inventory shape without assuming that infrastructure is available on the host machine.")]
	[AllureTag(ToolName)]
	[AllureName("Show passing infrastructure returns structured deployment inventory")]
	[AllureDescription("Uses the real clio MCP server to invoke show-passing-infrastructure and verifies the passing-only infrastructure payload shape and recommendation contract.")]
	public async Task ShowPassingInfrastructure_Should_Return_Structured_Result()
	{
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		ActResult actResult = await ActAsync(arrangeContext);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "passing-infrastructure discovery should report availability inside the payload instead of as an MCP transport error");
		new[] { "available", "unavailable" }.Should().Contain(actResult.Execution.Status,
			because: "the passing-infrastructure contract should always expose one of the defined availability states");
		actResult.Execution.Summary.Should().NotBeNullOrWhiteSpace(
			because: "the payload should include a human-readable availability summary");
		actResult.Execution.Kubernetes.Should().NotBeNull(
			because: "the payload should always include the Kubernetes section");
		actResult.Execution.Local.Should().NotBeNull(
			because: "the payload should always include the local section");
		actResult.Execution.Filesystem.Should().NotBeNull(
			because: "the payload should always include the filesystem section");
		AssertCandidateShapes(actResult.Execution.Kubernetes.Databases, actResult.Execution.Local.Databases);
		AssertRedisShapes(actResult.Execution.Kubernetes.Redis, actResult.Execution.Local.RedisServers);
		AssertRecommendationShape(actResult.Execution.RecommendedDeployment);
		AssertRecommendationShape(actResult.Execution.RecommendedByEngine.Postgres);
		AssertRecommendationShape(actResult.Execution.RecommendedByEngine.Mssql);
	}

	private static async Task<ArrangeContext> ArrangeAsync()
	{
		return await AllureApi.Step("Arrange show-passing-infrastructure MCP session", async () =>
		{
			McpE2ESettings settings = TestConfiguration.Load();
			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
			McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			return new ArrangeContext(session, cancellationTokenSource);
		});
	}

	private static async Task<ActResult> ActAsync(ArrangeContext arrangeContext)
	{
		return await AllureApi.Step("Act by invoking show-passing-infrastructure through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(ToolName,
				because: "the show-passing-infrastructure MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed through the lazy surface");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?>(),
				arrangeContext.CancellationTokenSource.Token);

			ShowPassingInfrastructureEnvelope execution = ShowPassingInfrastructureResultParser.Extract(callResult);
			return new ActResult(callResult, execution);
		});
	}

	private static void AssertCandidateShapes(
		IEnumerable<ShowPassingInfrastructureDatabaseCandidateEnvelope> kubernetesCandidates,
		IEnumerable<ShowPassingInfrastructureDatabaseCandidateEnvelope> localCandidates)
	{
		foreach (ShowPassingInfrastructureDatabaseCandidateEnvelope candidate in kubernetesCandidates.Concat(localCandidates))
		{
			new[] { "k8", "local" }.Should().Contain(candidate.Source,
				because: "passing database candidates should identify their infrastructure source");
			candidate.Engine.Should().NotBeNullOrWhiteSpace(
				because: "passing database candidates should expose a normalized engine");
			candidate.Name.Should().NotBeNullOrWhiteSpace(
				because: "passing database candidates should expose a resolved name");
			candidate.Host.Should().NotBeNullOrWhiteSpace(
				because: "passing database candidates should expose a resolved host");
			candidate.Port.Should().BeGreaterThan(0,
				because: "passing database candidates should expose a resolved TCP port");
		}
	}

	private static void AssertRedisShapes(
		ShowPassingInfrastructureRedisCandidateEnvelope? kubernetesRedis,
		IEnumerable<ShowPassingInfrastructureRedisCandidateEnvelope> localRedis)
	{
		IEnumerable<ShowPassingInfrastructureRedisCandidateEnvelope> allCandidates =
			(kubernetesRedis is not null
				? new[] { kubernetesRedis }
				: Enumerable.Empty<ShowPassingInfrastructureRedisCandidateEnvelope>()).Concat(localRedis);
		foreach (ShowPassingInfrastructureRedisCandidateEnvelope candidate in allCandidates)
		{
			new[] { "k8", "local" }.Should().Contain(candidate.Source,
				because: "passing Redis candidates should identify their infrastructure source");
			candidate.Name.Should().NotBeNullOrWhiteSpace(
				because: "passing Redis candidates should expose a resolved name");
			candidate.Host.Should().NotBeNullOrWhiteSpace(
				because: "passing Redis candidates should expose a resolved host");
			candidate.Port.Should().BeGreaterThan(0,
				because: "passing Redis candidates should expose a resolved TCP port");
			candidate.FirstAvailableDb.Should().BeGreaterThanOrEqualTo(0,
				because: "passing Redis candidates should expose the discovered first available database index");
		}
	}

	private static void AssertRecommendationShape(ShowPassingInfrastructureRecommendationEnvelope? recommendation)
	{
		if (recommendation is null)
		{
			return;
		}

		new[] { "kubernetes", "local" }.Should().Contain(recommendation.DeploymentMode,
			because: "recommendations should use one of the supported deployment modes");
		recommendation.DbEngine.Should().NotBeNullOrWhiteSpace(
			because: "recommendations should state the database engine they target");
		recommendation.DeployCreatioArguments.Should().NotBeNull(
			because: "recommendations should include the deploy-creatio argument bundle");
		(recommendation.DeployCreatioArguments.DbServerName is null ||
		 !string.IsNullOrWhiteSpace(recommendation.DeployCreatioArguments.DbServerName)).Should().BeTrue(
			because: "deploy-creatio recommendations should only expose an optional local db-server-name argument");
		(recommendation.DeployCreatioArguments.RedisServerName is null ||
		 !string.IsNullOrWhiteSpace(recommendation.DeployCreatioArguments.RedisServerName)).Should().BeTrue(
			because: "deploy-creatio recommendations should only expose an optional local redis-server-name argument");
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record ActResult(
		CallToolResult CallResult,
		ShowPassingInfrastructureEnvelope Execution);
}
