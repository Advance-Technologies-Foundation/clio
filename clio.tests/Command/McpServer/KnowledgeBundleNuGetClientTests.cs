using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class KnowledgeBundleNuGetClientTests {
	private const string Source = "https://synthetic-feed.invalid/v3/index.json";
	private const string PackageId = "Clio.Synthetic.Knowledge";
	private ServiceProvider _container;
	private HttpClient _httpClient;
	private SyntheticNuGetHandler _handler;
	private string? _originalSource;
	private string? _originalPackageId;

	[SetUp]
	public void SetUp() {
		_originalSource = Environment.GetEnvironmentVariable(KnowledgeBundleNuGetClient.SourceVariable);
		_originalPackageId = Environment.GetEnvironmentVariable(KnowledgeBundleNuGetClient.PackageIdVariable);
		Environment.SetEnvironmentVariable(KnowledgeBundleNuGetClient.SourceVariable, Source);
		Environment.SetEnvironmentVariable(KnowledgeBundleNuGetClient.PackageIdVariable, PackageId);
		_handler = new SyntheticNuGetHandler();
		_handler.Packages["1.2.0"] = CreatePackage([0x50, 0x4B, 0x01, 0x02]);
		_httpClient = new HttpClient(_handler);
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(KnowledgeBundleNuGetClient.HttpClientName).Returns(_httpClient);
		ServiceCollection services = new();
		services.AddSingleton(factory);
		services.AddSingleton(new KnowledgeBundleNuGetOptions(TransportDeadlineMilliseconds: 1_000));
		services.AddSingleton<KnowledgeBundleNuGetClient>();
		_container = services.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown() {
		Environment.SetEnvironmentVariable(KnowledgeBundleNuGetClient.SourceVariable, _originalSource);
		Environment.SetEnvironmentVariable(KnowledgeBundleNuGetClient.PackageIdVariable, _originalPackageId);
		_container.Dispose();
		_httpClient.Dispose();
		_handler.Dispose();
	}

	[Test]
	[Description("Discovers the latest unattempted stable flat-container package and extracts its fixed inner knowledge bundle once.")]
	public void DownloadNext_ShouldExtractInnerBundle_WhenFeedHasUnattemptedStablePackage() {
		// Arrange
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();
		HashSet<string> attempted = new(StringComparer.Ordinal) { "1.0.0", "1.1.0" };

		// Act
		KnowledgeBundlePackageDownloadResult downloaded = client.DownloadNext(attempted, null, null, null, null);
		attempted.Add(downloaded.PackageVersion!);
		KnowledgeBundlePackageDownloadResult downloadedAgain = client.DownloadNext(attempted, null, null, null, null);

		// Assert
		downloaded.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Downloaded,
			because: "the highest stable package version is newer than the last attempted version");
		downloaded.PackageVersion.Should().Be("1.2.0",
			because: "NuGet discovery must select the greatest stable three-part version");
		downloaded.BundleBytes.Should().Equal(new byte[] { 0x50, 0x4B, 0x01, 0x02 },
			because: "the fixed package entry must be extracted without transforming inner bundle bytes");
		downloadedAgain.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "an already attempted immutable package version must not be downloaded repeatedly");
		_handler.PackageRequests.Should().Be(1,
			because: "the flat-container payload should be fetched only for a strictly newer version");
	}

	[Test]
	[Description("Downloads only the requested immutable package revision when repairing an installed knowledge generation.")]
	public void DownloadNext_ShouldSelectExactRevision_WhenRepairRevisionIsSpecified() {
		// Arrange
		_handler.Packages["1.1.0"] = CreatePackage([0x50, 0x4B, 0x11, 0x00]);
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(
			new HashSet<string>(StringComparer.Ordinal),
			activePackageVersion: null,
			highestObservedPackageVersion: null,
			fallbackCeilingPackageVersion: null,
			catalogFingerprint: null,
			exactPackageVersion: "1.1.0");

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Downloaded,
			because: "an exact repair revision that remains in the signed package catalog is eligible");
		result.PackageVersion.Should().Be("1.1.0",
			because: "repair must not silently replace the damaged immutable generation with the latest package");
		result.BundleBytes.Should().Equal(new byte[] { 0x50, 0x4B, 0x11, 0x00 },
			because: "the selected bytes must come from the exact active package revision");
	}

	[Test]
	[Description("NuGet transport retrieves from the supplied source configuration instead of process-global legacy variables.")]
	public void Retrieve_ShouldUseConfiguredSource_WhenLegacyEnvironmentVariablesAreMissing() {
		// Arrange
		Environment.SetEnvironmentVariable(KnowledgeBundleNuGetClient.SourceVariable, null);
		Environment.SetEnvironmentVariable(KnowledgeBundleNuGetClient.PackageIdVariable, null);
		KnowledgeBundleNuGetClient transport = _container
			.GetRequiredService<KnowledgeBundleNuGetClient>();
		KnowledgeTransportRequest request = new(
			"partner",
			new KnowledgeSourceConfiguration {
				LibraryId = "com.example.partner",
				Type = KnowledgeSourceType.NuGet,
				Location = Source,
				TrustedKeyId = "test-signing-key",
				TrustedPublicKeyPath = Path.GetFullPath("test-public.pem"),
				PackageId = PackageId
			},
			new HashSet<string>(StringComparer.Ordinal),
			null,
			null,
			null,
			null,
			Path.GetTempPath());

		// Act
		KnowledgeTransportResult result = transport.Retrieve(request);

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Downloaded,
			because: "multi-source retrieval must use the selected source instead of global legacy configuration");
		result.ResolvedRevision.Should().Be("1.2.0",
			because: "NuGet transport provenance is the selected immutable stable package version");
		result.CandidateBytes.Should().Equal(new byte[] { 0x50, 0x4B, 0x01, 0x02 },
			because: "the common transport contract must carry the extracted signed bundle bytes");
	}

	[Test]
	[Description("NuGet transport preserves a failed feed check and its diagnostic on the common transport contract.")]
	public void Retrieve_ShouldReturnFailed_WhenConfiguredFeedTimesOut() {
		// Arrange
		_handler.TimeoutServiceIndex = true;
		KnowledgeBundleNuGetClient transport = _container.GetRequiredService<KnowledgeBundleNuGetClient>();
		KnowledgeTransportRequest request = new(
			"partner",
			new KnowledgeSourceConfiguration {
				LibraryId = "com.example.partner",
				Type = KnowledgeSourceType.NuGet,
				Location = Source,
				TrustedKeyId = "test-signing-key",
				TrustedPublicKeyPath = Path.GetFullPath("test-public.pem"),
				PackageId = PackageId
			},
			new HashSet<string>(StringComparer.Ordinal),
			null,
			null,
			null,
			null,
			Path.GetTempPath());

		// Act
		KnowledgeTransportResult result = transport.Retrieve(request);

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Failed,
			because: "a feed timeout cannot be collapsed into a successful no-candidate lookup");
		result.ResolvedRevision.Should().BeNull(
			because: "a failed catalog request did not resolve an immutable package revision");
		result.Diagnostic.Should().NotBeNullOrWhiteSpace(
			because: "the management layer needs the transport diagnostic to report unknown update state");
	}

	[Test]
	[Description("Reports the greatest stable catalog version without downloading a package payload.")]
	public void GetCatalog_ShouldReturnLatestStableVersion_WithoutDownloadingPackage() {
		// Arrange
		_handler.Versions = ["1.0.0", "2.0.0-beta", "1.2.0"];
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageCatalogResult result = client.GetCatalog();

		// Assert
		result.IsAvailable.Should().BeTrue(
			because: "the service index and bounded version catalog are readable");
		result.LatestVersion.Should().Be("1.2.0",
			because: "update status considers only stable three-part package versions");
		_handler.PackageRequests.Should().Be(0,
			because: "an update availability check must not download package content");
	}

	[Test]
	[Description("Reports catalog availability as unknown when the service index cannot be reached within the deadline.")]
	public void GetCatalog_ShouldReturnUnavailable_WhenTransportFails() {
		// Arrange
		_handler.TimeoutServiceIndex = true;
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageCatalogResult result = client.GetCatalog();

		// Assert
		result.IsAvailable.Should().BeFalse(
			because: "a transport failure cannot prove that the installed package is current");
		result.LatestVersion.Should().BeNull(
			because: "no remote version was observed through the failed catalog request");
	}

	[Test]
	[Description("Rejects feed URLs containing query credentials before they can be persisted or displayed.")]
	public void GetConfiguration_ShouldRejectSource_WhenUrlContainsQuery() {
		// Arrange
		Environment.SetEnvironmentVariable(
			KnowledgeBundleNuGetClient.SourceVariable,
			$"{Source}?token=secret");
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageConfiguration? configuration = client.GetConfiguration();

		// Assert
		configuration.Should().BeNull(
			because: "source URLs emitted by info and install metadata must never contain credentials");
	}

	[Test]
	[Description("Rejects a cross-origin PackageBaseAddress advertised by an otherwise allowed service index.")]
	public void DownloadNext_ShouldRejectPackageBaseAddress_WhenOriginDiffersFromConfiguredSource() {
		// Arrange
		_handler.PackageBaseAddress = "https://other-feed.invalid/flat/";
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(new HashSet<string>(), null, null, null, null);

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Failed,
			because: "an invalid service index is a failed retrieval, not proof that no update exists");
		result.Diagnostic.Should().NotBeNullOrWhiteSpace(
			because: "operators need a reason why update availability could not be determined");
		_handler.PackageRequests.Should().Be(0,
			because: "a rejected cross-origin flat-container address must never be contacted");
	}

	[Test]
	[Description("Converts a transport timeout before version selection into a typed failed-retrieval result.")]
	public void DownloadNext_ShouldReturnFailed_WhenServiceIndexTimesOut() {
		// Arrange
		_handler.TimeoutServiceIndex = true;
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		Action act = () => client.DownloadNext(new HashSet<string>(), null, null, null, null);
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(new HashSet<string>(), null, null, null, null);

		// Assert
		act.Should().NotThrow(because: "NuGet timeouts must stay inside the fail-closed discovery boundary");
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Failed,
			because: "a timeout cannot prove that no newer immutable version exists");
		result.Diagnostic.Should().NotBeNullOrWhiteSpace(
			because: "the retrieval failure must remain distinguishable from an empty catalog");
	}

	[Test]
	[Description("Rejects an adversarially long untrusted flat-container version without regex timeout or package download.")]
	public void DownloadNext_ShouldIgnoreVersion_WhenVersionStringExceedsStableBound() {
		// Arrange
		_handler.Versions = [new string('9', 500_000)];
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(
			new HashSet<string>(), null, null, null, null);
		elapsed.Stop();

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "oversized untrusted version text is not a supported stable package identity");
		_handler.PackageRequests.Should().Be(0,
			because: "invalid version text must be rejected before constructing a package request");
		elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
			because: "a fixed length gate must reject adversarial version text before regex work");
	}

	[Test]
	[Description("Reports a flat-container index outside the bounded discovery budget as a failed retrieval.")]
	public void DownloadNext_ShouldReturnFailed_WhenVersionEntryBudgetIsExceeded() {
		// Arrange
		_handler.Versions = Enumerable.Range(0, 4097)
			.Select(index => $"1.0.{index}")
			.ToArray();
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(
			new HashSet<string>(), null, null, null, null);

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Failed,
			because: "an invalid remote catalog cannot prove that the installed package is current");
		_handler.PackageRequests.Should().Be(0,
			because: "a version index outside the discovery budget must not select a package path");
	}

	[Test]
	[Description("Memoizes an invalid highest package while allowing a lower unattempted valid package to activate later.")]
	public void DownloadNext_ShouldContinueWithLowerVersion_WhenHigherVersionIsRejected() {
		// Arrange
		_handler.Versions = ["1.2.0", "999.0.0"];
		_handler.Packages["999.0.0"] = [0x00, 0x01];
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();
		HashSet<string> attempted = new(StringComparer.Ordinal);

		// Act
		KnowledgeBundlePackageDownloadResult rejected = client.DownloadNext(attempted, null, null, null, null);
		attempted.Add(rejected.PackageVersion!);
		KnowledgeBundlePackageDownloadResult recovered = client.DownloadNext(
			attempted, null, "999.0.0", "999.0.0", rejected.CatalogFingerprint);
		attempted.Add(recovered.PackageVersion!);
		KnowledgeBundlePackageDownloadResult exhausted = client.DownloadNext(
			attempted, null, "999.0.0", "1.2.0", recovered.CatalogFingerprint);

		// Assert
		rejected.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Rejected,
			because: "a malformed immutable package must be identified as an attempted rejected version");
		rejected.PackageVersion.Should().Be("999.0.0",
			because: "the highest advertised stable version is attempted first");
		recovered.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Downloaded,
			because: "an invalid high version must not poison a lower unattempted valid version");
		recovered.PackageVersion.Should().Be("1.2.0",
			because: "discovery must continue with the next greatest unattempted stable version");
		exhausted.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "every advertised immutable version has now been attempted");
		_handler.RequestedPackageVersions.Should().Equal(new[] { "999.0.0", "1.2.0" },
			because: "each selected package path must be fetched exactly once in descending order");
	}

	[Test]
	[Description("Retries an immutable package after a transient HTTP failure instead of permanently rejecting its version.")]
	public void DownloadNext_ShouldRetryPackage_WhenPreviousDownloadFailureWasTransient() {
		// Arrange
		_handler.TransientPackageFailuresRemaining = 1;
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();
		HashSet<string> rejected = new(StringComparer.Ordinal) { "1.0.0", "1.1.0" };

		// Act
		KnowledgeBundlePackageDownloadResult transient = client.DownloadNext(rejected, null, null, null, null);
		KnowledgeBundlePackageDownloadResult recovered = client.DownloadNext(rejected, null, null, null, null);

		// Assert
		transient.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Failed,
			because: "transport failures do not prove either immutable content invalidity or absence of updates");
		transient.PackageVersion.Should().BeNull(
			because: "a transient result must not cause the activator to blacklist the selected package version");
		recovered.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Downloaded,
			because: "the same immutable version should be retried after a transient feed failure");
		_handler.RequestedPackageVersions.Should().Equal(new[] { "1.2.0", "1.2.0" },
			because: "the failed and successful attempts must target the same highest eligible version");
	}

	[Test]
	[Description("Cancels a stalled package response body and reports update availability as unknown.")]
	public void DownloadNext_ShouldReturnFailed_WhenPackageBodyStallsPastDeadline() {
		// Arrange
		_handler.StallPackageBody = true;
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();
		HashSet<string> rejected = new(StringComparer.Ordinal) { "1.0.0", "1.1.0" };
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(rejected, null, null, null, null);
		elapsed.Stop();

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Failed,
			because: "a stalled response body is a retryable transport failure rather than proof of no update");
		result.PackageVersion.Should().BeNull(
			because: "body timeout must not blacklist an immutable version that was never fully downloaded");
		elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
			because: "the configured discovery deadline must cover response-body reads after headers arrive");
	}

	[Test]
	[Description("Does not replay historical packages at or below the highest successfully activated package version.")]
	public void DownloadNext_ShouldIgnoreHistoricalVersions_WhenActivePackageFloorIsPresent() {
		// Arrange
		KnowledgeBundleNuGetClient client = _container.GetRequiredService<KnowledgeBundleNuGetClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(
			new HashSet<string>(), "1.2.0", "1.2.0", null, null);

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "renewal is forward-only after a package version activates successfully");
		_handler.PackageRequests.Should().Be(0,
			because: "historical packages must be filtered before payload download");
	}

	private static byte[] CreatePackage(byte[] innerBundle) {
		using MemoryStream output = new();
		using (ZipArchive package = new(output, ZipArchiveMode.Create, leaveOpen: true)) {
			ZipArchiveEntry entry = package.CreateEntry(KnowledgeBundleNuGetClient.InnerBundlePath);
			using Stream stream = entry.Open();
			stream.Write(innerBundle);
		}
		return output.ToArray();
	}

	private sealed class SyntheticNuGetHandler : HttpMessageHandler {
		private int _packageRequests;
		private int _versionIndexRequests;

		internal Dictionary<string, byte[]> Packages { get; } = new(StringComparer.Ordinal);
		internal string[] Versions { get; set; } = ["1.0.0", "2.0.0-beta", "1.2.0", "1.1.0"];
		internal string PackageBaseAddress { get; set; } = "https://synthetic-feed.invalid/flat/";
		internal bool TimeoutServiceIndex { get; set; }
		internal int TransientPackageFailuresRemaining { get; set; }
		internal bool StallPackageBody { get; set; }
		internal int PackageRequests => _packageRequests;
		internal int VersionIndexRequests => _versionIndexRequests;
		internal List<string> RequestedPackageVersions { get; } = [];

		protected override HttpResponseMessage Send(
			HttpRequestMessage request,
			CancellationToken cancellationToken) => Resolve(request);

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) => Task.FromResult(Resolve(request));

		private HttpResponseMessage Resolve(HttpRequestMessage request) {
			string path = request.RequestUri!.AbsolutePath;
			if (path == "/v3/index.json") {
				if (TimeoutServiceIndex) {
					throw new TaskCanceledException("Synthetic timeout.");
				}
				return Json(new {
					version = "3.0.0",
					resources = new[] {
						new Dictionary<string, string> {
							["@id"] = PackageBaseAddress,
							["@type"] = "PackageBaseAddress/3.0.0"
						}
					}
				});
			}
			if (path == "/flat/clio.synthetic.knowledge/index.json") {
				Interlocked.Increment(ref _versionIndexRequests);
				return Json(new { versions = Versions });
			}
			const string prefix = "/flat/clio.synthetic.knowledge/";
			if (path.StartsWith(prefix, StringComparison.Ordinal)) {
				string version = path[prefix.Length..].Split('/')[0];
				Interlocked.Increment(ref _packageRequests);
				RequestedPackageVersions.Add(version);
				if (TransientPackageFailuresRemaining > 0) {
					TransientPackageFailuresRemaining--;
					return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
				}
				if (StallPackageBody) {
					return new HttpResponseMessage(HttpStatusCode.OK) {
						Content = new StalledHttpContent()
					};
				}
				if (!Packages.TryGetValue(version, out byte[]? packageBytes)) {
					return new HttpResponseMessage(HttpStatusCode.NotFound);
				}
				return new HttpResponseMessage(HttpStatusCode.OK) {
					Content = new ByteArrayContent(packageBytes)
				};
			}
			return new HttpResponseMessage(HttpStatusCode.NotFound);
		}

		private static HttpResponseMessage Json<T>(T value) =>
			new(HttpStatusCode.OK) {
				Content = new StringContent(
					System.Text.Json.JsonSerializer.Serialize(value),
					Encoding.UTF8,
					"application/json")
			};

		private sealed class StalledHttpContent : HttpContent {
			internal StalledHttpContent() {
				Headers.ContentLength = 4;
			}

			protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
				Task.CompletedTask;

			protected override void SerializeToStream(
				Stream stream,
				TransportContext? context,
				CancellationToken cancellationToken) =>
				Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).GetAwaiter().GetResult();

			protected override bool TryComputeLength(out long length) {
				length = 4;
				return true;
			}

			protected override Task<Stream> CreateContentReadStreamAsync() =>
				Task.FromResult<Stream>(new StalledStream());
		}

		private sealed class StalledStream : Stream {
			public override bool CanRead => true;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position {
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}

			public override void Flush() {
			}

			public override int Read(byte[] buffer, int offset, int count) =>
				throw new NotSupportedException();

			public override async ValueTask<int> ReadAsync(
				Memory<byte> buffer,
				CancellationToken cancellationToken = default) {
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				return 0;
			}

			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}
	}
}
