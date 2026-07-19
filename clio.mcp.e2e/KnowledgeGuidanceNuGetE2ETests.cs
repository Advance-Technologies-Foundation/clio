using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Knowledge;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("persistent-knowledge-cache")]
[NonParallelizable]
public sealed class KnowledgeGuidanceNuGetE2ETests : McpContractFixtureBase {
	private readonly SyntheticKnowledgeNuGetFixture _fixture;
	private readonly SyntheticPackageEvidence _initial;
	private McpE2ESettings _settings = null!;

	public KnowledgeGuidanceNuGetE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_initial = _fixture.PublishValid("1.0.0", sequence: 10, revision: "initial");
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_settings = settings;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{}", "knowledge-home");
		settings.ProcessEnvironmentVariables[KnowledgeBundleNuGetClient.SourceVariable] =
			_fixture.Feed.ServiceIndexUri.AbsoluteUri;
		settings.ProcessEnvironmentVariables[KnowledgeBundleNuGetClient.PackageIdVariable] =
			SyntheticKnowledgeNuGetFixture.PackageId;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = _fixture.KeyId;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] =
			_fixture.PublicKeyPath;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("disk knowledge update hot reloads in the same MCP process")]
	[AllureDescription("Installs a synthetic package into an isolated Clio home, updates it through the real CLI, and proves the already-running MCP process observes the new activation marker.")]
	[Description("Persists a synthetic knowledge package and hot reloads an externally installed update without restarting MCP.")]
	public async Task PersistentCache_ShouldHotReloadInSameMcpProcess_AfterCliUpdate() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ClioCliCommandResult installResult = await ClioCliCommandRunner.RunAsync(
			_settings,
			["install-knowledge"],
			cancellationToken: context.CancellationTokenSource.Token);
		(CallToolResult initialCall, GuidanceGetResponse initialResponse) = await CallSelectedGuide(context);
		IList<McpClientResource> advertisedResources = await context.Session.ListResourcesAsync(
			context.CancellationTokenSource.Token);
		ReadResourceResult initialResourceResult = await context.Session.ReadResourceAsync(
			_fixture.SelectedGuideUri,
			context.CancellationTokenSource.Token);
		SyntheticPackageEvidence updated = _fixture.PublishValid("1.1.0", sequence: 20, revision: "updated");
		ClioCliCommandResult updateResult = await ClioCliCommandRunner.RunAsync(
			_settings,
			["update-knowledge"],
			cancellationToken: context.CancellationTokenSource.Token);
		(CallToolResult updatedCall, GuidanceGetResponse updatedResponse) = await CallSelectedGuide(context);
		_fixture.PublishInvalidSignature("1.2.0", sequence: 30, revision: "invalid");
		ClioCliCommandResult rejectedUpdate = await ClioCliCommandRunner.RunAsync(
			_settings,
			["update-knowledge"],
			cancellationToken: context.CancellationTokenSource.Token);
		(CallToolResult retainedCall, GuidanceGetResponse retainedResponse) = await CallSelectedGuide(context);
		int requestsBeforeFreshProcess = _fixture.Feed.Requests.Count;
		await using McpServerSession freshSession = await McpServerSession.StartAsync(
			_settings,
			context.CancellationTokenSource.Token);
		(CallToolResult freshCall, GuidanceGetResponse freshResponse) = await CallSelectedGuide(
			freshSession,
			context.CancellationTokenSource.Token);
		int requestsAfterFreshProcess = _fixture.Feed.Requests.Count;
		ClioCliCommandResult infoResult = await ClioCliCommandRunner.RunAsync(
			_settings,
			["info-knowledge", "--offline", "--json"],
			cancellationToken: context.CancellationTokenSource.Token);
		ClioCliCommandResult onlineInfoResult = await ClioCliCommandRunner.RunAsync(
			_settings,
			["info-knowledge", "--json"],
			cancellationToken: context.CancellationTokenSource.Token);
		int requestsBeforeDelete = _fixture.Feed.Requests.Count;
		ClioCliCommandResult confirmedDelete = await ClioCliCommandRunner.RunAsync(
			_settings,
			["delete-knowledge", "--force"],
			cancellationToken: context.CancellationTokenSource.Token);
		(CallToolResult deletedCall, GuidanceGetResponse deletedResponse) = await CallSelectedGuide(context);

		// Assert
		installResult.ExitCode.Should().Be(0,
			because: $"the external install command must persist the first verified package: {installResult.StandardError}");
		AssertSuccessfulDelivery(initialCall, initialResponse, _initial, "initial package");
		advertisedResources.Select(resource => resource.Uri).Should().Contain(
			GuidanceCatalog.GetExternalResourceUris().Values,
			because: "every external guidance resource class must remain discoverable through the real MCP server");
		TextResourceContents initialResource = initialResourceResult.Contents.Single().Should()
			.BeOfType<TextResourceContents>(
				because: "resources/read must serialize one verified synthetic article as plain text").Subject;
		initialResource.Uri.Should().Be(_fixture.SelectedGuideUri,
			because: "resources/read must preserve the stable external resource URI");
		Digest(initialResource.Text).Should().Be(_initial.SelectedGuideDigest,
			because: "resources/read must return the same generated bytes verified from the synthetic package");
		updateResult.ExitCode.Should().Be(0,
			because: $"the external update command must atomically publish the newer package: {updateResult.StandardError}");
		AssertSuccessfulDelivery(updatedCall, updatedResponse, updated, "updated package");
		rejectedUpdate.ExitCode.Should().Be(1,
			because: "an invalid newer package must not replace the last-known-good installation");
		AssertSuccessfulDelivery(retainedCall, retainedResponse, updated, "retained package after rejected update");
		AssertSuccessfulDelivery(freshCall, freshResponse, updated, "fresh-process disk cache");
		requestsAfterFreshProcess.Should().Be(requestsBeforeFreshProcess,
			because: "a newly started MCP process must activate the disk cache without contacting NuGet");
		infoResult.ExitCode.Should().Be(0,
			because: $"the installed cache must remain inspectable: {infoResult.StandardError}");
		infoResult.StandardOutput.Should().Contain("1.1.0",
			because: "info-knowledge must report the version selected by the persisted activation marker");
		using JsonDocument offlineInfo = JsonDocument.Parse(infoResult.StandardOutput);
		JsonElement offlineRoot = offlineInfo.RootElement;
		offlineRoot.GetProperty("IsValid").GetBoolean().Should().BeTrue(
			because: "offline inspection must revalidate the persisted bundle and extracted materialization");
		offlineRoot.GetProperty("RootPath").GetString().Should().NotBeNullOrWhiteSpace(
			because: "the visible configured disk location is part of the lifecycle contract");
		offlineRoot.GetProperty("ActiveContentPath").GetString().Should().NotBeNullOrWhiteSpace(
			because: "agents need the extracted content path without going through MCP");
		offlineRoot.GetProperty("Source").GetString().Should().Be(_fixture.Feed.ServiceIndexUri.AbsoluteUri,
			because: "installation provenance must identify the non-secret source used for the active package");
		offlineRoot.GetProperty("PackageId").GetString().Should().Be(SyntheticKnowledgeNuGetFixture.PackageId,
			because: "installation provenance must identify the package owning the active version");
		offlineRoot.GetProperty("InstalledAtUtc").ValueKind.Should().Be(JsonValueKind.String,
			because: "the installation timestamp must be inspectable from disk metadata");
		onlineInfoResult.ExitCode.Should().Be(0,
			because: $"bounded online inspection should complete successfully: {onlineInfoResult.StandardError}");
		using JsonDocument onlineInfo = JsonDocument.Parse(onlineInfoResult.StandardOutput);
		onlineInfo.RootElement.GetProperty("UpdateAvailability").GetString().Should().Be("Available",
			because: "the remote catalog advertises a newer stable package even though activation rejected it");
		onlineInfo.RootElement.GetProperty("LatestVersion").GetString().Should().Be("1.2.0",
			because: "online inspection must expose the greatest stable remote version without downloading it");
		confirmedDelete.ExitCode.Should().Be(0,
			because: $"explicitly confirmed deletion must remove the managed cache: {confirmedDelete.StandardError}");
		deletedCall.IsError.Should().NotBeTrue(
			because: "cache deletion is observed as typed guidance unavailability, not a protocol error");
		deletedResponse.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "the same MCP process must stop serving its in-memory snapshot after marker deletion");
		_fixture.Feed.Requests.Should().HaveCount(requestsBeforeDelete,
			because: "MCP must remain disk-only and must not reinstall knowledge after external deletion");
		_fixture.Feed.Requests.Should().ContainSingle(path => path.EndsWith("/1.0.0/clio.synthetic.knowledge.1.0.0.nupkg", StringComparison.Ordinal),
			because: "the explicit CLI install should download the initial immutable package once");
		_fixture.Feed.Requests.Should().ContainSingle(path => path.EndsWith("/1.1.0/clio.synthetic.knowledge.1.1.0.nupkg", StringComparison.Ordinal),
			because: "the explicit CLI update should download the newer immutable package once");
		_fixture.Feed.CompletedRequests.Should().ContainSingle(path => path.EndsWith(
			"/1.2.0/clio.synthetic.knowledge.1.2.0.nupkg",
			StringComparison.Ordinal), because: "invalid-update retention is meaningful only after the corrupt package completed");
	}

	private static async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> CallSelectedGuide(
		ArrangeContext context) {
		return await CallSelectedGuide(context.Session, context.CancellationTokenSource.Token);
	}

	private static async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> CallSelectedGuide(
		McpServerSession session,
		CancellationToken cancellationToken) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = SyntheticKnowledgeNuGetFixture.SelectedGuideName
				}
			},
			cancellationToken);
		return (callResult, EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult));
	}

	private static void AssertSuccessfulDelivery(
		CallToolResult callResult,
		GuidanceGetResponse response,
		SyntheticPackageEvidence evidence,
		string label) {
		callResult.IsError.Should().NotBeTrue(
			because: $"the {label} should be returned as a normal typed MCP result");
		response.Success.Should().BeTrue(
			because: $"the {label} should be served only after package and bundle verification");
		response.Article!.Name.Should().Be(SyntheticKnowledgeNuGetFixture.SelectedGuideName,
			because: "the stable synthetic guide identity must survive disk delivery");
		Digest(response.Article.Text).Should().Be(evidence.SelectedGuideDigest,
			because: $"the {label} bytes must match the generated synthetic package");
	}

	private static string Digest(string text) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("persistent-knowledge-cache")]
[NonParallelizable]
public sealed class KnowledgeGuidanceNuGetRedirectE2ETests : McpContractFixtureBase {
	private readonly SyntheticKnowledgeNuGetFixture _fixture;
	private McpE2ESettings _settings = null!;

	public KnowledgeGuidanceNuGetRedirectE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_fixture.Feed.RedirectServiceIndex = true;
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_settings = settings;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{}", "knowledge-redirect-home");
		settings.ProcessEnvironmentVariables[KnowledgeBundleNuGetClient.SourceVariable] =
			_fixture.Feed.ServiceIndexUri.AbsoluteUri;
		settings.ProcessEnvironmentVariables[KnowledgeBundleNuGetClient.PackageIdVariable] =
			SyntheticKnowledgeNuGetFixture.PackageId;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = _fixture.KeyId;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] =
			_fixture.PublicKeyPath;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("NuGet service-index redirects are refused")]
	[Description("Refuses a redirected NuGet service index and leaves a cold persistent cache typed unavailable.")]
	public async Task Install_ShouldReturnUnavailable_AndNotFollowServiceIndexRedirect() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(1));

		// Act
		ClioCliCommandResult installResult = await ClioCliCommandRunner.RunAsync(
			_settings,
			["install-knowledge"],
			cancellationToken: context.CancellationTokenSource.Token);
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = SyntheticKnowledgeNuGetFixture.SelectedGuideName
				}
			},
			context.CancellationTokenSource.Token);
		GuidanceGetResponse response = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);

		// Assert
		installResult.ExitCode.Should().Be(1,
			because: "the explicit installer must fail when the configured NuGet service index redirects");
		callResult.IsError.Should().NotBeTrue(
			because: "transport refusal is returned as typed unavailability rather than a protocol error");
		response.Success.Should().BeFalse(
			because: "a cold cache cannot activate guidance from an unverified redirected feed");
		response.ErrorCode.Should().Be("guidance-unavailable",
			because: "redirect refusal must preserve the typed cold-state contract");
		_fixture.Feed.Requests.Should().NotContain("/redirect-target",
			because: "knowledge package discovery must never follow service-index redirects");
	}
}
