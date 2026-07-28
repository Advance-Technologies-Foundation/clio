using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clio.Common.IIS;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class IisCertificateResolverTests {
	private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

	[Test]
	[Description("Selects the pinned usable host certificate even when another matching certificate expires later.")]
	public void Resolve_ShouldPreferPinnedCertificate_WhenPinIsUsable() {
		// Arrange
		IIisCertificateProvider provider = Substitute.For<IIisCertificateProvider>();
		IisCertificateInfo pinned = Certificate("AA", "k-host.example.com", Now.AddYears(1));
		IisCertificateInfo newer = Certificate("BB", "k-host.example.com", Now.AddYears(2));
		provider.GetCertificates().Returns([pinned, newer]);
		IisCertificateResolver sut = new(provider);

		// Act
		IisCertificateSelection result = sut.Resolve("k-host.example.com", "a a", Now);

		// Assert
		result.Certificate.Should().BeSameAs(pinned,
			because: "an eligible explicit pin must win over deterministic expiration ordering");
		result.PinnedCertificateUnavailable.Should().BeFalse(
			because: "the normalized pinned thumbprint exists in the usable candidate list");
	}

	[Test]
	[Description("Rejects expired, private-key-less, wrong-EKU, and non-matching certificates and deterministically selects the latest expiry.")]
	public void Resolve_ShouldFilterIneligibleCertificates_AndSelectLatestExpiry() {
		// Arrange
		IIisCertificateProvider provider = Substitute.For<IIisCertificateProvider>();
		IisCertificateInfo expected = Certificate("BB", "*.example.com", Now.AddYears(2));
		provider.GetCertificates().Returns([
			Certificate("AA", "k-host.example.com", Now.AddYears(1)),
			expected,
			Certificate("CC", "k-host.example.com", Now.AddDays(-1)),
			Certificate("DD", "k-host.example.com", Now.AddYears(3), hasPrivateKey: false),
			Certificate("EE", "k-host.example.com", Now.AddYears(3), supportsServerAuthentication: false),
			Certificate("FF", "other.example.com", Now.AddYears(3))
		]);
		IisCertificateResolver sut = new(provider);

		// Act
		IisCertificateSelection result = sut.Resolve("k-host.example.com", null, Now);

		// Assert
		result.Certificate.Should().BeSameAs(expected,
			because: "only usable host matches participate and the latest expiration is the deterministic fallback");
	}

	[Test]
	[Description("Reports a stale pin and still selects the best usable certificate for a non-failing HTTPS request.")]
	public void Resolve_ShouldReportUnavailablePin_AndFallback_WhenPinIsStale() {
		// Arrange
		IIisCertificateProvider provider = Substitute.For<IIisCertificateProvider>();
		IisCertificateInfo fallback = Certificate("BB", "k-host.example.com", Now.AddYears(2));
		provider.GetCertificates().Returns([fallback]);
		IisCertificateResolver sut = new(provider);

		// Act
		IisCertificateSelection result = sut.Resolve("k-host.example.com", "DEAD", Now);

		// Assert
		result.Certificate.Should().BeSameAs(fallback,
			because: "a stale preference must not prevent deployment when another usable certificate exists");
		result.PinnedCertificateUnavailable.Should().BeTrue(
			because: "callers should be able to warn that the persisted pin was unavailable");
	}

	[Test]
	[Description("Does not fall back to the common name when a SAN extension exists without any DNS identities.")]
	public void CertificateMetadata_ShouldNotUseCommonName_WhenSanContainsOnlyIpAddress() {
		// Arrange
		using RSA key = RSA.Create(2048);
		CertificateRequest request = new("CN=k-host.example.com", key, HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		SubjectAlternativeNameBuilder san = new();
		san.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(san.Build());
		using X509Certificate2 certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(30));

		// Act
		IisCertificateInfo result = WindowsIisCertificateProvider.ToInfo(certificate);

		// Assert
		result.DnsNames.Should().BeEmpty(
			because: "browser hostname validation ignores the common name whenever a SAN extension is present");
	}

	[Test]
	[Description("Treats Windows Any Extended Key Usage as permitting TLS server authentication.")]
	public void CertificateMetadata_ShouldSupportServerAuthentication_WhenEkuAllowsAnyPurpose() {
		// Arrange
		using RSA key = RSA.Create(2048);
		CertificateRequest request = new("CN=k-host.example.com", key, HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
			new OidCollection { new("2.5.29.37.0") }, critical: false));
		using X509Certificate2 certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(30));

		// Act
		IisCertificateInfo result = WindowsIisCertificateProvider.ToInfo(certificate);

		// Assert
		result.SupportsServerAuthentication.Should().BeTrue(
			because: "Windows All application policies certificates are valid for IIS server authentication");
	}

	private static IisCertificateInfo Certificate(string thumbprint, string dnsName, DateTimeOffset expires,
		bool hasPrivateKey = true, bool supportsServerAuthentication = true) =>
		new(thumbprint, $"CN={dnsName}", [dnsName], Now.AddYears(-1), expires,
			hasPrivateKey, supportsServerAuthentication);
}
