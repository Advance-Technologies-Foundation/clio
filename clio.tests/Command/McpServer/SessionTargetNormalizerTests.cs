using System;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// AC-00 / TC-U-706 (ENG-95262, story 7) — the session-target equivalence table.
/// <para>
/// The rows are GENERATED FROM the binding component table in the credential threat model
/// <c>T-5 — Target normalisation collision</c>, not from cases that happened to occur to the implementer:
/// every case carries the T-5 component it exercises in its label, and every T-5 row that names a fold, a
/// non-fold or a rejection appears here.
/// </para>
/// <para>
/// Both directions are asserted, and the NEAR-MISS direction is the more important half: it is the one
/// that catches OVER-normalisation. Merging two targets is not a cache miss on a sticky worker — it is a
/// credential crossover, one caller's credentials carried to another caller's target. Under-normalising
/// only costs another worker (0.7 s).
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SessionTargetNormalizerTests {

	private ISessionTargetNormalizer _normalizer;

	[SetUp]
	public void SetUp() => _normalizer = new SessionTargetNormalizer();

	// ---------------------------------------------------------------------------------------------
	// T-5 rows marked "folded": two spellings that name ONE target and must share ONE identity.
	// ---------------------------------------------------------------------------------------------
	private static readonly object[] EquivalentPairs = [
		new object[] { "Scheme — lowercase", "HTTP://example.com", "http://example.com" },
		new object[] { "Scheme — mixed case", "HttPs://example.com", "https://example.com" },
		new object[] { "Host, ASCII — lowercase", "https://Example.COM", "https://example.com" },
		new object[] { "Host, ASCII — lowercase with subdomain", "https://Dev.Example.COM/App", "https://dev.example.com/App" },
		new object[] { "Host, non-ASCII — IDNA 2008 A-label", "https://münchen.de", "https://xn--mnchen-3ya.de" },
		new object[] { "Host, non-ASCII — A-label after case fold", "https://MÜNCHEN.DE", "https://xn--mnchen-3ya.de" },
		new object[] { "Host, IPv6 literal — RFC 5952 zero-run compression", "http://[2001:0db8:0000:0000:0000:0000:0000:0001]", "http://[2001:db8::1]" },
		new object[] { "Host, IPv6 literal — RFC 5952 lowercase hex", "http://[2001:DB8::1]", "http://[2001:db8::1]" },
		new object[] { "Host, IPv6 literal — RFC 5952 loopback", "http://[0:0:0:0:0:0:0:1]", "http://[::1]" },
		new object[] { "Port — http default elided", "http://example.com:80", "http://example.com" },
		new object[] { "Port — https default elided", "https://example.com:443", "https://example.com" },
		new object[] { "Path — one trailing slash stripped (root)", "https://example.com/", "https://example.com" },
		new object[] { "Path — one trailing slash stripped (base path)", "https://example.com/app/", "https://example.com/app" },
		new object[] { "Path — '..' dot segments resolved", "https://example.com/a/b/../c", "https://example.com/a/c" },
		new object[] { "Path — '.' dot segments resolved", "https://example.com/a/./b", "https://example.com/a/b" },
		new object[] { "Path — '..' at the root is discarded (RFC 3986 §5.2.4), never escapes below it", "https://example.com/../etc", "https://example.com/etc" },
		new object[] { "Path — repeated '..' past the root still yields a root-anchored path", "https://example.com/a/../../b", "https://example.com/b" },
		new object[] { "Path — traversal back to the origin root folds onto the bare origin", "https://example.com/app/..", "https://example.com" },
		new object[] { "Path — a percent-encoded dot segment is decoded FIRST, then resolved (RFC 3986 §6.2.2)", "https://example.com/a/%2E%2E/b", "https://example.com/b" },
		new object[] { "Path — percent-encoding hex case normalised", "https://example.com/a%2fb", "https://example.com/a%2Fb" },
		new object[] { "Path — unreserved octet decoded (RFC 3986 §6.2.2.2)", "https://example.com/%7Euser", "https://example.com/~user" },
		new object[] { "Path — unreserved alphanumeric decoded", "https://example.com/%41pp", "https://example.com/App" },
		new object[] { "Combined folds — every folded component at once", "HTTPS://Example.COM:443/app/./sub/../", "https://example.com/app" }
	];

	// ---------------------------------------------------------------------------------------------
	// T-5 rows marked "not folded", plus the negative side of each fold: pairs that must stay DISTINCT.
	// This is the direction that catches over-normalisation.
	// ---------------------------------------------------------------------------------------------
	private static readonly object[] NearMissPairs = [
		new object[] { "Scheme value — http and https are different targets", "http://example.com", "https://example.com" },
		new object[] { "Scheme value — downgrade is a different security context, ports elided both sides", "http://example.com:80", "https://example.com:443" },
		new object[] { "Host vs IP — hostname and address are different targets", "http://localhost", "http://127.0.0.1" },
		new object[] { "Host vs IP — a resolved address is neither stable nor authenticated", "https://example.com", "https://93.184.216.34" },
		new object[] { "Host — different registered names", "https://a.example.com", "https://b.example.com" },
		new object[] { "Host — a subdomain is a different target", "https://a.example.com", "https://example.com" },
		new object[] { "Host, non-ASCII — different IDN names must not converge", "https://münchen.de", "https://muenchen.de" },
		new object[] { "Host, IPv6 literal — different addresses", "http://[::1]", "http://[::2]" },
		new object[] { "Host, IPv6 literal vs IPv4 literal", "http://[::1]", "http://127.0.0.1" },
		new object[] { "Port, non-default — exact match", "https://example.com:8443", "https://example.com:9443" },
		new object[] { "Port, non-default — a non-default port is not the no-port target", "https://example.com:8443", "https://example.com" },
		new object[] { "Port — only the SCHEME default is elided", "http://example.com:443", "http://example.com" },
		new object[] { "Path, case — Creatio paths are case-sensitive", "https://example.com/App", "https://example.com/app" },
		new object[] { "Path — exactly ONE trailing slash is stripped", "https://example.com/app//", "https://example.com/app" },
		new object[] { "Path — a percent-encoded reserved octet is not decoded", "https://example.com/a%2Fb", "https://example.com/a/b" },
		new object[] { "Path — a resolved '..' is not the un-traversed path", "https://example.com/a/../b", "https://example.com/a/b" },
		new object[] { "Path — different base paths", "https://example.com/alpha", "https://example.com/beta" },
		new object[] { "Path — a base path is not the bare origin", "https://example.com/app", "https://example.com" }
	];

	// ---------------------------------------------------------------------------------------------
	// T-5 rows marked "rejected": the call fails with an explicit error, never a silent fallback to a
	// looser key.
	// ---------------------------------------------------------------------------------------------
	private static readonly object[] RejectedTargets = [
		new object[] { "Userinfo — user:password@", "https://admin:s3cr3t@example.com" },
		new object[] { "Userinfo — user@ with no password", "https://admin@example.com" },
		new object[] { "Userinfo — present alongside a port and a path", "http://admin:s3cr3t@example.com:8080/app" },
		new object[] { "Query — a target is an origin plus base path, never a query", "https://example.com/app?tenant=b" },
		new object[] { "Query — empty query string", "https://example.com/?" },
		new object[] { "Fragment", "https://example.com/app#section" },
		new object[] { "Host, IPv4 literal — octal form", "http://0177.0.0.1" },
		new object[] { "Host, IPv4 literal — decimal-integer form", "http://2130706433" },
		new object[] { "Host, IPv4 literal — 0x form", "http://0x7f.0.0.1" },
		new object[] { "Host, IPv4 literal — short (class-A) form", "http://127.1" },
		new object[] { "Host, IPv4 literal — padded octet", "http://192.168.001.1" }
	];

	[TestCaseSource(nameof(EquivalentPairs))]
	[Description("T-5 folded rows: two spellings of one target produce one identity, so a registered name and an explicit URI cannot split the tenant-keyed registries.")]
	public void Normalize_ShouldProduceOneIdentity_WhenTwoSpellingsNameOneTarget(
		string t5Row, string left, string right) {
		// Arrange — the pair comes straight from the T-5 component table row named in t5Row.

		// Act
		string normalizedLeft = _normalizer.Normalize(left);
		string normalizedRight = _normalizer.Normalize(right);

		// Assert
		normalizedLeft.Should().Be(normalizedRight,
			because: $"T-5 folds this component [{t5Row}]: '{left}' and '{right}' name one target, so they "
				+ $"must share one session key (got '{normalizedLeft}' and '{normalizedRight}')");
	}

	[TestCaseSource(nameof(NearMissPairs))]
	[Description("T-5 non-folded rows: two near-miss targets keep distinct identities, because merging them on a sticky worker is a credential crossover rather than a cache miss.")]
	public void Normalize_ShouldProduceDistinctIdentities_WhenTargetsAreNotProvablyTheSame(
		string t5Row, string left, string right) {
		// Arrange — the pair comes straight from the T-5 component table row named in t5Row.

		// Act
		string normalizedLeft = _normalizer.Normalize(left);
		string normalizedRight = _normalizer.Normalize(right);

		// Assert
		normalizedLeft.Should().NotBe(normalizedRight,
			because: $"T-5 does NOT fold this component [{t5Row}]: '{left}' and '{right}' are two targets, "
				+ $"and merging them would carry one caller's credentials to the other's target "
				+ $"(both normalised to '{normalizedLeft}')");
	}

	[TestCaseSource(nameof(RejectedTargets))]
	[Description("T-5 rejected rows: userinfo, a query, a fragment and a non-canonical IPv4 literal fail the call with an explicit error instead of silently falling back to a looser key.")]
	public void Normalize_ShouldThrow_WhenTargetCarriesAComponentT5Rejects(string t5Row, string target) {
		// Arrange
		Action normalize = () => _normalizer.Normalize(target);

		// Act / Assert
		normalize.Should().Throw<EnvironmentResolutionException>(
			because: $"T-5 rejects this component [{t5Row}] rather than normalising it, and rejection means "
				+ "the call fails closed — a silent fallback to a looser key is exactly the merge the "
				+ "rejection exists to prevent");
	}

	[Test]
	[Description("A rejected target never echoes its own value back, so a password carried in userinfo cannot reach a log, an error envelope or a test snapshot (T-6).")]
	public void Normalize_ShouldNotEchoTheTargetValue_WhenRejectingUserinfo() {
		// Arrange
		const string secret = "s3cr3t-passphrase";
		Action normalize = () => _normalizer.Normalize($"https://admin:{secret}@example.com");

		// Act
		EnvironmentResolutionException rejection = normalize.Should()
			.Throw<EnvironmentResolutionException>(because: "userinfo in a target is rejected").Which;

		// Assert
		rejection.Message.Should().NotContain(secret,
			because: "the rejection names the reason, never the offending value — a credential must not "
				+ "travel into a diagnostic");
	}

	// The two host folds whose implementation touches a platform-backed or address-family-aware API are
	// pinned to EXPLICIT expected strings rather than to a round-trip, so a macOS/Linux/Windows difference
	// in IDNA or IPv6 formatting fails the test instead of silently agreeing with itself.
	[TestCase("https://MÜNCHEN.de", "https://xn--mnchen-3ya.de",
		TestName = "Normalize_ShouldEmitTheIdnaALabel_WhenHostIsNonAscii")]
	[TestCase("http://[2001:0DB8:0000:0000:0000:0000:0000:0001]:8080/",
		"http://[2001:db8::1]:8080",
		TestName = "Normalize_ShouldEmitTheRfc5952Form_WhenHostIsAnIPv6Literal")]
	[TestCase("HTTPS://Example.COM:443/app/./sub/../%7Euser/", "https://example.com/app/~user",
		TestName = "Normalize_ShouldEmitTheCanonicalIdentity_WhenEveryFoldedComponentIsPresent")]
	[Description("The canonical identity is an exact, platform-independent string — IDNA A-labels, RFC 5952 IPv6 forms and the combined fold are pinned rather than round-tripped.")]
	public void Normalize_ShouldEmitTheExpectedCanonicalIdentity(string target, string expected) {
		// Arrange — expected is written out in full so a platform-specific formatting difference fails here.

		// Act
		string normalized = _normalizer.Normalize(target);

		// Assert
		normalized.Should().Be(expected,
			because: "the canonical identity must be byte-identical on macOS, Linux and Windows — a worker "
				+ "keyed differently per platform is a different bug on every host");
	}

	[Test]
	[Description("An input that is not an absolute hierarchical URI is carried byte-exact behind an opaque marker: strictly more distinguishing than the input, so the safety valve can cost an extra worker but can never merge two targets.")]
	public void Normalize_ShouldStayDistinguishing_WhenTargetIsNotAnAbsoluteUri() {
		// Arrange
		const string first = "not-a-uri";
		const string second = "not-a-uri-either";

		// Act
		string normalizedFirst = _normalizer.Normalize(first);
		string normalizedSecond = _normalizer.Normalize(second);

		// Assert
		normalizedFirst.Should().NotBe(normalizedSecond,
			because: "an undecomposable target is never folded onto another one");
		normalizedFirst.Should().NotBe(_normalizer.Normalize("http://not-a-uri"),
			because: "the opaque marker keeps an undecomposable value out of the normalised identity space");
	}

	[Test]
	[Description("A canonical dotted-quad IPv4 literal is accepted unchanged — only the non-canonical spellings T-5 names are rejected.")]
	public void Normalize_ShouldAcceptTheLiteral_WhenIPv4IsCanonicalDottedQuad() {
		// Arrange
		const string target = "http://127.0.0.1:8080/app";

		// Act
		string normalized = _normalizer.Normalize(target);

		// Assert
		normalized.Should().Be("http://127.0.0.1:8080/app",
			because: "the dotted-quad form is the one canonical IPv4 spelling and must resolve, not fail");
	}

	[Test]
	[Description("A registered name whose last label is not numeric is never mistaken for an IPv4 literal, so an all-hex-looking hostname keeps resolving instead of being rejected.")]
	public void Normalize_ShouldTreatHostAsARegisteredName_WhenLastLabelIsNotNumeric() {
		// Arrange
		const string target = "http://face.beef/app";

		// Act
		string normalized = _normalizer.Normalize(target);

		// Assert
		normalized.Should().Be("http://face.beef/app",
			because: "the IPv4 rejection keys off a numeric or 0x-prefixed LAST label, so an ordinary "
				+ "hostname made of hex characters is still a hostname");
	}
}
