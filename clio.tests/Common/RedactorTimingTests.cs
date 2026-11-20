using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The entry points are linear, and the fenced renderings are bounded, proven WITHOUT THE CLOCK: the redactor counts its
/// own work (<see cref="SensitiveErrorTextRedactor.Work"/>: every character a rule or a step of master's chain scans,
/// every match, edit, placeholder found or examined, piece kept), and the tests compare the work of an input with the
/// work of one twice as long. A shared CI runner cannot make that flake. One coarse clock check stays.
/// </summary>
/// <remarks>
/// Measured on 20 Nov at 1.2 MB: master's Redact 0.4-0.7 s and its fenced rendering 1-3 ms; this build's Redact
/// 0.5-1.6 s, RedactSiteText up to 2.2 s (JSON-escaped credentials), the fenced renderings at most 7 ms. Before B2 the
/// edit function compared every edit with every placeholder (12.9 s on the first shape), and the fenced rendering, with
/// master's 2,400 bound gone, took 3.6 s at 600 KB.
/// </remarks>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactorTimingTests {

	private static string Repeat(string unit, int length) {
		StringBuilder text = new(length + unit.Length);
		while (text.Length < length) {
			text.Append(unit);
		}
		return text.ToString(0, length);
	}

	private static readonly string EscapedQuote = "\\" + "u0022";

	internal static readonly string[] ShapeNames = ["pairs", "paths", "urls-emails", "escaped-json", "site-page", "brackets",
		"placeholders", "pair-dense", "escapes"];

	internal static string Shape(string name) => name switch {
		"pairs" => "password=Basic X7q2 rejected; ",
		"paths" => "see C:\\x\\y (a) [b] {c} ..\\z\\w.config and /home/jdoe/x; ",
		"urls-emails" => "GET https://zq.example/a?b=1 from jdoe@zq.example at db01.internal:1433, ",
		"escaped-json" => "{" + EscapedQuote + "password" + EscapedQuote + ":" + EscapedQuote + "hunter2" + EscapedQuote + ","
			+ EscapedQuote + "x" + EscapedQuote + ":" + EscapedQuote + "\\\\\\\\SRV01\\\\share" + EscapedQuote + "} ",
		"site-page" => "Login failed for user 'zq_login'. Data Source=.\\SQLEXPRESS;Initial Catalog=ZqDb <div>(a)</div> ",
		"brackets" => "([{a}])[b](c){d}",
		"placeholders" => "[redacted] [redacted-uri]x [redacted-path]/y ",
		"pair-dense" => "a=1 b=2 password=x token=y ",
		_ => "\\n\\t\\r" + EscapedQuote + "\\\\\\\""
	};

	private static readonly (string Name, Func<string, string> Run)[] Entries = [
		("Redact", SensitiveErrorTextRedactor.Redact),
		("RedactSiteText", SensitiveErrorTextRedactor.RedactSiteText),
		("RedactCredentials", SensitiveErrorTextRedactor.RedactCredentials),
		("RedactJsonCredentialProperties", SensitiveErrorTextRedactor.RedactJsonCredentialProperties)
	];

	// The work one call does, and whether it failed closed.
	private static (long Work, bool FailedClosed, string Output) Measure(Func<string, string> entry, string text) {
		long work = SensitiveErrorTextRedactor.Work;
		long failed = SensitiveErrorTextRedactor.FailedClosed;
		string output = entry(text);
		return (SensitiveErrorTextRedactor.Work - work, SensitiveErrorTextRedactor.FailedClosed != failed, output);
	}

	[TestCaseSource(nameof(ShapeNames))]
	[Category("Unit")]
	[Description("LINEARITY without the clock: for every entry point, the work of a 200,000-character input is at most 2.2 "
		+ "times the work of a 100,000-character one of the same shape, and neither fails closed. A quadratic step - the edit "
		+ "function comparing every edit with every placeholder - doubles its share of the work twice over.")]
	public void EveryEntryPoint_ShouldDoLinearWork(string shape) {
		// Arrange
		string single = Repeat(Shape(shape), 100_000);
		string twice = Repeat(Shape(shape), 200_000);

		// Act
		var measured = Entries.Select(entry => (entry.Name, Single: Measure(entry.Run, single), Twice: Measure(entry.Run, twice))).ToList();
		TestContext.Out.WriteLine(string.Join("; ", measured.Select(entry =>
			$"{entry.Name} {entry.Single.Work} -> {entry.Twice.Work} ({(double)entry.Twice.Work / entry.Single.Work:0.000})")));

		// Assert
		foreach (var entry in measured) {
			entry.Single.FailedClosed.Should().BeFalse(because: $"{entry.Name} of 100,000 characters ({shape}) must finish");
			entry.Twice.FailedClosed.Should().BeFalse(because: $"{entry.Name} of 200,000 characters ({shape}) must finish");
			((double)entry.Twice.Work).Should().BeLessThanOrEqualTo(2.2 * entry.Single.Work,
				because: $"{entry.Name}'s work on the {shape} shape must grow linearly");
		}
	}

	[TestCaseSource(nameof(ShapeNames))]
	[Category("Unit")]
	[Description("THE FENCE BOUND without the clock: both fenced renderings do exactly the same work for 1 MB as for 10 KB of "
		+ "the same shape, because they cut their input at master's 2,400 before the chain.")]
	public void FencedRenderings_ShouldDoTheSameWork_ForAnyInputPastTheirBound(string shape) {
		// Arrange
		string small = Repeat(Shape(shape), 10_000);
		string large = Repeat(Shape(shape), 1_000_000);
		Func<string, string> fenced = text => SensitiveErrorTextRedactor.RedactUntrustedOrNull(text);
		Func<string, string> site = text => SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);

		// Act
		long fencedSmall = Measure(fenced, small).Work;
		long fencedLarge = Measure(fenced, large).Work;
		long siteSmall = Measure(site, small).Work;
		long siteLarge = Measure(site, large).Work;

		// Assert
		fencedLarge.Should().Be(fencedSmall, because: "the fenced rendering reads at most master's 2,400 characters");
		siteLarge.Should().Be(siteSmall, because: "the fenced site rendering reads at most master's 2,400 characters");
	}

	[TestCase("pair-dense")]
	[TestCase("escapes")]
	[Category("Unit")]
	[Description("THE WORK BUDGET is deterministic: a 5 MB legitimate input is redacted normally by Redact and "
		+ "RedactSiteText - not failed closed - whatever the machine or its load, and its work stays well inside the budget "
		+ "(WorkPerCharacter units per character). The two shapes are the one with the most work per character and the one whose 5 MB had failed "
		+ "closed on a regex timeout before long inputs got a length-scaled match timeout.")]
	public void A5MBInput_ShouldBeRedactedNormally_WhateverTheLoad(string shape) {
		// Arrange
		string text = Repeat(Shape(shape), 5_000_000);

		// Act
		(long redactWork, bool redactFailed, string redact) = Measure(SensitiveErrorTextRedactor.Redact, text);
		(long siteWork, bool siteFailed, string site) = Measure(SensitiveErrorTextRedactor.RedactSiteText, text);

		// Assert
		redactFailed.Should().BeFalse(because: "a 5 MB answer is redacted, not failed closed");
		siteFailed.Should().BeFalse(because: "a 5 MB answer is redacted, not failed closed");
		redact.Should().NotContain(SensitiveErrorTextRedactor.RedactedTimeout);
		site.Should().NotContain(SensitiveErrorTextRedactor.RedactedTimeout);
		siteWork.Should().BeLessThan(SensitiveErrorTextRedactor.WorkPerCharacter * text.Length / 5,
			because: "the budget keeps a wide margin over the work a legitimate shape does");
		redactWork.Should().BeLessThan(SensitiveErrorTextRedactor.WorkPerCharacter * text.Length / 5);
	}

	[Test]
	[NonParallelizable]
	[Category("Unit")]
	[Description("The one coarse clock check: RedactSiteText of 1 MB of the slowest shape (JSON-escaped credentials), "
		+ "warmed up with the same shape, finishes within 8 s and does not fail closed. Linearity itself is proven by the "
		+ "work counter, not here.")]
	public void RedactSiteText_ShouldFinish1MBOfTheSlowestShape_WellWithinTheClock() {
		// Arrange
		string text = Repeat(Shape("escaped-json"), 1_000_000);
		SensitiveErrorTextRedactor.RedactSiteText(Repeat(Shape("escaped-json"), 100_000));
		Stopwatch watch = Stopwatch.StartNew();

		// Act
		(long _, bool failed, string output) = Measure(SensitiveErrorTextRedactor.RedactSiteText, text);
		watch.Stop();

		// Assert
		failed.Should().BeFalse();
		output.Should().NotContain(SensitiveErrorTextRedactor.RedactedTimeout);
		watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
	}
}
