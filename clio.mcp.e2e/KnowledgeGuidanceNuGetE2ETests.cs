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
		CallToolResult addResult = await CallKnowledgeCommand(context, "add-knowledge-source", new Dictionary<string, object?> {
			["alias"] = "synthetic",
			["libraryId"] = SyntheticKnowledgeNuGetFixture.LibraryId,
			["type"] = "nuget",
			["location"] = _fixture.Feed.ServiceIndexUri.AbsoluteUri,
			["packageId"] = SyntheticKnowledgeNuGetFixture.PackageId,
			["trustedKeyId"] = _fixture.KeyId,
			["trustedPublicKeyPath"] = _fixture.PublicKeyPath,
			["enabled"] = true,
			["priority"] = 100,
			["participation"] = "authoritative",
			["confirmed"] = true
		});
		CallToolResult installResult = await CallKnowledgeCommand(
			context, "install-knowledge", new Dictionary<string, object?> { ["source"] = "synthetic" });
		(CallToolResult initialCall, GuidanceGetResponse initialResponse) = await CallSelectedGuide(context);
		ReadResourceResult? initialResourceResult = null;
		Exception? initialResourceError = null;
		try {
			initialResourceResult = await context.Session.ReadResourceAsync(
				_fixture.SelectedGuideUri,
				context.CancellationTokenSource.Token);
		} catch (Exception exception) {
			initialResourceError = exception;
		}
		SyntheticPackageEvidence updated = _fixture.PublishValid("1.1.0", sequence: 20, revision: "updated");
		CallToolResult updateResult = await CallKnowledgeCommand(
			context, "update-knowledge", new Dictionary<string, object?> { ["source"] = "synthetic" });
		(CallToolResult updatedCall, GuidanceGetResponse updatedResponse) = await CallSelectedGuide(context);
		_fixture.PublishInvalidSignature("1.2.0", sequence: 30, revision: "invalid");
		CallToolResult rejectedUpdate = await CallKnowledgeCommand(
			context, "update-knowledge", new Dictionary<string, object?> { ["source"] = "synthetic" });
		(CallToolResult retainedCall, GuidanceGetResponse retainedResponse) = await CallSelectedGuide(context);
		CallToolResult disableResult = await CallKnowledgeCommand(
			context, "disable-knowledge-source", new Dictionary<string, object?> { ["alias"] = "synthetic" });
		(CallToolResult disabledCall, GuidanceGetResponse disabledResponse) = await CallSelectedGuide(context);
		CallToolResult enableResult = await CallKnowledgeCommand(
			context, "enable-knowledge-source", new Dictionary<string, object?> { ["alias"] = "synthetic" });
		(CallToolResult reenabledCall, GuidanceGetResponse reenabledResponse) = await CallSelectedGuide(context);
		int requestsBeforeFreshProcess = _fixture.Feed.Requests.Count;
		await using McpServerSession freshSession = await McpServerSession.StartAsync(
			_settings,
			context.CancellationTokenSource.Token);
		(CallToolResult freshCall, GuidanceGetResponse freshResponse) = await CallSelectedGuide(
			freshSession,
			context.CancellationTokenSource.Token);
		int requestsAfterFreshProcess = _fixture.Feed.Requests.Count;
		CallToolResult infoResult = await CallKnowledgeCommand(context, "info-knowledge", new Dictionary<string, object?> {
			["source"] = "synthetic",
			["checkUpdates"] = false
		});
		CallToolResult listResult = await CallKnowledgeCommand(context, "list-knowledge-sources", null);
		CallToolResult deleteResult = await CallKnowledgeCommand(
			context,
			"delete-knowledge",
			new Dictionary<string, object?> { ["source"] = "synthetic", ["confirmed"] = true });
		(CallToolResult deletedCall, GuidanceGetResponse deletedResponse) = await CallSelectedGuide(context);
		CallToolResult removeResult = await CallKnowledgeCommand(
			context,
			"remove-knowledge-source",
			new Dictionary<string, object?> { ["alias"] = "synthetic", ["confirmed"] = true });
		CallToolResult removedListResult = await CallKnowledgeCommand(context, "list-knowledge-sources", null);

		// Assert
		AssertCommandSucceeded(addResult, "the explicitly trusted source should be persisted through clio-run");
		AssertCommandSucceeded(installResult, "the first verified synthetic package should be installed through clio-run");
		AssertSuccessfulDelivery(initialCall, initialResponse, _initial, "initial package");
		initialResourceError.Should().BeNull(
			because: "the canonical namespaced resource should be readable after verified installation");
		TextResourceContents initialResource = initialResourceResult!.Contents.Single().Should()
			.BeOfType<TextResourceContents>(
				because: "resources/read must serialize one verified synthetic article as plain text").Subject;
		initialResource.Uri.Should().Be(_fixture.SelectedGuideUri,
			because: "resources/read must preserve the stable external resource URI");
		Digest(initialResource.Text).Should().Be(_initial.SelectedGuideDigest,
			because: "resources/read must return the same generated bytes verified from the synthetic package");
		AssertCommandSucceeded(updateResult, "the newer package should publish atomically through clio-run");
		AssertSuccessfulDelivery(updatedCall, updatedResponse, updated, "updated package");
		AssertCommandFailed(rejectedUpdate, "an invalid newer package must be rejected by the mechanics layer");
		AssertSuccessfulDelivery(retainedCall, retainedResponse, updated, "retained package after rejected update");
		AssertCommandSucceeded(disableResult, "the source kill switch should be executable through clio-run");
		disabledCall.IsError.Should().NotBeTrue(
			because: "disabled knowledge is a typed availability state rather than an MCP process error");
		disabledResponse.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "the same MCP process must stop serving a disabled source immediately");
		AssertCommandSucceeded(enableResult, "the retained source should be re-enabled without reinstalling it");
		AssertSuccessfulDelivery(reenabledCall, reenabledResponse, updated, "re-enabled retained package");
		AssertSuccessfulDelivery(freshCall, freshResponse, updated, "fresh-process disk cache");
		requestsAfterFreshProcess.Should().Be(requestsBeforeFreshProcess,
			because: "a newly started MCP process must activate the disk cache without contacting NuGet");
		AssertCommandSucceeded(infoResult, "installed generations and their visible local path should be inspectable");
		SerializeResult(infoResult).Should().Contain("activeContentPath",
			because: "agents need the extracted content path without going through MCP");
		SerializeResult(infoResult).Should().Contain("1.1.0",
			because: "info-knowledge should expose the active synthetic library version");
		AssertCommandSucceeded(listResult, "configured sources should be discoverable through clio-run");
		SerializeResult(listResult).Should().Contain("com.example.synthetic",
			because: "source discovery must expose stable library identity without asserting real guidance content");
		AssertCommandSucceeded(deleteResult, "confirmed cache deletion should be executable through clio-run");
		deletedCall.IsError.Should().NotBeTrue(
			because: "deleted knowledge is reported as typed unavailability rather than an MCP process error");
		deletedResponse.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "the running MCP process must stop serving a source immediately after its cache is deleted");
		AssertCommandSucceeded(removeResult, "confirmed source removal should remove its retained configuration");
		AssertCommandSucceeded(removedListResult, "source listing should remain available after removing the last source");
		SerializeResult(removedListResult).Should().NotContain("com.example.synthetic",
			because: "a removed source must disappear from the persisted trusted-source catalog");
		_fixture.Feed.Requests.Should().ContainSingle(path => path.EndsWith("/1.0.0/clio.synthetic.knowledge.1.0.0.nupkg", StringComparison.Ordinal),
			because: "the explicit CLI install should download the initial immutable package once");
		_fixture.Feed.Requests.Should().ContainSingle(path => path.EndsWith("/1.1.0/clio.synthetic.knowledge.1.1.0.nupkg", StringComparison.Ordinal),
			because: "the explicit CLI update should download the newer immutable package once");
		_fixture.Feed.CompletedRequests.Should().ContainSingle(path => path.EndsWith(
			"/1.2.0/clio.synthetic.knowledge.1.2.0.nupkg",
			StringComparison.Ordinal), because: "invalid-update retention is meaningful only after the corrupt package completed");
	}

	private static async Task<CallToolResult> CallKnowledgeCommand(
		ArrangeContext context,
		string command,
		Dictionary<string, object?>? args) => await context.Session.CallToolAsync(
		ClioRunTool.ToolName,
		new Dictionary<string, object?> {
			["command"] = command,
			["args"] = args ?? new Dictionary<string, object?>()
		},
		context.CancellationTokenSource.Token);

	private static void AssertCommandSucceeded(CallToolResult result, string reason) {
		result.IsError.Should().NotBeTrue(because: reason);
		SerializeResult(result).Should().Contain("\"success\":true", because: reason);
	}

	private static void AssertCommandFailed(CallToolResult result, string reason) {
		result.IsError.Should().NotBeTrue(because: "typed lifecycle failure is a normal MCP response");
		SerializeResult(result).Should().Contain("\"success\":false", because: reason);
	}

	private static string SerializeResult(CallToolResult result) {
		if (result.StructuredContent is not null) {
			return JsonSerializer.Serialize(result.StructuredContent);
		}
		return string.Concat(result.Content.OfType<TextContentBlock>().Select(content => content.Text));
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
		string appSettings = JsonSerializer.Serialize(new Dictionary<string, object?> {
			["knowledge"] = new Dictionary<string, object?> {
				["sources"] = new Dictionary<string, object?> {
					["synthetic"] = new Dictionary<string, object?> {
						["library-id"] = SyntheticKnowledgeNuGetFixture.LibraryId,
						["type"] = "nuget",
						["location"] = _fixture.Feed.ServiceIndexUri.AbsoluteUri,
						["package-id"] = SyntheticKnowledgeNuGetFixture.PackageId,
						["trusted-key-id"] = _fixture.KeyId,
						["trusted-public-key-path"] = _fixture.PublicKeyPath,
						["enabled"] = true,
						["priority"] = 100,
						["participation"] = "authoritative"
					}
				}
			}
		});
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			appSettings,
			"knowledge-redirect-home");
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
