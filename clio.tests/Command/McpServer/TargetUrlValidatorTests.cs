using System;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class TargetUrlValidatorTests
{
	private const string NonLoopbackBoundHost = "0.0.0.0";
	private const string LoopbackBoundHost = "127.0.0.1";

	private static ITargetUrlValidator CreateValidator(
		string boundHost = NonLoopbackBoundHost,
		params string[] allowedBaseUrls) =>
		new TargetUrlValidator(boundHost, allowedBaseUrls);

	[Test]
	[Description("A relative url is rejected because an absolute http/https URL is required (AC-01).")]
	public void EnsureAllowed_ShouldReject_WhenUrlIsRelative() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("/some/relative/path");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "a relative url is not an absolute http/https URL (AC-01)")
			.Which.Message.Should().Contain("absolute http/https",
				because: "the rejection names the reason (AC-ERR)");
	}

	[Test]
	[Description("A garbage (unparseable) url is rejected because it is not an absolute http/https URL (AC-01).")]
	public void EnsureAllowed_ShouldReject_WhenUrlIsGarbage() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("not a url at all");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "an unparseable string is not an absolute http/https URL (AC-01)");
	}

	[Test]
	[Description("A non-http(s) scheme (ftp) is rejected because only http/https are allowed (AC-01).")]
	public void EnsureAllowed_ShouldReject_WhenSchemeIsNotHttp() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("ftp://acme.creatio.com/resource");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "ftp is not an http/https scheme (AC-01)")
			.Which.Message.Should().Contain("absolute http/https",
				because: "the rejection names the reason (AC-ERR)");
	}

	[Test]
	[Description("The cloud-metadata address 169.254.169.254 is always blocked, even with no allowlist (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsCloudMetadataAddress() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://169.254.169.254/latest/meta-data/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "the cloud-metadata address is a baseline block regardless of allowlist (AC-02)")
			.Which.Message.Should().Contain("cloud-metadata",
				because: "the rejection names the blocked address class (AC-ERR)");
	}

	[Test]
	[Description("An IPv4 link-local address (169.254.0.0/16) is always blocked (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsIpv4LinkLocal() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://169.254.10.20/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "IPv4 link-local is a baseline block regardless of allowlist (AC-02)")
			.Which.Message.Should().Contain("link-local",
				because: "the rejection names the blocked address class (AC-ERR)");
	}

	[Test]
	[Description("An IPv6 link-local address (fe80::/10) is always blocked (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsIpv6LinkLocal() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://[fe80::1]/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "IPv6 link-local is a baseline block regardless of allowlist (AC-02)")
			.Which.Message.Should().Contain("link-local",
				because: "the rejection names the blocked address class (AC-ERR)");
	}

	[Test]
	[Description("An IPv4-mapped IPv6 cloud-metadata literal is blocked so the dual-stack encoding cannot bypass baseline (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsIpv4MappedIpv6CloudMetadata() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://[::ffff:169.254.169.254]/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "an IPv4-mapped IPv6 literal must normalize to its IPv4 form and hit the metadata block (AC-02)")
			.Which.Message.Should().Contain("cloud-metadata",
				because: "the rejection names the blocked address class after normalization (AC-ERR)");
	}

	[Test]
	[Description("An IPv4-mapped IPv6 loopback literal is blocked when the server is NOT bound to loopback (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsIpv4MappedIpv6LoopbackAndBoundHostIsNotLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://[::ffff:127.0.0.1]:5000/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "an IPv4-mapped IPv6 loopback literal must normalize to IPv4 and hit the loopback block (AC-02)")
			.Which.Message.Should().Contain("loopback",
				because: "the rejection names the blocked address class after normalization (AC-ERR)");
	}

	[Test]
	[Description("An IPv4 loopback target is blocked when the server is NOT bound to loopback (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsIpv4LoopbackAndBoundHostIsNotLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://127.0.0.1:5000/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "loopback is blocked unless the server itself is bound to loopback (AC-02)")
			.Which.Message.Should().Contain("loopback",
				because: "the rejection names the blocked address class (AC-ERR)");
	}

	[Test]
	[Description("An IPv6 loopback target ([::1]) is blocked when the server is NOT bound to loopback (AC-02).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsIpv6LoopbackAndBoundHostIsNotLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://[::1]:5000/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "IPv6 loopback is blocked unless the server is bound to loopback (AC-02)");
	}

	[Test]
	[Description("An IPv4 loopback target is permitted when the server is bound to loopback (local-dev) (AC-02).")]
	public void EnsureAllowed_ShouldPermit_WhenTargetIsLoopbackAndBoundHostIsLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: LoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://127.0.0.1:5000/");

		// Assert
		act.Should().NotThrow(
			because: "loopback targets are allowed for local dev when the server binds to loopback (AC-02)");
	}

	[Test]
	[Description("An off-allowlist origin is rejected when an allowlist is configured (AC-03).")]
	public void EnsureAllowed_ShouldReject_WhenOriginIsNotOnAllowlist() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(
			boundHost: NonLoopbackBoundHost, "https://acme.creatio.com");

		// Act
		Action act = () => sut.EnsureAllowed("https://evil.example.com/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "the origin is not on the configured allowlist (AC-03)")
			.Which.Message.Should().Contain("allowlist",
				because: "the rejection names the reason (AC-ERR)");
	}

	[Test]
	[Description("An on-allowlist origin is permitted when an allowlist is configured (AC-03).")]
	public void EnsureAllowed_ShouldPermit_WhenOriginIsOnAllowlist() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(
			boundHost: NonLoopbackBoundHost, "https://acme.creatio.com");

		// Act
		Action act = () => sut.EnsureAllowed("https://acme.creatio.com/0/ServiceModel/");

		// Assert
		act.Should().NotThrow(
			because: "the target origin exactly matches a configured allowlist origin (AC-03)");
	}

	[Test]
	[Description("A url whose scheme differs from the allowlisted origin is rejected (scheme sensitivity) (AC-03).")]
	public void EnsureAllowed_ShouldReject_WhenSchemeDiffersFromAllowlistedOrigin() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(
			boundHost: NonLoopbackBoundHost, "https://acme.creatio.com");

		// Act
		Action act = () => sut.EnsureAllowed("http://acme.creatio.com/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "the allowlist is scheme-sensitive: http is a different origin than https (AC-03)");
	}

	[Test]
	[Description("A url whose port differs from the allowlisted origin is rejected (port sensitivity) (AC-03).")]
	public void EnsureAllowed_ShouldReject_WhenPortDiffersFromAllowlistedOrigin() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(
			boundHost: NonLoopbackBoundHost, "https://acme.creatio.com");

		// Act
		Action act = () => sut.EnsureAllowed("https://acme.creatio.com:8443/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "the allowlist is port-sensitive: :8443 is a different origin than the default :443 (AC-03)");
	}

	[Test]
	[Description("An ordinary Creatio host is permitted when no allowlist is configured and it passes baseline (AC-04).")]
	public void EnsureAllowed_ShouldPermit_WhenAllowlistUnsetAndBaselinePasses() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("https://acme.creatio.com/0/ServiceModel/");

		// Assert
		act.Should().NotThrow(
			because: "with no allowlist, any reachable host passing the baseline blocks is permitted (AC-04)");
	}

	[Test]
	[Description("The baseline blocks apply even when the blocked target is on the allowlist (AC-02 precedence).")]
	public void EnsureAllowed_ShouldReject_WhenBlockedTargetIsAlsoOnAllowlist() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(
			boundHost: NonLoopbackBoundHost, "http://169.254.169.254");

		// Act
		Action act = () => sut.EnsureAllowed("http://169.254.169.254/latest/meta-data/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "baseline address-class blocks always win, regardless of allowlist configuration (AC-02)");
	}

	[Test]
	[Description("A trailing-dot cloud-metadata host (169.254.169.254.) is blocked even though Uri classifies it as a DNS host, not an IP literal (SSRF trailing-dot bypass).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsTrailingDotCloudMetadata() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://169.254.169.254./");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "a single trailing dot must not slip the metadata address past the baseline block (SSRF)");
	}

	[Test]
	[Description("A trailing-dot loopback host (127.0.0.1.) is blocked when the server is NOT bound to loopback, closing the trailing-dot DNS-classification bypass.")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsTrailingDotLoopbackAndBoundHostIsNotLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://127.0.0.1./");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
				because: "a single trailing dot must not slip a loopback target past the baseline block (SSRF)")
			.Which.Message.Should().Contain("loopback",
				because: "the rejection names the blocked address class after the trailing dot is stripped (AC-ERR)");
	}

	[Test]
	[Description("A decimal-encoded loopback integer (http://2130706433/) is blocked because Uri canonicalizes it to the 127.0.0.1 IPv4 literal (SSRF alternative-encoding).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsDecimalEncodedLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://2130706433/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "2130706433 canonicalizes to 127.0.0.1 and must hit the loopback baseline block (SSRF)");
	}

	[Test]
	[Description("A hex-encoded loopback integer (http://0x7f000001/) is blocked because Uri canonicalizes it to the 127.0.0.1 IPv4 literal (SSRF alternative-encoding).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsHexEncodedLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://0x7f000001/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "0x7f000001 canonicalizes to 127.0.0.1 and must hit the loopback baseline block (SSRF)");
	}

	[Test]
	[Description("An octal-encoded loopback host (http://0177.0.0.1/) is blocked because Uri canonicalizes it to the 127.0.0.1 IPv4 literal (SSRF alternative-encoding).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsOctalEncodedLoopback() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://0177.0.0.1/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "0177.0.0.1 canonicalizes to 127.0.0.1 and must hit the loopback baseline block (SSRF)");
	}

	[Test]
	[Description("A decimal-encoded cloud-metadata integer (http://2852039166/) is blocked because Uri canonicalizes it to 169.254.169.254 (SSRF alternative-encoding).")]
	public void EnsureAllowed_ShouldReject_WhenTargetIsDecimalEncodedCloudMetadata() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://2852039166/");

		// Assert
		act.Should().Throw<TargetUrlNotAllowedException>(
			because: "2852039166 canonicalizes to 169.254.169.254 and must hit the cloud-metadata baseline block (SSRF)");
	}

	[Test]
	[Description("An RFC1918 private address (http://10.0.0.5/) is PERMITTED when no allowlist is set — documenting the deliberate v1 residual so a future baseline tightening fails this test.")]
	public void EnsureAllowed_ShouldPermit_WhenTargetIsRfc1918TenAndAllowlistUnset() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://10.0.0.5/");

		// Assert
		act.Should().NotThrow(
			because: "RFC1918 10.0.0.0/8 is intentionally NOT part of the v1 baseline blocks (allowlist is the operator's tool)");
	}

	[Test]
	[Description("An RFC1918 private address (http://192.168.1.10/) is PERMITTED when no allowlist is set — documenting the deliberate v1 residual.")]
	public void EnsureAllowed_ShouldPermit_WhenTargetIsRfc1918OneNineTwoAndAllowlistUnset() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator(boundHost: NonLoopbackBoundHost);

		// Act
		Action act = () => sut.EnsureAllowed("http://192.168.1.10/");

		// Assert
		act.Should().NotThrow(
			because: "RFC1918 192.168.0.0/16 is intentionally NOT part of the v1 baseline blocks (allowlist is the operator's tool)");
	}

	[Test]
	[Description("A rejection message contains only the reason and no unexpected data such as the path or query.")]
	public void EnsureAllowed_ShouldNotLeakUnexpectedData_WhenRejecting() {
		// Arrange
		ITargetUrlValidator sut = CreateValidator();

		// Act
		Action act = () => sut.EnsureAllowed("http://169.254.169.254/latest/meta-data/?token=secret-value");

		// Assert
		TargetUrlNotAllowedException exception = act.Should().Throw<TargetUrlNotAllowedException>(
			because: "the metadata address is blocked (AC-02)").Which;
		exception.Message.Should().NotContain("secret-value",
			because: "the rejection message must not echo caller-supplied query/path data (AC-ERR/FR-11)");
	}
}
