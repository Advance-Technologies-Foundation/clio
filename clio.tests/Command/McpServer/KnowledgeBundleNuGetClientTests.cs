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
	private IKnowledgeBundleRuntime _runtime;
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
		services.AddSingleton(new KnowledgeBundleNuGetOptions(TransportDeadlineMilliseconds: 100));
		services.AddSingleton<IKnowledgeBundlePackageClient, KnowledgeBundleNuGetClient>();
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		services.AddSingleton(_runtime);
		services.AddSingleton(new KnowledgeBundleRenewalOptions(CooldownMilliseconds: 0));
		services.AddSingleton<IKnowledgeBundleActivator, EnvironmentKnowledgeBundleActivator>();
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
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();
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
	[Description("Rejects a cross-origin PackageBaseAddress advertised by an otherwise allowed service index.")]
	public void DownloadNext_ShouldRejectPackageBaseAddress_WhenOriginDiffersFromConfiguredSource() {
		// Arrange
		_handler.PackageBaseAddress = "https://other-feed.invalid/flat/";
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(new HashSet<string>(), null, null, null, null);

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "service-index discovery must not pivot package requests to another origin");
		_handler.PackageRequests.Should().Be(0,
			because: "a rejected cross-origin flat-container address must never be contacted");
	}

	[Test]
	[Description("Converts a transport timeout before version selection into a typed no-candidate result.")]
	public void DownloadNext_ShouldReturnNoCandidate_WhenServiceIndexTimesOut() {
		// Arrange
		_handler.TimeoutServiceIndex = true;
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();

		// Act
		Action act = () => client.DownloadNext(new HashSet<string>(), null, null, null, null);
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(new HashSet<string>(), null, null, null, null);

		// Assert
		act.Should().NotThrow(because: "NuGet timeouts must stay inside the fail-closed discovery boundary");
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "no immutable version was selected before the service-index timeout");
	}

	[Test]
	[Description("Rejects an adversarially long untrusted flat-container version without regex timeout or package download.")]
	public void DownloadNext_ShouldIgnoreVersion_WhenVersionStringExceedsStableBound() {
		// Arrange
		_handler.Versions = [new string('9', 500_000)];
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();
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
	[Description("Rejects a flat-container index whose version count exceeds the bounded discovery budget.")]
	public void DownloadNext_ShouldReturnNoCandidate_WhenVersionEntryBudgetIsExceeded() {
		// Arrange
		_handler.Versions = Enumerable.Range(0, 4097)
			.Select(index => $"1.0.{index}")
			.ToArray();
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(
			new HashSet<string>(), null, null, null, null);

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "an oversized catalog must fail closed before version parsing, sorting, and fingerprinting");
		_handler.PackageRequests.Should().Be(0,
			because: "a version index outside the discovery budget must not select a package path");
	}

	[Test]
	[Description("Memoizes an invalid highest package while allowing a lower unattempted valid package to activate later.")]
	public void DownloadNext_ShouldContinueWithLowerVersion_WhenHigherVersionIsRejected() {
		// Arrange
		_handler.Versions = ["1.2.0", "999.0.0"];
		_handler.Packages["999.0.0"] = [0x00, 0x01];
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();
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
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();
		HashSet<string> rejected = new(StringComparer.Ordinal) { "1.0.0", "1.1.0" };

		// Act
		KnowledgeBundlePackageDownloadResult transient = client.DownloadNext(rejected, null, null, null, null);
		KnowledgeBundlePackageDownloadResult recovered = client.DownloadNext(rejected, null, null, null, null);

		// Assert
		transient.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "transport failures do not prove immutable package content is invalid");
		transient.PackageVersion.Should().BeNull(
			because: "a transient result must not cause the activator to blacklist the selected package version");
		recovered.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.Downloaded,
			because: "the same immutable version should be retried after a transient feed failure");
		_handler.RequestedPackageVersions.Should().Equal(new[] { "1.2.0", "1.2.0" },
			because: "the failed and successful attempts must target the same highest eligible version");
	}

	[Test]
	[Description("Cancels a package response body that stalls after successful response headers.")]
	public void DownloadNext_ShouldReturnNoCandidate_WhenPackageBodyStallsPastDeadline() {
		// Arrange
		_handler.StallPackageBody = true;
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();
		HashSet<string> rejected = new(StringComparer.Ordinal) { "1.0.0", "1.1.0" };
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(rejected, null, null, null, null);
		elapsed.Stop();

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "a stalled response body is a retryable transport failure rather than verified rejection");
		result.PackageVersion.Should().BeNull(
			because: "body timeout must not blacklist an immutable version that was never fully downloaded");
		elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
			because: "the configured discovery deadline must cover response-body reads after headers arrive");
	}

	[Test]
	[Description("Does not replay historical packages at or below the highest successfully activated package version.")]
	public void DownloadNext_ShouldIgnoreHistoricalVersions_WhenActivePackageFloorIsPresent() {
		// Arrange
		IKnowledgeBundlePackageClient client = _container.GetRequiredService<IKnowledgeBundlePackageClient>();

		// Act
		KnowledgeBundlePackageDownloadResult result = client.DownloadNext(
			new HashSet<string>(), "1.2.0", "1.2.0", null, null);

		// Assert
		result.Status.Should().Be(KnowledgeBundlePackageDownloadStatus.NoCandidate,
			because: "renewal is forward-only after a package version activates successfully");
		_handler.PackageRequests.Should().Be(0,
			because: "historical packages must be filtered before payload download");
	}

	[Test]
	[Description("Scans below more than 64 invalid packages through the real client and activates the lower valid package without a carousel.")]
	public void EnsureActivated_ShouldReachLowerValidPackage_WhenMoreThanCacheBoundHigherPackagesAreInvalid() {
		// Arrange
		string[] invalidVersions = Enumerable.Range(36, 65)
			.Reverse()
			.Select(version => $"{version}.0.0")
			.ToArray();
		_handler.Versions = invalidVersions.Append("35.0.0").ToArray();
		foreach (string invalidVersion in invalidVersions) {
			_handler.Packages[invalidVersion] = [0x00, 0x01];
		}
		_handler.Packages["35.0.0"] = CreatePackage([3, 5, 0]);
		ulong? activeSequence = null;
		byte[] observedBundle = [];
		_runtime.ActiveSequence.Returns(_ => activeSequence);
		_runtime.Activate(Arg.Any<Stream>(), "35.0.0").Returns(callInfo => {
			using MemoryStream copy = new();
			callInfo.Arg<Stream>().CopyTo(copy);
			observedBundle = copy.ToArray();
			activeSequence = 35;
			return new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				35,
				35,
				null);
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		for (int attempt = 0; attempt < 66; attempt++) {
			activator.EnsureActivated();
		}
		int scansAfterActivation = _handler.VersionIndexRequests;
		activator.EnsureActivated();
		bool postActivationScanCompleted = SpinWait.SpinUntil(
			() => _handler.VersionIndexRequests > scansAfterActivation,
			TimeSpan.FromSeconds(1));

		// Assert
		_handler.RequestedPackageVersions.Should().Equal(
			invalidVersions.Append("35.0.0"),
			because: "the descending cursor must visit every higher invalid version once before the valid fallback");
		observedBundle.Should().Equal(new byte[] { 3, 5, 0 },
			because: "the real NuGet client must deliver the lower valid inner-bundle bytes intact");
		activeSequence.Should().Be(35,
			because: "bounded recent rejection memory must not starve a valid lower immutable package");
		postActivationScanCompleted.Should().BeTrue(
			because: "the post-activation generation check should complete within the bounded test window");
		_handler.RequestedPackageVersions.Should().HaveCount(66,
			because: "an unchanged catalog must not replay an evicted invalid package after fallback activation");
	}

	[Test]
	[Description("Retries a newly inserted in-between version after transient failure while a descending scan cursor is active.")]
	public void EnsureActivated_ShouldRetryInsertedVersion_WhenFirstChangedCatalogDownloadIsTransient() {
		// Arrange
		_handler.Versions = ["999.0.0", "800.0.0"];
		_handler.Packages["999.0.0"] = [0x00, 0x01];
		_handler.Packages["800.0.0"] = [0x00, 0x01];
		_handler.Packages["900.0.0"] = CreatePackage([9, 0, 0]);
		ulong? activeSequence = null;
		_runtime.ActiveSequence.Returns(_ => activeSequence);
		_runtime.Activate(Arg.Any<Stream>(), "900.0.0").Returns(_ => {
			activeSequence = 900;
			return new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				900,
				900,
				null);
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();
		activator.EnsureActivated();
		_handler.Versions = ["999.0.0", "900.0.0", "800.0.0"];
		_handler.TransientPackageFailuresRemaining = 1;
		activator.EnsureActivated();
		activator.EnsureActivated();

		// Assert
		_handler.RequestedPackageVersions.Should().Equal(
			new[] { "999.0.0", "800.0.0", "900.0.0", "900.0.0" },
			because: "catalog generation reset must retry the inserted version after transient transport failure");
		activeSequence.Should().Be(900,
			because: "the changed-catalog package must remain eligible until a deterministic activation outcome");
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
