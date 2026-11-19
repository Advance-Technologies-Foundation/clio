using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Requirement (c) of the site-text redaction plan: the redaction fixture holds invented values only. Its header
/// says so, and this scan fails on anything shaped like a real secret or a real place.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextRedactionFixtureSafetyTests {

	// The placeholders the host lane's cases use that a shape check would otherwise flag, each one invented or a
	// published protocol example. A new value may join this list only when it is as obviously not real.
	private static readonly string[] AllowedPlaceholders = [
		// A JWT whose payload is {"sub":"x"}: the host lane's stub.
		"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
		// Descending hex and a dash-less GUID stub.
		"3f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a",
		"3f2a9c1b7d4e4a5b9c8d0e1f2a3b4c5d",
		// base64 of "ABCDEFGHIJKLMNOPQRSTUVWXYZ4242".
		"QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo0MjQy+/==",
		// An NTLM type-1 (negotiate) message: no credential in it.
		"TlRMTVNTUAABAAAAB4IIogAAAAAAAAAAAAAAAAAAAAAGAbEdAAAADw==",
		// The nonce and response of RFC 2617's own Digest example.
		"dcd98b7102dd2f0e8b11d0f600bfb0c093",
		"6629fae49393a05397450978507c4ef1",
		// Keyboard-walk base64url stubs.
		"Xk3_9fJ2-pQ_aB7cD_eF1gH2iJ3kL4mN5oP6qR7sT8",
		"q8Zr_3kLmN0pQ7sT_vW2xY4zA6bC8dE0fG1hI3jK5lM",
		// Address-shaped placeholders on no real domain.
		"jane@x",
		"admin@localhost",
		"p@ss"
	];

	// Domains under which an invented host or address may live: the reserved names of RFC 2606 and RFC 6761, and
	// the private-use labels a site's own refusal names.
	private static readonly Regex AllowedDomain = new(
		@"(?:^|\.)(?:example(?:\.com|\.org|\.net)?|invalid|test|localhost|local|internal)$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	// Top-level domains a real host is most often under; a dotted lower-case name ending in one is a real place.
	private static readonly Regex RealSuffixHost = new(
		@"\b(?:[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?\.)+(?:com|net|org|io|ai|dev|app|cloud|co|info|biz|me|tech|online|site|"
		+ @"xyz|uk|de|fr|ua|ru|eu|us|nl|pl|it|es|ca|au|ch|se|no|jp|cn|br|in)\b",
		RegexOptions.CultureInvariant);

	private static readonly (string Shape, Regex Pattern)[] SecretShapes = [
		("a JWT with a real-length signature", new Regex(@"eyJ[A-Za-z0-9_\-]+\.eyJ[A-Za-z0-9_\-]*\.[A-Za-z0-9_\-]{16,}")),
		// With a digit or a base64 symbol in it, so a long CamelCase name is not read as one.
		("a base64 run of 32 characters or more",
			new Regex(@"(?<![A-Za-z0-9+/])(?=[A-Za-z+/]*[0-9+/])[A-Za-z0-9+/]{32,}={0,2}")),
		// A dashed GUID is a record id, not a key.
		("a base64url run of 32 or more with digits", new Regex(
			@"(?<![A-Za-z0-9_\-])(?![0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}(?![A-Za-z0-9_\-]))"
			+ @"(?=[A-Za-z0-9_\-]*[_\-])(?=(?:[A-Za-z_\-]*\d){8})[A-Za-z0-9_\-]{32,}")),
		("an AWS access key id", new Regex(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA)[0-9A-Z]{16}\b")),
		("a GitHub token", new Regex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})")),
		("an Azure storage or SAS secret", new Regex(@"(?i)\b(?:AccountKey|SharedAccessKey|SharedAccessSignature)=|[?&]sig=[A-Za-z0-9%]{20,}")),
		("a Slack, Stripe or Google key", new Regex(@"\b(?:xox[abprs]-[A-Za-z0-9\-]{10,}|[sr]k_live_[A-Za-z0-9]{10,}|sk_test_[A-Za-z0-9]{24,}|AIza[0-9A-Za-z_\-]{35})")),
		("a private key block", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")),
	];

	private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9](?:[A-Za-z0-9.\-]*[A-Za-z0-9])?");
	private static readonly Regex UriHost = new(@"[A-Za-z][A-Za-z0-9+.\-]*://(?:[^@/\s""'<>]*@)?(?<host>[A-Za-z0-9.\-]+)");

	[Test]
	[Category("Unit")]
	[Description("The fixture's header states that every value is invented and that no real host, login, password, "
		+ "key or token may ever be added.")]
	public void Fixture_ShouldStateThatEveryValueIsInvented() {
		// Arrange
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(SiteTextRedactionFixtureTests.FixturePath));

		// Act
		string notice = document.RootElement.GetProperty("header").GetProperty("notice").GetString();

		// Assert
		notice.Should().Contain("EVERY VALUE IN THIS FILE IS INVENTED")
			.And.Contain("No real host, login, password, key or token may ever be added",
				because: "a reader adding a row must see the rule before the rows");
	}

	[Test]
	[Category("Unit")]
	[Description("No string in the fixture is shaped like a real secret - a real-length JWT, a long base64 or base64url "
		+ "run, an AWS, GitHub, Azure, Slack, Stripe or Google key, a private key - except the listed placeholders.")]
	public void Fixture_ShouldHoldNoRealSecretShapes() {
		// Arrange
		List<string> texts = FixtureStrings();

		// Act
		List<string> findings = [];
		foreach (string text in texts.Select(WithoutPlaceholders)) {
			foreach ((string shape, Regex pattern) in SecretShapes) {
				findings.AddRange(pattern.Matches(text).Where(match => !IsLowVariety(match.Value))
					.Select(match => $"{shape}: {match.Value}"));
			}
		}

		// Assert
		findings.Distinct().Should().BeEmpty(
			because: "the fixture may only hold invented values; add a placeholder to AllowedPlaceholders only if it "
				+ "is as obviously not real as the ones there");
	}

	[Test]
	[Category("Unit")]
	[Description("No host in the fixture is under a real public suffix, and no e-mail address is outside example.com "
		+ "or the reserved names, except the listed placeholders.")]
	public void Fixture_ShouldHoldNoRealPlace() {
		// Arrange
		List<string> texts = FixtureStrings();

		// Act
		List<string> findings = [];
		foreach (string text in texts.Select(WithoutPlaceholders)) {
			findings.AddRange(UriHost.Matches(text).Select(match => match.Groups["host"].Value)
				.Where(host => host.Contains('.') && !AllowedDomain.IsMatch(host))
				.Select(host => $"a URI host: {host}"));
			findings.AddRange(Email.Matches(text).Select(match => match.Value)
				.Where(address => !AllowedDomain.IsMatch(address[(address.IndexOf('@') + 1)..]))
				.Select(address => $"an e-mail address: {address}"));
			findings.AddRange(RealSuffixHost.Matches(text).Select(match => match.Value)
				.Where(host => !AllowedDomain.IsMatch(host))
				.Select(host => $"a host under a real suffix: {host}"));
		}

		// Assert
		findings.Distinct().Should().BeEmpty(
			because: "an invented host belongs under .example, .invalid or .test, and an address under example.com");
	}

	[TestCase("token AKIAABCDEFGHIJKLMNOP", TestName = "Scan_FlagsAnAwsKeyId")]
	[TestCase("ghp_abcdefghijklmnopqrstuvwxyz0123456789", TestName = "Scan_FlagsAGitHubToken")]
	[TestCase("Key Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFy", TestName = "Scan_FlagsALongBase64Run")]
	[Category("Unit")]
	[Description("The scan itself catches the shapes it names, so an empty finding list means something.")]
	public void SecretShapes_ShouldFlagARealLookingValue(string text) {
		// Arrange

		// Act
		bool flagged = SecretShapes.Any(shape => shape.Pattern.IsMatch(text));

		// Assert
		flagged.Should().BeTrue(because: "a value of this shape must never pass the fixture scan");
	}

	[TestCase("https://tenant.creatio.com/0/odata", TestName = "Scan_FlagsARealUriHost")]
	[TestCase("jane.doe@contoso.com", TestName = "Scan_FlagsARealEmailDomain")]
	[Category("Unit")]
	[Description("The place scan catches a real host and a real e-mail domain.")]
	public void PlaceScan_ShouldFlagARealPlace(string text) {
		// Arrange

		// Act
		bool flagged = RealSuffixHost.IsMatch(text)
			|| Email.Matches(text).Any(match => !AllowedDomain.IsMatch(match.Value[(match.Value.IndexOf('@') + 1)..]));

		// Assert
		flagged.Should().BeTrue(because: "a real place must never pass the fixture scan");
	}

	private static List<string> FixtureStrings() {
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(SiteTextRedactionFixtureTests.FixturePath));
		List<string> texts = [];
		Collect(document.RootElement, texts);
		return texts;
	}

	private static void Collect(JsonElement element, List<string> texts) {
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject()) {
					Collect(property.Value, texts);
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray()) {
					Collect(item, texts);
				}
				break;
			case JsonValueKind.String:
				texts.Add(element.GetString());
				break;
		}
	}

	private static string WithoutPlaceholders(string text) =>
		AllowedPlaceholders.Aggregate(text, (current, placeholder) => current.Replace(placeholder, " ", StringComparison.Ordinal));

	// "aaaa…", "1111…", "a1a1…": a repetition, not a secret.
	private static bool IsLowVariety(string value) => value.Distinct().Count() <= 8;
}
