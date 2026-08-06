using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Covers the GitHub Release transport's discovery, selection, download, and verification rules.
/// </summary>
/// <remarks>
/// Every case drives the real transport against a stub message handler, so the assertions are about
/// what the transport accepts and refuses, not about how a fake was configured.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeGitHubReleaseTransportTests {
	private const string ApiBase = "https://api.github.com/";
	private const string Owner = "Advance-Technologies-Foundation";
	private const string Repository = "clio-knowledge";
	private const string AssetName = "clio-knowledge-bundle.zip";
	private const string LatestPath = "/repos/Advance-Technologies-Foundation/clio-knowledge/releases/latest";
	private const string AssetApiUrl = "https://api.github.com/repos/Advance-Technologies-Foundation/clio-knowledge/releases/assets/9001";
	private const string AssetRedirectUrl = "https://release-assets.githubusercontent.com/download/9001";

	private static readonly byte[] BundleBytes = Encoding.UTF8.GetBytes("synthetic-github-release-bundle");

	private ServiceProvider _container = null!;
	private HttpClient _httpClient = null!;
	private SyntheticGitHubHandler _handler = null!;

	[SetUp]
	public void SetUp() {
		_handler = new SyntheticGitHubHandler();
		_handler.Assets[AssetRedirectUrl] = BundleBytes;
		_handler.ReleaseJson = CreateReleaseJson();
		_httpClient = new HttpClient(_handler);
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(KnowledgeGitHubReleaseTransport.HttpClientName).Returns(_httpClient);
		ServiceCollection services = new();
		services.AddSingleton(factory);
		services.AddSingleton(new KnowledgeGitHubReleaseOptions(TransportDeadlineMilliseconds: 5_000));
		services.AddSingleton<KnowledgeGitHubReleaseTransport>();
		_container = services.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
		_httpClient.Dispose();
		_handler.Dispose();
	}

	[Test]
	[Description("Downloads the declared release asset, follows the GitHub redirect, and verifies the published digest.")]
	public void Retrieve_ShouldDownloadAsset_WhenLatestReleaseMatchesTheDeclaredContract() {
		// Arrange
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request());

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Downloaded,
			because: "a stable release exposing exactly the declared asset is an installable candidate");
		result.ResolvedRevision.Should().Be("1.10.0",
			because: "the release tag is the transport revision recorded against the installed generation");
		result.CandidateBytes.Should().Equal(BundleBytes,
			because: "the asset bytes must reach the bundle runtime untransformed");
		result.Diagnostic.Should().BeNull(
			because: "an immutable release needs no advisory about replaceable assets");
		_handler.RequestedPaths.Should().Equal(
			[
				LatestPath,
				"/repos/Advance-Technologies-Foundation/clio-knowledge/releases/assets/9001",
				"/download/9001"
			],
			because: "discovery, the asset API URL, and the redirect target are the only hops the transport may take");
	}

	[Test]
	[Description("Records an advisory when the release is not immutable, because its assets could still be replaced upstream.")]
	public void Retrieve_ShouldReportAdvisory_WhenReleaseIsNotImmutable() {
		// Arrange
		_handler.ReleaseJson = CreateReleaseJson(immutable: false);
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request());

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Downloaded,
			because: "a mutable release is still verifiable through its digest and publisher signature");
		result.Diagnostic.Should().Contain("immutable",
			because: "the operator must be able to see that upstream immutability is not in force");
	}

	[Test]
	[Description("Refuses a candidate whose downloaded bytes do not match the digest GitHub published for the asset.")]
	public void Retrieve_ShouldRejectCandidate_WhenDownloadedDigestDiffers() {
		// Arrange
		_handler.Assets[AssetRedirectUrl] = Encoding.UTF8.GetBytes("tampered-bundle-payload-XXXXXXX");
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request());

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Rejected,
			because: "bytes that do not hash to the published digest were altered in transit or at rest");
		result.ResolvedRevision.Should().Be("1.10.0",
			because: "a rejected revision must be recorded so the candidate walk does not retry it");
		result.CandidateBytes.Should().BeNull(
			because: "unverified bytes must never be handed on for activation");
	}

	[TestCaseSource(nameof(MalformedReleaseCases))]
	[Description("Refuses release metadata that does not describe exactly one uploaded, bounded, digest-bearing asset.")]
	public void Retrieve_ShouldFail_WhenReleaseMetadataViolatesTheContract(string caseName, string releaseJson) {
		// Arrange
		_handler.ReleaseJson = releaseJson;
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request());

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Failed,
			because: $"the '{caseName}' release does not satisfy the declared asset contract");
		result.CandidateBytes.Should().BeNull(
			because: $"the '{caseName}' release must never yield candidate bytes");
		_handler.RequestedPaths.Should().ContainSingle(
			because: $"the '{caseName}' release must be refused from metadata alone, without a download");
	}

	[TestCaseSource(nameof(ForbiddenRedirectCases))]
	[Description("Refuses an asset redirect that leaves HTTPS or targets a host outside the documented GitHub set.")]
	public void Retrieve_ShouldFail_WhenAssetRedirectTargetIsNotAllowed(string caseName, string redirectTarget) {
		// Arrange
		_handler.RedirectOverride = redirectTarget;
		_handler.Assets[redirectTarget] = BundleBytes;
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request());

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Failed,
			because: $"a '{caseName}' redirect must be refused rather than followed");
		result.CandidateBytes.Should().BeNull(
			because: $"no bytes may be read from a '{caseName}' redirect target");
		_handler.RequestedPaths.Should().NotContain(new Uri(redirectTarget).AbsolutePath,
			because: $"the transport must never issue a request to a '{caseName}' target");
	}

	[Test]
	[Description("Stops without downloading when GitHub answers the conditional metadata request with 304 Not Modified.")]
	public void Retrieve_ShouldReportNoCandidate_WhenReleaseMetadataIsUnchanged() {
		// Arrange
		_handler.ExpectedEntityTag = "\"release-etag-1\"";
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request(catalogFingerprint: "\"release-etag-1\""));

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.NoCandidate,
			because: "an unchanged release list offers nothing new to install");
		result.CatalogFingerprint.Should().Be("\"release-etag-1\"",
			because: "the fingerprint must survive so the next check can stay conditional");
		_handler.RequestedPaths.Should().ContainSingle(
			because: "a 304 must cost exactly one metadata request and no asset transfer");
	}

	[Test]
	[Description("Stops without downloading when the latest release is already the installed active revision.")]
	public void Retrieve_ShouldReportNoCandidate_WhenLatestReleaseIsAlreadyActive() {
		// Arrange
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request(activeRevision: "1.10.0"));

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.NoCandidate,
			because: "re-downloading the active generation would be pure waste and must be idempotent");
		_handler.RequestedPaths.Should().ContainSingle(
			because: "the active revision must be recognised from metadata, before any transfer");
	}

	[Test]
	[Description("Stops without downloading a revision the candidate search already refused.")]
	public void Retrieve_ShouldReportNoCandidate_WhenLatestReleaseWasAlreadyRejected() {
		// Arrange
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(
			Request(rejectedRevisions: new HashSet<string>(StringComparer.Ordinal) { "1.10.0" }));

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.NoCandidate,
			because: "offering a refused revision again would loop the candidate search against the network");
		_handler.RequestedPaths.Should().ContainSingle(
			because: "one metadata request is enough to establish that nothing new is on offer");
	}

	[Test]
	[Description("Reports a throttled or forbidden GitHub response as a failure without retrying it.")]
	public void Retrieve_ShouldFailWithoutRetry_WhenGitHubRefusesTheRequest() {
		// Arrange
		_handler.MetadataStatusCode = HttpStatusCode.Forbidden;
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request());

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Failed,
			because: "an access or rate-limit refusal is not a candidate problem and must surface as a failure");
		_handler.RequestedPaths.Should().ContainSingle(
			because: "retrying into an active rate limit would deepen the throttle");
	}

	[Test]
	[Description("Repairing an installed generation retrieves that exact tag instead of whatever release is latest.")]
	public void Retrieve_ShouldUseTagEndpoint_WhenAnExactRevisionIsRequested() {
		// Arrange
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request(exactRevision: "1.10.0"));

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Downloaded,
			because: "a repair must be able to re-fetch the exact generation it is repairing");
		_handler.RequestedPaths.Should().StartWith(
			"/repos/Advance-Technologies-Foundation/clio-knowledge/releases/tags/1.10.0",
			because: "repair must address the recorded tag, never the moving latest-release pointer");
	}

	[Test]
	[Description("Reports a failure instead of a candidate when the whole retrieval exceeds its transport deadline.")]
	public void Retrieve_ShouldFail_WhenTheOperationDeadlineElapses() {
		// Arrange
		_handler.MetadataDelay = TimeSpan.FromSeconds(30);
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		KnowledgeTransportResult result = transport.Retrieve(Request(transportDeadlineMilliseconds: 150));

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Failed,
			because: "a stalled GitHub response must expire against the deadline rather than hang the caller");
		result.CandidateBytes.Should().BeNull(
			because: "an expired retrieval produces no candidate");
	}

	[Test]
	[Description("Orders release tags for candidate bookkeeping and ignores values that are not exact library versions.")]
	public void GreaterRevision_ShouldOrderReleaseTags_AndIgnoreNonVersionValues() {
		// Arrange
		KnowledgeGitHubReleaseTransport transport = Transport();

		// Act
		string? ascending = transport.GreaterRevision("1.9.0", "1.10.0");
		string? descending = transport.GreaterRevision("2.0.0", "1.10.0");
		string? oneUnparsable = transport.GreaterRevision("v1.10.0", "1.9.0");
		string? bothUnparsable = transport.GreaterRevision("main", "v2");

		// Assert
		ascending.Should().Be("1.10.0",
			because: "release tags order numerically per component, not lexically");
		descending.Should().Be("2.0.0",
			because: "the greater major version wins regardless of argument order");
		oneUnparsable.Should().Be("1.9.0",
			because: "a tag that is not an exact library version cannot be ordered and must lose to one that is");
		bothUnparsable.Should().BeNull(
			because: "with no orderable tag there is no greater revision to report");
	}

	private static IEnumerable<TestCaseData> MalformedReleaseCases() {
		yield return new TestCaseData("draft", CreateReleaseJson(draft: true)).SetName("Draft release");
		yield return new TestCaseData("prerelease", CreateReleaseJson(prerelease: true)).SetName("Prerelease");
		yield return new TestCaseData("missing asset", CreateReleaseJson(assetName: "other-bundle.zip"))
			.SetName("Asset name does not match");
		yield return new TestCaseData("duplicate asset", CreateReleaseJson(duplicateAsset: true))
			.SetName("Two assets share the declared name");
		yield return new TestCaseData("unfinished upload", CreateReleaseJson(state: "starter"))
			.SetName("Asset is not uploaded");
		yield return new TestCaseData("missing digest", CreateReleaseJson(digest: null))
			.SetName("Asset publishes no digest");
		yield return new TestCaseData("malformed digest", CreateReleaseJson(digest: "sha256:not-a-digest"))
			.SetName("Asset digest is not SHA-256 hex");
		yield return new TestCaseData("wrong digest algorithm", CreateReleaseJson(digest: "md5:" + new string('a', 32)))
			.SetName("Asset digest names another algorithm");
		yield return new TestCaseData("oversized asset", CreateReleaseJson(size: 41L * 1024 * 1024))
			.SetName("Asset exceeds the size bound");
		yield return new TestCaseData("empty asset", CreateReleaseJson(size: 0))
			.SetName("Asset is empty");
		yield return new TestCaseData("wrong content type", CreateReleaseJson(contentType: "text/html"))
			.SetName("Asset is not an archive");
		yield return new TestCaseData("non-version tag", CreateReleaseJson(tag: "v1.10.0"))
			.SetName("Tag is not an exact library version");
		yield return new TestCaseData("foreign asset host",
				CreateReleaseJson(assetUrl: "https://attacker.invalid/releases/assets/9001"))
			.SetName("Asset URL is not a GitHub host");
	}

	private static IEnumerable<TestCaseData> ForbiddenRedirectCases() {
		yield return new TestCaseData("foreign host", "https://attacker.invalid/download/9001")
			.SetName("Redirect leaves the allowed GitHub hosts");
		yield return new TestCaseData("scheme downgrade", "http://release-assets.githubusercontent.com/download/9001")
			.SetName("Redirect downgrades to plain HTTP");
		yield return new TestCaseData("loopback", "https://127.0.0.1/download/9001")
			.SetName("Redirect points at loopback from a public origin");
		yield return new TestCaseData("embedded credentials",
				"https://user:secret@release-assets.githubusercontent.com/download/9001")
			.SetName("Redirect carries credentials in the URL");
	}

	private KnowledgeGitHubReleaseTransport Transport() =>
		_container.GetRequiredService<KnowledgeGitHubReleaseTransport>();

	private static KnowledgeTransportRequest Request(
		string? activeRevision = null,
		string? catalogFingerprint = null,
		string? exactRevision = null,
		IReadOnlySet<string>? rejectedRevisions = null,
		int? transportDeadlineMilliseconds = null) => new(
		"creatio-curated",
		new KnowledgeSourceConfiguration {
			LibraryId = "com.creatio.clio",
			Type = KnowledgeSourceType.GitHubRelease,
			Location = ApiBase,
			RepositoryOwner = Owner,
			RepositoryName = Repository,
			AssetName = AssetName,
			Priority = 100,
			Participation = KnowledgeSourceParticipation.Authoritative
		},
		rejectedRevisions ?? new HashSet<string>(StringComparer.Ordinal),
		activeRevision,
		null,
		null,
		catalogFingerprint,
		StagingDirectory: string.Empty,
		transportDeadlineMilliseconds,
		exactRevision);

	private static string CreateReleaseJson(
		string tag = "1.10.0",
		bool draft = false,
		bool prerelease = false,
		bool immutable = true,
		string assetName = AssetName,
		string state = "uploaded",
		string contentType = "application/zip",
		long? size = null,
		string? digest = "",
		string assetUrl = AssetApiUrl,
		bool duplicateAsset = false) {
		string effectiveDigest = digest == string.Empty
			? "sha256:" + Convert.ToHexString(SHA256.HashData(BundleBytes)).ToLowerInvariant()
			: digest!;
		string digestJson = digest is null ? "null" : $"\"{effectiveDigest}\"";
		long effectiveSize = size ?? BundleBytes.LongLength;
		string asset = $$"""
			{
			  "name": "{{assetName}}",
			  "state": "{{state}}",
			  "content_type": "{{contentType}}",
			  "size": {{effectiveSize}},
			  "digest": {{digestJson}},
			  "id": 9001,
			  "url": "{{assetUrl}}"
			}
			""";
		string assets = duplicateAsset ? $"{asset},{asset}" : asset;
		return $$"""
			{
			  "id": 5001,
			  "tag_name": "{{tag}}",
			  "draft": {{(draft ? "true" : "false")}},
			  "prerelease": {{(prerelease ? "true" : "false")}},
			  "immutable": {{(immutable ? "true" : "false")}},
			  "assets": [{{assets}}]
			}
			""";
	}

	/// <summary>
	/// A GitHub Releases API stub: one release document, one asset URL that redirects, and a record of
	/// every path the transport actually requested.
	/// </summary>
	private sealed class SyntheticGitHubHandler : HttpMessageHandler {
		private readonly List<string> _requestedPaths = [];

		internal Dictionary<string, byte[]> Assets { get; } = new(StringComparer.Ordinal);

		internal string ReleaseJson { get; set; } = string.Empty;

		internal string? RedirectOverride { get; set; }

		internal string? ExpectedEntityTag { get; set; }

		internal HttpStatusCode MetadataStatusCode { get; set; } = HttpStatusCode.OK;

		internal TimeSpan MetadataDelay { get; set; } = TimeSpan.Zero;

		internal IReadOnlyList<string> RequestedPaths {
			get {
				lock (_requestedPaths) {
					return _requestedPaths.ToArray();
				}
			}
		}

		// The transport issues synchronous sends, so the default HttpMessageHandler.Send — which throws
		// NotSupportedException — must be overridden or every case would fail before reaching the wire.
		protected override HttpResponseMessage Send(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			SendAsync(request, cancellationToken).GetAwaiter().GetResult();

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) {
			Uri uri = request.RequestUri!;
			lock (_requestedPaths) {
				_requestedPaths.Add(uri.AbsolutePath);
			}
			if (uri.AbsolutePath.Contains("/releases/", StringComparison.Ordinal)
					&& !uri.AbsolutePath.Contains("/assets/", StringComparison.Ordinal)) {
				return await CreateMetadataResponse(request, cancellationToken);
			}
			if (uri.AbsolutePath.Contains("/assets/", StringComparison.Ordinal)) {
				HttpResponseMessage redirect = new(HttpStatusCode.Found);
				redirect.Headers.Location = new Uri(RedirectOverride ?? AssetRedirectUrl, UriKind.Absolute);
				return redirect;
			}
			string key = Assets.Keys.FirstOrDefault(candidate =>
				string.Equals(new Uri(candidate).AbsolutePath, uri.AbsolutePath, StringComparison.Ordinal))
				?? string.Empty;
			return Assets.TryGetValue(key, out byte[]? bytes)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
				: new HttpResponseMessage(HttpStatusCode.NotFound);
		}

		private async Task<HttpResponseMessage> CreateMetadataResponse(
			HttpRequestMessage request,
			CancellationToken cancellationToken) {
			if (MetadataDelay > TimeSpan.Zero) {
				await Task.Delay(MetadataDelay, cancellationToken);
			}
			if (MetadataStatusCode != HttpStatusCode.OK) {
				return new HttpResponseMessage(MetadataStatusCode);
			}
			bool matchesEntityTag = ExpectedEntityTag is not null
				&& request.Headers.TryGetValues("If-None-Match", out IEnumerable<string>? values)
				&& values.Contains(ExpectedEntityTag, StringComparer.Ordinal);
			HttpResponseMessage response = new(matchesEntityTag ? HttpStatusCode.NotModified : HttpStatusCode.OK);
			if (ExpectedEntityTag is not null) {
				response.Headers.TryAddWithoutValidation("ETag", ExpectedEntityTag);
			}
			if (!matchesEntityTag) {
				response.Content = new StringContent(ReleaseJson, Encoding.UTF8, "application/json");
			}
			return response;
		}
	}
}
