using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ConfiguredKnowledgeBundleTrustStoreTests {
	private string _directory = null!;
	private string _publicKeyPath = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeRuntimeConfigurationProvider _configurationProvider = null!;
	private IKnowledgeBundleTrustStore _trustStore = null!;
	private IKnowledgeTrustFingerprintService _fingerprints = null!;

	[SetUp]
	public void SetUp() {
		_directory = Path.Combine(Path.GetTempPath(), $"clio-knowledge-trust-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_publicKeyPath = Path.Combine(_directory, "publisher-public.pem");
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		File.WriteAllText(_publicKeyPath, key.ExportSubjectPublicKeyInfoPem());
		_configurationProvider = Substitute.For<IKnowledgeRuntimeConfigurationProvider>();
		_configurationProvider.GetCurrent().Returns(Configuration());
		ServiceCollection services = new();
		services.AddSingleton(_configurationProvider);
		services.AddSingleton<IKnowledgeBundleTrustStore, ConfiguredKnowledgeBundleTrustStore>();
		services.AddSingleton<IKnowledgeTrustFingerprintService, KnowledgeTrustFingerprintService>();
		_container = services.BuildServiceProvider();
		_trustStore = _container.GetRequiredService<IKnowledgeBundleTrustStore>();
		_fingerprints = _container.GetRequiredService<IKnowledgeTrustFingerprintService>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
		if (Directory.Exists(_directory)) {
			Directory.Delete(_directory, recursive: true);
		}
	}

	[Test]
	[Description("Loads public verification material only for the library and key ID authorized by its configured source.")]
	public void TryGetPublicKeyPem_ShouldReturnKey_WhenLibraryAndKeyAreAuthorized() {
		// Arrange

		// Act
		bool found = _trustStore.TryGetPublicKeyPem(
			"com.example.partner",
			"partner-key-2026",
			out string publicKey);

		// Assert
		found.Should().BeTrue(
			because: "each publisher must be verified with the public key explicitly authorized for its library");
		publicKey.Should().Contain("BEGIN PUBLIC KEY",
			because: "the trust store should return validated public-only SubjectPublicKeyInfo material");
	}

	[Test]
	[Description("Refuses to reuse one publisher's configured signing key for a different library identity.")]
	public void TryGetPublicKeyPem_ShouldRejectKey_WhenLibraryDoesNotMatchSource() {
		// Arrange

		// Act
		bool found = _trustStore.TryGetPublicKeyPem(
			"com.example.other",
			"partner-key-2026",
			out string publicKey);

		// Assert
		found.Should().BeFalse(
			because: "trusting a key for one publisher must not authorize a different library");
		publicKey.Should().BeEmpty(
			because: "rejected trust lookups must not expose key material");
	}

	[Test]
	[Description("Rejects private key PEM material even when it is stored at the configured local trust path.")]
	public void TryGetPublicKeyPem_ShouldRejectKey_WhenFileContainsPrivateMaterial() {
		// Arrange
		using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		File.WriteAllText(_publicKeyPath, privateKey.ExportPkcs8PrivateKeyPem());

		// Act
		bool found = _trustStore.TryGetPublicKeyPem(
			"com.example.partner",
			"partner-key-2026",
			out string publicKey);

		// Assert
		found.Should().BeFalse(
			because: "a trust configuration must never accept or load private signing material");
		publicKey.Should().BeEmpty(
			because: "rejected private material must not escape the bounded trust reader");
	}

	[Test]
	[Description("Rejects a public key on an unsupported curve instead of treating any ECDSA key as P-256 trust.")]
	public void TryGetPublicKeyPem_ShouldRejectKey_WhenCurveIsNotP256() {
		// Arrange
		using ECDsa unsupportedKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
		File.WriteAllText(_publicKeyPath, unsupportedKey.ExportSubjectPublicKeyInfoPem());

		// Act
		bool found = _trustStore.TryGetPublicKeyPem(
			"com.example.partner",
			"partner-key-2026",
			out string publicKey);

		// Assert
		found.Should().BeFalse(
			because: "bundle signatures are constrained to ECDSA P-256 and trust loading must enforce that curve");
		publicKey.Should().BeEmpty(
			because: "unsupported verification material must not be returned to the runtime");
	}

	[Test]
	[Description("Rejects a public-key file reached through a symlink or junction in its path ancestry.")]
	public void TryReadPublicKeyFile_ShouldRejectPath_WhenAncestryContainsReparsePoint() {
		// Arrange
		string actualDirectory = Path.Combine(_directory, "actual");
		string linkedDirectory = Path.Combine(_directory, "linked");
		Directory.CreateDirectory(actualDirectory);
		string actualKeyPath = Path.Combine(actualDirectory, "linked-public.pem");
		File.Copy(_publicKeyPath, actualKeyPath);
		try {
			Directory.CreateSymbolicLink(linkedDirectory, actualDirectory);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or NotSupportedException) {
			Assert.Ignore($"Symbolic links are unavailable in this test environment: {exception.Message}");
		}

		// Act
		bool found = EnvironmentKnowledgeBundleTrustStore.TryReadPublicKeyFile(
			Path.Combine(linkedDirectory, "linked-public.pem"),
			out string publicKey);

		// Assert
		found.Should().BeFalse(
			because: "trusted key resolution must not traverse mutable symlink or junction boundaries");
		publicKey.Should().BeEmpty(
			because: "reparse-point paths are outside the accepted local trust boundary");
	}

	[TestCase(@"\\server\share\publisher-public.pem")]
	[TestCase(@"\\?\C:\keys\publisher-public.pem")]
	[Platform("Win")]
	[Description("Rejects UNC and Windows device paths before any trusted-key file access.")]
	public void TryNormalizeLocalPublicKeyPath_ShouldRejectNetworkAndDevicePaths(string unsafePath) {
		// Arrange

		// Act
		bool accepted = EnvironmentKnowledgeBundleTrustStore.TryNormalizeLocalPublicKeyPath(
			unsafePath,
			requireExisting: false,
			out string normalizedPath);

		// Assert
		accepted.Should().BeFalse(
			because: "network and device namespaces must not supply mutable publisher trust roots");
		normalizedPath.Should().BeEmpty(
			because: "a rejected unsafe path must not be propagated into settings");
	}

	[Test]
	[Description("Hashes effective public-key bytes so same-path replacement and deletion invalidate activation identity.")]
	public void TryGetFingerprint_ShouldChangeAndThenFail_WhenSamePathKeyIsReplacedAndDeleted() {
		// Arrange
		using ECDsa replacement = ECDsa.Create(ECCurve.NamedCurves.nistP256);

		// Act
		bool initialFound = _fingerprints.TryGetFingerprint(_publicKeyPath, out string before);
		File.WriteAllText(_publicKeyPath, replacement.ExportSubjectPublicKeyInfoPem());
		bool replacementFound = _fingerprints.TryGetFingerprint(_publicKeyPath, out string after);
		File.Delete(_publicKeyPath);
		bool deletedFound = _fingerprints.TryGetFingerprint(_publicKeyPath, out string deleted);

		// Assert
		initialFound.Should().BeTrue(
			because: "the initial configured P-256 public key is valid fingerprint input");
		replacementFound.Should().BeTrue(
			because: "a valid replacement public key remains a usable trust root");
		after.Should().NotBe(before,
			because: "the fingerprint must identify effective key bytes rather than the stable path string");
		deletedFound.Should().BeFalse(
			because: "deleting the key must invalidate trust on the next activation reconciliation");
		deleted.Should().BeEmpty(
			because: "unavailable trust has no valid key fingerprint");
	}

	private KnowledgeConfiguration Configuration() => new() {
		Sources = new Dictionary<string, KnowledgeSourceConfiguration>(StringComparer.OrdinalIgnoreCase) {
			["partner"] = new() {
				LibraryId = "com.example.partner",
				Type = KnowledgeSourceType.NuGet,
				Location = "https://packages.example.test/v3/index.json",
				TrustedKeyId = "partner-key-2026",
				TrustedPublicKeyPath = _publicKeyPath,
				PackageId = "Example.Partner.Knowledge"
			}
		}
	};
}
