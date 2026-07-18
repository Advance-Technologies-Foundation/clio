using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Integration")]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class KnowledgeBundleDiscoveryTests {
	private ServiceProvider _container;
	private IKnowledgeBundleRuntime _runtime;
	private IKnowledgeBundlePackageClient _packageClient;
	private string _originalBundlePath;
	private string _candidatePath;

	[SetUp]
	public void SetUp() {
		_originalBundlePath = Environment.GetEnvironmentVariable(
			EnvironmentKnowledgeBundleActivator.BundlePathVariable);
		_candidatePath = Path.Combine(Path.GetTempPath(), $"clio-knowledge-{Guid.NewGuid():N}.zip");
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		_packageClient = Substitute.For<IKnowledgeBundlePackageClient>();
		_packageClient.IsConfigured.Returns(false);
		ServiceCollection services = new();
		services.AddSingleton(_runtime);
		services.AddSingleton(_packageClient);
		services.AddSingleton(new KnowledgeBundleRenewalOptions(CooldownMilliseconds: 0));
		services.AddSingleton<IKnowledgeBundleActivator, EnvironmentKnowledgeBundleActivator>();
		_container = services.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown() {
		Environment.SetEnvironmentVariable(
			EnvironmentKnowledgeBundleActivator.BundlePathVariable,
			_originalBundlePath);
		_container.Dispose();
		if (File.Exists(_candidatePath)) {
			File.Delete(_candidatePath);
		}
	}

	[Test]
	[Description("Discovers the configured candidate path, streams its bytes to verification, and attempts activation only once.")]
	public void EnsureActivated_ShouldStreamConfiguredCandidateOnce_WhenPathExists() {
		// Arrange
		byte[] expected = [0x50, 0x4B, 0x03, 0x04];
		File.WriteAllBytes(_candidatePath, expected);
		Environment.SetEnvironmentVariable(
			EnvironmentKnowledgeBundleActivator.BundlePathVariable,
			_candidatePath);
		byte[] observed = [];
		_runtime.Activate(Arg.Any<Stream>()).Returns(callInfo => {
			using MemoryStream copy = new();
			callInfo.Arg<Stream>().CopyTo(copy);
			observed = copy.ToArray();
			return new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Rejected,
				KnowledgeBundleRejectionCode.Malformed,
				null,
				null,
				"Synthetic candidate reached verification.");
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();
		activator.EnsureActivated();

		// Assert
		observed.Should().Equal(expected,
			because: "the discovery boundary must stream the downloaded candidate without transformation");
		_runtime.ReceivedCalls().Should().ContainSingle(
			because: "lazy activation must make at most one discovery attempt per process instance");
	}

	[Test]
	[Description("Leaves guidance unavailable without throwing when the configured candidate cannot be opened.")]
	public void EnsureActivated_ShouldRemainUnavailable_WhenConfiguredPathCannotBeRead() {
		// Arrange
		Environment.SetEnvironmentVariable(
			EnvironmentKnowledgeBundleActivator.BundlePathVariable,
			_candidatePath);
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		Action act = activator.EnsureActivated;

		// Assert
		act.Should().NotThrow(
			because: "an absent download must preserve typed cold-state serving instead of crashing startup");
		_runtime.ReceivedCalls().Should().BeEmpty(
			because: "verification cannot run when discovery did not produce a readable candidate stream");
	}

	[Test]
	[Description("Rejects a relative candidate path so an untrusted working directory cannot substitute the bundle.")]
	public void EnsureActivated_ShouldRemainUnavailable_WhenConfiguredPathIsRelative() {
		// Arrange
		Environment.SetEnvironmentVariable(
			EnvironmentKnowledgeBundleActivator.BundlePathVariable,
			"workspace-controlled-bundle.zip");
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();

		// Assert
		_runtime.ReceivedCalls().Should().BeEmpty(
			because: "bundle discovery must not resolve trust-sensitive paths against the current workspace");
	}

	[Test]
	[Description("Downloads each newer NuGet candidate once and streams it to the verifier for forward-only renewal.")]
	public void EnsureActivated_ShouldAttemptEachNewerNuGetPackageOnce_WhenFeedAdvances() {
		// Arrange
		_packageClient.IsConfigured.Returns(true);
		List<string[]> attemptedSnapshots = [];
		List<string?> activeVersionSnapshots = [];
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>()).Returns(callInfo => {
			IReadOnlySet<string> attempted = callInfo.Arg<IReadOnlySet<string>>();
			string? activeVersion = callInfo.ArgAt<string?>(1);
			activeVersionSnapshots.Add(activeVersion);
			attemptedSnapshots.Add(attempted.OrderBy(value => value, StringComparer.Ordinal).ToArray());
			if (activeVersion is null) {
				return new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded, "1.0.0", [1, 0, 0]);
			}
			if (activeVersion == "1.0.0") {
				return new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded, "1.1.0", [1, 1, 0]);
			}
			return new KnowledgeBundlePackageDownloadResult(
				KnowledgeBundlePackageDownloadStatus.NoCandidate, null, null);
		});
		ConcurrentQueue<byte[]> observed = [];
		ulong? activeSequence = null;
		_runtime.ActiveSequence.Returns(_ => activeSequence);
		_runtime.Activate(Arg.Any<Stream>(), Arg.Any<string?>()).Returns(callInfo => {
			using MemoryStream copy = new();
			callInfo.Arg<Stream>().CopyTo(copy);
			observed.Enqueue(copy.ToArray());
			activeSequence = (ulong)observed.Count;
			return new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				activeSequence,
				activeSequence,
				null);
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();
		activator.EnsureActivated();
		bool renewed = SpinWait.SpinUntil(() => observed.Count == 2, TimeSpan.FromSeconds(2));

		// Assert
		renewed.Should().BeTrue(because: "the background renewal should complete within the bounded test window");
		observed.Should().HaveCount(2,
			because: "only strictly newer immutable packages should reach verification");
		byte[][] observedValues = observed.ToArray();
		observedValues[0].Should().Equal(new byte[] { 1, 0, 0 },
			because: "the initial package bytes must reach verification first");
		observedValues[1].Should().Equal(new byte[] { 1, 1, 0 },
			because: "the renewed package bytes must reach verification second");
		attemptedSnapshots.Should().HaveCount(2,
			because: "cold activation and one renewal should each expose one attempted-version snapshot");
		attemptedSnapshots[0].Should().BeEmpty(
			because: "cold discovery starts before any immutable package was attempted");
		attemptedSnapshots[1].Should().BeEmpty(
			because: "successfully activated versions are tracked as a floor rather than as rejected packages");
		activeVersionSnapshots.Should().HaveCount(2,
			because: "cold activation and one renewal should each capture their package floor");
		activeVersionSnapshots[0].Should().BeNull(
			because: "cold discovery has no successfully activated package floor");
		activeVersionSnapshots[1].Should().Be("1.0.0",
			because: "renewal discovery must receive the highest successfully activated package as its floor");
		_packageClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IKnowledgeBundlePackageClient.DownloadNext))
			.Should().Be(2,
			because: "cold activation and one renewal should each perform exactly one discovery call");
	}

	[Test]
	[Description("Returns active guidance immediately while one renewal download continues in the background.")]
	public void EnsureActivated_ShouldNotBlockActiveServing_WhenRenewalIsSlow() {
		// Arrange
		_packageClient.IsConfigured.Returns(true);
		using ManualResetEventSlim downloadStarted = new(false);
		using ManualResetEventSlim releaseDownload = new(false);
		_runtime.ActiveSequence.Returns((ulong)10);
		using ManualResetEventSlim renewalCompleted = new(false);
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>()).Returns(_ => {
			downloadStarted.Set();
			releaseDownload.Wait(TimeSpan.FromSeconds(2));
			renewalCompleted.Set();
			return new KnowledgeBundlePackageDownloadResult(
				KnowledgeBundlePackageDownloadStatus.NoCandidate, null, null);
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		Task call = Task.Run(activator.EnsureActivated);
		bool returned = call.Wait(TimeSpan.FromSeconds(1));
		bool backgroundStarted = downloadStarted.Wait(TimeSpan.FromSeconds(1));

		// Assert
		try {
			returned.Should().BeTrue(
				because: "an active bundle must be served without waiting for renewal network I/O");
			backgroundStarted.Should().BeTrue(
				because: "the same request should schedule one background renewal attempt");
			_packageClient.ReceivedCalls()
				.Count(call => call.GetMethodInfo().Name == nameof(IKnowledgeBundlePackageClient.DownloadNext))
				.Should().Be(1,
				because: "single-flight renewal must schedule exactly one package discovery call");
		} finally {
			releaseDownload.Set();
			renewalCompleted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue(
				because: "the scheduled renewal must finish before synchronization primitives and the fixture are disposed");
		}
	}

	[Test]
	[Description("Allows only one bounded cold discovery attempt while concurrent callers return typed unavailable immediately.")]
	public void EnsureActivated_ShouldSingleFlightColdDiscovery_WhenConcurrentDownloadIsSlow() {
		// Arrange
		_packageClient.IsConfigured.Returns(true);
		using ManualResetEventSlim downloadStarted = new(false);
		using ManualResetEventSlim releaseDownload = new(false);
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>()).Returns(_ => {
			downloadStarted.Set();
			releaseDownload.Wait(TimeSpan.FromSeconds(2));
			return new KnowledgeBundlePackageDownloadResult(
				KnowledgeBundlePackageDownloadStatus.NoCandidate, null, null);
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		Task first = Task.Run(activator.EnsureActivated);
		bool started = downloadStarted.Wait(TimeSpan.FromSeconds(1));
		Task concurrent = Task.Run(activator.EnsureActivated);
		bool concurrentReturned = concurrent.Wait(TimeSpan.FromSeconds(1));
		activator.EnsureActivated();

		// Assert
		try {
			started.Should().BeTrue(because: "the first cold caller should own the bounded discovery attempt");
			concurrentReturned.Should().BeTrue(
				because: "a concurrent cold caller must not queue behind network I/O");
			_packageClient.ReceivedCalls()
				.Count(call => call.GetMethodInfo().Name == nameof(IKnowledgeBundlePackageClient.DownloadNext))
				.Should().Be(1,
					because: "single-flight cold discovery allows only one package-client request at a time");
		} finally {
			releaseDownload.Set();
			first.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue(
				because: "the cold discovery task must finish before test teardown");
		}
	}

	[Test]
	[Description("Evicts the oldest bounded rejected-version history while continuing to discover and activate a later valid package.")]
	public void EnsureActivated_ShouldRecoverAfterRejectedVersionHistoryExceedsBound() {
		// Arrange
		_packageClient.IsConfigured.Returns(true);
		int downloadCalls = 0;
		List<string[]> rejectedSnapshots = [];
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>()).Returns(callInfo => {
			rejectedSnapshots.Add(callInfo.ArgAt<IReadOnlySet<string>>(0)
				.OrderBy(version => version, StringComparer.Ordinal)
				.ToArray());
			downloadCalls++;
			return downloadCalls <= 65
				? new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Rejected,
					$"{99 + downloadCalls}.0.0",
					null)
				: new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded,
					"200.0.0",
					[2, 0, 0]);
		});
		ulong? activeSequence = null;
		_runtime.ActiveSequence.Returns(_ => activeSequence);
		_runtime.Activate(Arg.Any<Stream>(), "200.0.0").Returns(_ => {
			activeSequence = 200;
			return new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				200,
				200,
				null);
		});
		IKnowledgeBundleActivator activator = _container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		for (int attempt = 0; attempt < 66; attempt++) {
			activator.EnsureActivated();
		}

		// Assert
		downloadCalls.Should().Be(66,
			because: "bounded history eviction must not stop later feed discovery");
		rejectedSnapshots[^1].Should().HaveCount(64,
			because: "the rejected-version cache must remain bounded while discovery continues");
		rejectedSnapshots[^1].Should().NotContain("100.0.0",
			because: "the oldest rejected version should be evicted when the bounded cache is full");
		rejectedSnapshots[^1].Should().Contain("164.0.0",
			because: "the most recent rejected version should remain memoized before later recovery");
		activeSequence.Should().Be(200,
			because: "a later valid package above the compacted floor must still activate");
		_runtime.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IKnowledgeBundleRuntime.Activate))
			.Should().Be(1,
				because: "only the later downloaded valid package should reach inner-bundle activation");
	}

}

[TestFixture]
[Category("Integration")]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class KnowledgeBundleTrustStoreTests {
	private ServiceProvider _container;
	private string _originalKeyId;
	private string _originalPublicKeyPath;
	private string _root;

	[SetUp]
	public void SetUp() {
		_originalKeyId = Environment.GetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.KeyIdVariable);
		_originalPublicKeyPath = Environment.GetEnvironmentVariable(
			EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable);
		_root = Path.Combine(Path.GetTempPath(), $"clio-knowledge-trust-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
		ServiceCollection services = new();
		services.AddSingleton<IKnowledgeBundleTrustStore, EnvironmentKnowledgeBundleTrustStore>();
		_container = services.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown() {
		Environment.SetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.KeyIdVariable, _originalKeyId);
		Environment.SetEnvironmentVariable(
			EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable,
			_originalPublicKeyPath);
		_container.Dispose();
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	[Test]
	[Description("Loads one absolute SubjectPublicKeyInfo PEM as the configured throwaway trust anchor.")]
	public void TryGetPublicKeyPem_ShouldReturnPublicKey_WhenAbsoluteSpkiPathIsConfigured() {
		// Arrange
		const string keyId = "synthetic-test-key";
		string path = Path.Combine(_root, "public.pem");
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string expected = key.ExportSubjectPublicKeyInfoPem();
		File.WriteAllText(path, expected);
		Environment.SetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.KeyIdVariable, keyId);
		Environment.SetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable, path);
		IKnowledgeBundleTrustStore trustStore = _container.GetRequiredService<IKnowledgeBundleTrustStore>();

		// Act
		bool found = trustStore.TryGetPublicKeyPem(keyId, out string actual);

		// Assert
		found.Should().BeTrue(because: "an absolute configured SPKI public key is valid trust material");
		actual.Should().Be(expected,
			because: "verification must receive the exact configured public-key PEM");
	}

	[Test]
	[Description("Rejects relative public-key paths so the current workspace cannot replace the trust anchor.")]
	public void TryGetPublicKeyPem_ShouldRejectKey_WhenConfiguredPathIsRelative() {
		// Arrange
		const string keyId = "synthetic-test-key";
		Environment.SetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.KeyIdVariable, keyId);
		Environment.SetEnvironmentVariable(
			EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable,
			"workspace-controlled-key.pem");
		IKnowledgeBundleTrustStore trustStore = _container.GetRequiredService<IKnowledgeBundleTrustStore>();

		// Act
		bool found = trustStore.TryGetPublicKeyPem(keyId, out string actual);

		// Assert
		found.Should().BeFalse(
			because: "trust-sensitive paths must never resolve against an untrusted current directory");
		actual.Should().BeEmpty(because: "rejected trust configuration must not return key material");
	}

	[Test]
	[Description("Rejects private-key PEM so the consumer process never imports signing material as a trust anchor.")]
	public void TryGetPublicKeyPem_ShouldRejectKey_WhenPemContainsPrivateKey() {
		// Arrange
		const string keyId = "synthetic-test-key";
		string path = Path.Combine(_root, "private.pem");
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
		Environment.SetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.KeyIdVariable, keyId);
		Environment.SetEnvironmentVariable(EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable, path);
		IKnowledgeBundleTrustStore trustStore = _container.GetRequiredService<IKnowledgeBundleTrustStore>();

		// Act
		bool found = trustStore.TryGetPublicKeyPem(keyId, out string actual);

		// Assert
		found.Should().BeFalse(because: "Clio consumers may import only public SubjectPublicKeyInfo trust anchors");
		actual.Should().BeEmpty(because: "private signing material must not escape the trust-store boundary");
	}
}
