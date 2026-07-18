using System;
using System.IO;
using System.Security.Cryptography;
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
	private string _originalBundlePath;
	private string _candidatePath;

	[SetUp]
	public void SetUp() {
		_originalBundlePath = Environment.GetEnvironmentVariable(
			EnvironmentKnowledgeBundleActivator.BundlePathVariable);
		_candidatePath = Path.Combine(Path.GetTempPath(), $"clio-knowledge-{Guid.NewGuid():N}.zip");
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		ServiceCollection services = new();
		services.AddSingleton(_runtime);
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
