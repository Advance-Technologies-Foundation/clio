using System.Security.Cryptography;
using System.Text;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Knowledge;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("external-knowledge-nuget")]
[NonParallelizable]
public sealed class KnowledgeGuidanceNuGetE2ETests : McpContractFixtureBase {
	private readonly SyntheticKnowledgeNuGetFixture _fixture;
	private readonly SyntheticPackageEvidence _initial;

	public KnowledgeGuidanceNuGetE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_initial = _fixture.PublishValid("1.0.0", sequence: 10, revision: "initial");
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleActivator.BundlePathVariable] = null;
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
	[AllureName("synthetic NuGet package renews atomically and retains last-known-good")]
	[AllureDescription("Runs the real MCP server against an isolated loopback NuGet v3 feed, proves package discovery, download, inner-bundle verification and renewal, then rejects a newer invalid bundle without replacing the last-known-good synthetic payload.")]
	[Description("Downloads and renews verified synthetic guidance from NuGet while retaining last-known-good after a newer invalid package.")]
	public async Task NuGetTransport_ShouldRenewVerifiedBundle_AndRetainLastKnownGood_WhenNewerPackageIsInvalid() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		(CallToolResult initialCall, GuidanceGetResponse initialResponse) = await CallSelectedGuide(context);
		SyntheticPackageEvidence renewedEvidence = _fixture.PublishValid(
			"1.1.0", sequence: 20, revision: "renewed");
		(CallToolResult renewedCall, GuidanceGetResponse renewedResponse) = await PollForDigest(
			context, renewedEvidence.SelectedGuideDigest);
		SyntheticPackageEvidence invalidEvidence = _fixture.PublishInvalidSignature(
			"1.2.0", sequence: 30, revision: "invalid-newer");
		(CallToolResult retainedCall, GuidanceGetResponse retainedResponse) = await PollUntilPackageResponseCompleted(
			context, invalidEvidence.PackageVersion);
		int versionScansAfterInvalidResponse = _fixture.Feed.CompletedRequests.Count(path =>
			string.Equals(path, VersionIndexPath(), StringComparison.Ordinal));
		(retainedCall, retainedResponse) = await PollUntilCompletedVersionScans(
			context,
			versionScansAfterInvalidResponse + 1);
		ReadResourceResult retainedResource = await context.Session.ReadResourceAsync(
			_fixture.SelectedGuideUri,
			context.CancellationTokenSource.Token);

		// Assert
		AssertSuccessfulSyntheticDelivery(initialCall, initialResponse, _initial, "initial package");
		AssertSuccessfulSyntheticDelivery(renewedCall, renewedResponse, renewedEvidence, "newer valid package");
		AssertSuccessfulSyntheticDelivery(retainedCall, retainedResponse, renewedEvidence, "last-known-good package");
		_initial.Sequence.Should().Be(10,
			because: "the initial signed synthetic manifest carries the expected monotonic sequence");
		renewedEvidence.Sequence.Should().Be(20,
			because: "the valid renewal fixture must advance the signed synthetic sequence");
		invalidEvidence.Sequence.Should().Be(30,
			because: "the invalid newer fixture must carry a higher signed sequence before signature rejection");
		AssertResourceRetainsDigest(retainedResource, renewedEvidence.SelectedGuideDigest);
		AssertNuGetTransportRequests(_fixture.Feed.Requests, _initial, renewedEvidence, invalidEvidence);
		_fixture.Feed.CompletedRequests.Should().ContainSingle(path =>
				string.Equals(path, PackagePath(invalidEvidence.PackageVersion), StringComparison.Ordinal),
			because: "the invalid immutable package response must complete exactly once before retention is asserted");
	}

	private static async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> PollForDigest(
		ArrangeContext context,
		string expectedDigest) {
		DateTime deadline = DateTime.UtcNow.AddSeconds(10);
		(CallToolResult CallResult, GuidanceGetResponse Response) latest;
		do {
			latest = await CallSelectedGuide(context);
			if (latest.Response.Success
					&& latest.Response.Article is not null
					&& Digest(latest.Response.Article.Text) == expectedDigest) {
				return latest;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(50), context.CancellationTokenSource.Token);
		} while (DateTime.UtcNow < deadline);
		throw new TimeoutException("Synthetic guidance did not reach the expected digest before the bounded deadline.");
	}

	private async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> PollUntilPackageResponseCompleted(
		ArrangeContext context,
		string packageVersion) {
		string expectedPath = PackagePath(packageVersion);
		DateTime deadline = DateTime.UtcNow.AddSeconds(10);
		(CallToolResult CallResult, GuidanceGetResponse Response) latest;
		do {
			latest = await CallSelectedGuide(context);
			if (_fixture.Feed.CompletedRequests.Count(path =>
					string.Equals(path, expectedPath, StringComparison.Ordinal)) == 1) {
				return latest;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(50), context.CancellationTokenSource.Token);
		} while (DateTime.UtcNow < deadline);
		throw new TimeoutException("Synthetic NuGet response did not complete before the bounded deadline.");
	}

	private async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> PollUntilCompletedVersionScans(
		ArrangeContext context,
		int expectedMinimumCount) {
		DateTime deadline = DateTime.UtcNow.AddSeconds(10);
		do {
			(CallToolResult CallResult, GuidanceGetResponse Response) latest = await CallSelectedGuide(context);
			int completedScans = _fixture.Feed.CompletedRequests.Count(path =>
				string.Equals(path, VersionIndexPath(), StringComparison.Ordinal));
			if (completedScans >= expectedMinimumCount) {
				return latest;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(50), context.CancellationTokenSource.Token);
		} while (DateTime.UtcNow < deadline);
		throw new TimeoutException("Post-rejection NuGet version scan did not complete before the bounded deadline.");
	}

	private static async Task<(CallToolResult CallResult, GuidanceGetResponse Response)> CallSelectedGuide(
		ArrangeContext context) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = SyntheticKnowledgeNuGetFixture.SelectedGuideName
				}
			},
			context.CancellationTokenSource.Token);
		return (callResult, EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult));
	}

	[AllureStep("Assert {label} is delivered as verified synthetic guidance")]
	private static void AssertSuccessfulSyntheticDelivery(
		CallToolResult callResult,
		GuidanceGetResponse response,
		SyntheticPackageEvidence evidence,
		string label) {
		callResult.IsError.Should().NotBeTrue(
			because: $"the {label} should be a normal typed MCP result");
		response.Success.Should().BeTrue(
			because: $"the {label} should activate only after transport and bundle verification succeed");
		response.Article!.Name.Should().Be(SyntheticKnowledgeNuGetFixture.SelectedGuideName,
			because: "the selected stable fixture identity must survive NuGet delivery");
		Digest(response.Article.Text).Should().Be(evidence.SelectedGuideDigest,
			because: $"the {label} payload digest must match the generated synthetic fixture bytes");
	}

	[AllureStep("Assert docs routing retains the last-known-good synthetic digest")]
	private static void AssertResourceRetainsDigest(ReadResourceResult result, string expectedDigest) {
		TextResourceContents resource = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the stable docs route must keep serving one verified text resource").Which;
		Digest(resource.Text).Should().Be(expectedDigest,
			because: "a newer package with an invalid inner-bundle signature must not replace active bytes");
	}

	[AllureStep("Assert NuGet discovery and package download requests")]
	private static void AssertNuGetTransportRequests(
		IReadOnlyCollection<string> requests,
		SyntheticPackageEvidence initial,
		SyntheticPackageEvidence renewed,
		SyntheticPackageEvidence invalid) {
		requests.Should().Contain("/v3/index.json",
			because: "production discovery must resolve the NuGet v3 PackageBaseAddress resource");
		requests.Should().ContainSingle(path => string.Equals(path, PackagePath(initial.PackageVersion), StringComparison.Ordinal),
			because: "the initial immutable package must be downloaded exactly once from the flat container");
		requests.Should().ContainSingle(path => string.Equals(path, PackagePath(renewed.PackageVersion), StringComparison.Ordinal),
			because: "the newer valid immutable package must be downloaded exactly once for renewal");
		requests.Should().ContainSingle(path => string.Equals(path, PackagePath(invalid.PackageVersion), StringComparison.Ordinal),
			because: "the newer invalid immutable package must reach verification exactly once before last-known-good retention is proven");
	}

	private static string PackagePath(string packageVersion) {
		string normalizedId = SyntheticKnowledgeNuGetFixture.PackageId.ToLowerInvariant();
		return $"/flat/{normalizedId}/{packageVersion}/{normalizedId}.{packageVersion}.nupkg";
	}

	private static string VersionIndexPath() =>
		$"/flat/{SyntheticKnowledgeNuGetFixture.PackageId.ToLowerInvariant()}/index.json";

	private static string Digest(string text) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("external-knowledge-nuget")]
[NonParallelizable]
public sealed class KnowledgeGuidanceNuGetRedirectE2ETests : McpContractFixtureBase {
	private readonly SyntheticKnowledgeNuGetFixture _fixture;

	public KnowledgeGuidanceNuGetRedirectE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_fixture.Feed.RedirectServiceIndex = true;
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleActivator.BundlePathVariable] = null;
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
	[AllureName("synthetic NuGet service-index redirects are refused")]
	[AllureDescription("Runs a cold real MCP server against a loopback NuGet service index that redirects and proves typed unavailability without contacting the target.")]
	[Description("Refuses NuGet service-index redirects and returns typed unavailable without contacting the redirect target.")]
	public async Task NuGetTransport_ShouldReturnUnavailable_AndNotFollowServiceIndexRedirect() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(1));

		// Act
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
		callResult.IsError.Should().NotBeTrue(
			because: "redirect refusal is returned as typed unavailability rather than a protocol error");
		response.Success.Should().BeFalse(
			because: "a cold process cannot activate guidance from an unverified redirected feed");
		response.ErrorCode.Should().Be("guidance-unavailable",
			because: "redirect refusal must preserve the typed cold-state contract");
		response.Article.Should().BeNull(
			because: "no guidance bytes may be served before a direct feed response verifies successfully");
		_fixture.Feed.Requests.Should().NotContain("/redirect-target",
			because: "the production named HttpClient must not follow service-index redirects");
	}
}
