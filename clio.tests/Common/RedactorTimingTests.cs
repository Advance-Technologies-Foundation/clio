using System;
using System.Diagnostics;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The entry points at about 1 MB stay within a time close to master's. Measured on 20 Nov at 1.2 MB, master's Redact
/// took 0.4-0.65 s and its fenced rendering 1-3 ms; this build's Redact 0.5-1.5 s, RedactSiteText 0.5-2.1 s and both
/// fenced renderings at most 7 ms. Before B2 the edit function compared every edit with every placeholder, and
/// "password=Basic X7q2 rejected; " repeated to 1.2 MB took 12.9 s (Redact) while the fenced rendering, with master's
/// 2,400 bound gone, took 3.6 s at 600 KB.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactorTimingTests {

	private const int Length = 1_000_000;

	private static string Repeat(string unit) {
		StringBuilder text = new(Length + unit.Length);
		while (text.Length < Length) {
			text.Append(unit);
		}
		return text.ToString(0, Length);
	}

	private static readonly string EscapedQuote = "\\" + "u0022";

	private static readonly object[] Shapes = [
		new object[] { "pairs", "password=Basic X7q2 rejected; " },
		new object[] { "paths", "see C:\\x\\y (a) [b] {c} ..\\z\\w.config and /home/jdoe/x; " },
		new object[] { "escaped-json", "{" + EscapedQuote + "password" + EscapedQuote + ":" + EscapedQuote + "hunter2" + EscapedQuote + ","
			+ EscapedQuote + "x" + EscapedQuote + ":" + EscapedQuote + "\\\\\\\\SRV01\\\\share" + EscapedQuote + "} " },
		new object[] { "site-page", "Login failed for user 'zq_login'. Data Source=.\\SQLEXPRESS;Initial Catalog=ZqDb <div>(a)</div> " }
	];

	private static (string Output, TimeSpan Elapsed) Time(Func<string, string> entry, string text) {
		entry("warm up");
		Stopwatch watch = Stopwatch.StartNew();
		string output = entry(text);
		return (output, watch.Elapsed);
	}

	[TestCaseSource(nameof(Shapes))]
	[Category("Unit")]
	[Description("Redact and RedactSiteText over 1 MB finish within 3 s each (master: about 0.5 s), and do not fail closed "
		+ "on the whole-call budget. The quadratic edit function took 12.9 s on the first shape.")]
	public void RedactAndRedactSiteText_ShouldStayNearMastersTime_At1MB(string shape, string unit) {
		// Arrange
		string text = Repeat(unit);

		// Act
		(string redact, TimeSpan redactTime) = Time(SensitiveErrorTextRedactor.Redact, text);
		(string site, TimeSpan siteTime) = Time(SensitiveErrorTextRedactor.RedactSiteText, text);

		// Assert
		redact.Should().NotBe("[redacted]", because: $"Redact of the {shape} shape must finish, not fail closed");
		site.Should().NotBe("[redacted]", because: $"RedactSiteText of the {shape} shape must finish, not fail closed");
		redactTime.Should().BeLessThan(TimeSpan.FromSeconds(3), because: $"Redact of 1 MB ({shape}) stays near master's time");
		siteTime.Should().BeLessThan(TimeSpan.FromSeconds(3), because: $"RedactSiteText of 1 MB ({shape}) stays near master's time");
	}

	[TestCaseSource(nameof(Shapes))]
	[Category("Unit")]
	[Description("Both fenced renderings over 1 MB finish within 250 ms (master: 1-3 ms): they cut their input at "
		+ "master's 2,400 before the chain, as master did.")]
	public void FencedRenderings_ShouldStayNearMastersTime_At1MB(string shape, string unit) {
		// Arrange
		string text = Repeat(unit);

		// Act
		(string fenced, TimeSpan fencedTime) = Time(value => SensitiveErrorTextRedactor.RedactUntrustedOrNull(value), text);
		(string site, TimeSpan siteTime) = Time(value => SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(value), text);

		// Assert
		fenced.Should().NotBeNull();
		site.Should().NotBeNull();
		fencedTime.Should().BeLessThan(TimeSpan.FromMilliseconds(250), because: $"the fenced rendering of 1 MB ({shape}) is bounded as master's was");
		siteTime.Should().BeLessThan(TimeSpan.FromMilliseconds(250), because: $"the fenced site rendering of 1 MB ({shape}) is bounded as master's was");
	}
}
