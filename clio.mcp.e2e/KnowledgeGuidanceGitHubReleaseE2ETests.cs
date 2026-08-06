using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
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

/// <summary>
/// Proves the GitHub Release delivery path end to end against a hermetic loopback Releases API.
/// </summary>
/// <remarks>
/// Mocked HTTP unit tests cannot show that a real <c>clio mcp-server</c> process installs, hot
/// reloads, retains last-known-good, and starts warm without a network. Every hop — discovery,
/// asset redirect, download, digest, signature — is served locally, so nothing here depends on
/// live GitHub availability.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("persistent-knowledge-cache")]
[NonParallelizable]
public sealed class KnowledgeGuidanceGitHubReleaseE2ETests : McpContractFixtureBase {
	private const string SourceAlias = "synthetic-release";

	private readonly SyntheticKnowledgeGitHubReleaseFixture _fixture;
	private readonly SyntheticReleaseEvidence _initial;
	private McpE2ESettings _settings = null!;

	public KnowledgeGuidanceGitHubReleaseE2ETests() {
		_fixture = SyntheticKnowledgeGitHubReleaseFixture.Create();
		_initial = _fixture.PublishValid("1.0.0", sequence: 10, revision: "initial");
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_settings = settings;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{}", "knowledge-release-home");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("GitHub Release knowledge installs, updates, and survives an offline restart")]
	[AllureDescription("Drives a real MCP process against a hermetic GitHub Releases API: cold install, hot reload on update, last-known-good retention for an unverifiable newer release, and a warm restart with the server offline.")]
	[Description("Installs, updates, and warm-restarts GitHub Release knowledge without Git and without contacting GitHub.")]
	public async Task GitHubReleaseKnowledge_ShouldInstallUpdateAndRestartOffline_WithoutGit() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult addResult = await CallKnowledgeCommand(
			context,
			KnowledgeManagementTools.AddKnowledgeSourceToolName,
			new Dictionary<string, object?> {
				["alias"] = SourceAlias,
				["libraryId"] = SyntheticKnowledgeGitHubReleaseFixture.LibraryId,
				["type"] = "github-release",
				["location"] = _fixture.Api.BaseUri.AbsoluteUri,
				["repositoryOwner"] = SyntheticKnowledgeGitHubReleaseFixture.RepositoryOwner,
				["repositoryName"] = SyntheticKnowledgeGitHubReleaseFixture.RepositoryName,
				["assetName"] = SyntheticKnowledgeGitHubReleaseFixture.AssetName,
				["trustedKeyId"] = _fixture.KeyId,
				["trustedPublicKeyPath"] = _fixture.PublicKeyPath,
				["enabled"] = true,
				["priority"] = 100,
				["participation"] = "authoritative",
				["confirmed"] = true
			});
		CallToolResult installResult = await CallKnowledgeCommand(
			context,
			KnowledgeManagementTools.InstallKnowledgeToolName,
			new Dictionary<string, object?> { ["source"] = SourceAlias });
		IList<McpClientResource> discoveredResources = await context.Session.ListResourcesAsync(
			context.CancellationTokenSource.Token);
		ReadResourceResult canonicalResource = await context.Session.ReadResourceAsync(
			_fixture.SelectedGuideUri,
			context.CancellationTokenSource.Token);
		ReadResourceResult legacyResource = await context.Session.ReadResourceAsync(
			SyntheticKnowledgeGitHubReleaseFixture.SelectedGuideLegacyUri,
			context.CancellationTokenSource.Token);
		(CallToolResult initialCall, GuidanceGetResponse initialResponse) = await CallSelectedGuide(context);

		SyntheticReleaseEvidence updated = _fixture.PublishValid("1.1.0", sequence: 20, revision: "updated");
		CallToolResult updateResult = await CallKnowledgeCommand(
			context,
			KnowledgeManagementTools.UpdateKnowledgeToolName,
			new Dictionary<string, object?> { ["source"] = SourceAlias });
		(CallToolResult updatedCall, GuidanceGetResponse updatedResponse) = await CallSelectedGuide(context);

		_fixture.PublishInvalidSignature("1.2.0", sequence: 30, revision: "unsigned");
		CallToolResult unsignedUpdate = await CallKnowledgeCommand(
			context,
			KnowledgeManagementTools.UpdateKnowledgeToolName,
			new Dictionary<string, object?> { ["source"] = SourceAlias });
		(CallToolResult afterUnsignedCall, GuidanceGetResponse afterUnsignedResponse) = await CallSelectedGuide(context);

		_fixture.PublishMismatchedDigest("1.3.0", sequence: 40, revision: "tampered");
		CallToolResult tamperedUpdate = await CallKnowledgeCommand(
			context,
			KnowledgeManagementTools.UpdateKnowledgeToolName,
			new Dictionary<string, object?> { ["source"] = SourceAlias });
		(CallToolResult afterTamperedCall, GuidanceGetResponse afterTamperedResponse) = await CallSelectedGuide(context);

		_fixture.Api.Offline = true;
		_fixture.Api.ResetRequests();
		await using McpServerSession offlineSession = await McpServerSession.StartAsync(
			_settings,
			context.CancellationTokenSource.Token);
		(CallToolResult offlineCall, GuidanceGetResponse offlineResponse) = await CallSelectedGuide(
			offlineSession,
			context.CancellationTokenSource.Token);
		IReadOnlyCollection<string> offlineRequests = _fixture.Api.Requests;

		// Assert
		AssertCommandSucceeded(addResult, "a github-release source with explicit trust should be persisted through clio-run");
		AssertCommandSucceeded(installResult, "the first verified release asset should install through clio-run");
		discoveredResources.Should().ContainSingle(resource => resource.Uri == _fixture.SelectedGuideUri,
			because: "every active article must be discoverable without a compiled resource class");
		canonicalResource.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the canonical namespaced URI must resolve one verified article");
		legacyResource.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "publisher-declared legacy URIs must keep resolving after the transport change");
		AssertDelivered(initialCall, initialResponse, _initial, "initial release");

		AssertCommandSucceeded(updateResult, "a newer signed release should publish atomically through clio-run");
		AssertDelivered(updatedCall, updatedResponse, updated, "updated release");

		AssertCommandFailed(unsignedUpdate, "a newer release whose signature does not verify must be refused");
		AssertDelivered(afterUnsignedCall, afterUnsignedResponse, updated, "last-known-good after an unsigned release");

		AssertCommandFailed(tamperedUpdate, "a newer release whose bytes do not match its published digest must be refused");
		AssertDelivered(afterTamperedCall, afterTamperedResponse, updated, "last-known-good after a tampered release");

		AssertDelivered(offlineCall, offlineResponse, updated, "warm restart with the Releases API offline");
		offlineRequests.Should().BeEmpty(
			because: "a warm MCP start must activate the verified local cache without any GitHub request at all");
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
		result.IsError.Should().NotBeTrue(because: "a typed lifecycle failure is a normal MCP response");
		SerializeResult(result).Should().Contain("\"success\":false", because: reason);
	}

	private static string SerializeResult(CallToolResult result) => result.StructuredContent is not null
		? JsonSerializer.Serialize(result.StructuredContent)
		: string.Concat(result.Content.OfType<TextContentBlock>().Select(content => content.Text));

	private static async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> CallSelectedGuide(
		ArrangeContext context) => await CallSelectedGuide(context.Session, context.CancellationTokenSource.Token);

	private static async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> CallSelectedGuide(
		McpServerSession session,
		CancellationToken cancellationToken) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = SyntheticKnowledgeGitHubReleaseFixture.SelectedGuideName
				}
			},
			cancellationToken);
		return (callResult, EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult));
	}

	private static void AssertDelivered(
		CallToolResult callResult,
		GuidanceGetResponse response,
		SyntheticReleaseEvidence evidence,
		string label) {
		callResult.IsError.Should().NotBeTrue(
			because: $"the {label} should be returned as a normal typed MCP result");
		response.Success.Should().BeTrue(
			because: $"the {label} should be served only after digest, signature, and contract verification");
		response.Article!.Name.Should().Be(SyntheticKnowledgeGitHubReleaseFixture.SelectedGuideName,
			because: "the stable synthetic guide identity must survive release delivery");
		Digest(response.Article.Text).Should().Be(evidence.SelectedGuideDigest,
			because: $"the {label} bytes must match the generation the fixture published");
	}

	private static string Digest(string text) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
