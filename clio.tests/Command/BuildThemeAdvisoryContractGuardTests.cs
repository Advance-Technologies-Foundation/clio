namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Clio.Command.McpServer;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Pins the advisory non-redaction contract's backstop through the public
/// <see cref="BuildThemeCommand.TryBuildTheme(BuildThemeOptions, out string, out string, out IReadOnlyList{string}, out string)"/>
/// path — no test seam. A font family that satisfies the <see cref="Clio.Theming.FontFamilyName"/> grammar can
/// still carry a token the sensitive-text redactor rewrites: "Bearer Sans" is grammar-valid, yet the redactor's
/// Bearer-token pattern rewrites both Google-Fonts advisories that interpolate it, so the guard's containment
/// (in-place redaction, companion advisory, logger echo, debug fail-fast) is reachable end-to-end. Every test
/// runs with the trace listeners swapped for a recording one (see <see cref="Setup"/>) so the guard's
/// <c>Debug.Fail</c> is captured instead of fail-fasting the test host; the fixture is
/// <see cref="NonParallelizableAttribute"/> because that swap is process-global state, and it sits apart from
/// <see cref="BuildThemeCommandTests"/> so the swap does not serialize that whole fixture.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Command")]
public class BuildThemeAdvisoryContractGuardTests : BaseClioModuleTests {

	/// <summary>
	/// Snapshot of the command's private companion advisory. Hardcoded on purpose: the literal is an
	/// MCP-caller-visible contract, so a production rewording must surface here as a conscious change.
	/// </summary>
	private const string RedactionContractAdvisory =
		"build-theme: an advisory violated the non-redaction contract and was replaced with its redacted form"
		+ "; report this as a clio defect.";

	/// <summary>
	/// The NotInCatalog advisory for a redactor-clean family, byte-for-byte as CollectWarnings emits it —
	/// the pass-through baseline a violating sibling must not disturb.
	/// </summary>
	private const string CompliantVerdanaAdvisory =
		"build-theme: \"Verdana\" was not found in Google Fonts — names are case-sensitive "
		+ "(\"Roboto\" resolves where \"roboto\" does not), and families are sometimes renamed (search "
		+ "fonts.google.com for the current name). No web-font import was added: the theme shows "
		+ "\"Verdana\" only where it is installed locally; everywhere else the text falls back to "
		+ "a generic face. Pick a Google font and restyle if that is not acceptable.";

	private const string FontWeightsAdvisory =
		"build-theme: font weights were ignored — they apply only to a custom heading or body font.";

	private sealed class RecordingTraceListener : TraceListener {

		public List<string> Failures { get; } = [];

		public override void Write(string message) { }

		public override void WriteLine(string message) { }

		public override void Fail(string message) {
			Failures.Add(message ?? string.Empty);
		}

		public override void Fail(string message, string detailMessage) {
			Failures.Add(message ?? string.Empty);
		}

	}

	private RecordingTraceListener _recorder;
	private TraceListener[] _savedListeners;
	private IThemeCssBuilder _themeCssBuilder;
	private IThemeTemplateProvider _themeTemplateProvider;
	private ILogger _logger;
	private IGoogleFontsCatalog _googleFontsCatalog;
	private BuildThemeCommand _command;

	public override void Setup() {
		base.Setup();
		_recorder = new RecordingTraceListener();
		_savedListeners = Trace.Listeners.Cast<TraceListener>().ToArray();
		Trace.Listeners.Clear();
		Trace.Listeners.Add(_recorder);
		_command = Container.GetRequiredService<BuildThemeCommand>();
		_themeTemplateProvider.GetCssTemplate(Arg.Any<string>()).Returns("template-css");
		_themeCssBuilder.Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>()).Returns("built-css");
	}

	public override void TearDown() {
		Trace.Listeners.Clear();
		Trace.Listeners.AddRange(_savedListeners);
		_themeCssBuilder.ClearReceivedCalls();
		_themeTemplateProvider.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_googleFontsCatalog.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_themeCssBuilder = Substitute.For<IThemeCssBuilder>();
		_themeTemplateProvider = Substitute.For<IThemeTemplateProvider>();
		_logger = Substitute.For<ILogger>();
		_googleFontsCatalog = Substitute.For<IGoogleFontsCatalog>();
		_googleFontsCatalog.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(GoogleFontAvailability.InCatalog);
		containerBuilder.AddTransient<IThemeCssBuilder>(_ => _themeCssBuilder);
		containerBuilder.AddTransient<IThemeTemplateProvider>(_ => _themeTemplateProvider);
		containerBuilder.AddTransient<ILogger>(_ => _logger);
		containerBuilder.AddTransient<IGoogleFontsCatalog>(_ => _googleFontsCatalog);
	}

	private static BuildThemeOptions ValidOptions() => new() {
		Primary = "#004fd6",
		CssClassName = "MyTheme",
		Accent = "#f94e11"
	};

	private void AssertDebugFailSignal() {
		// Debug.Fail is [Conditional("DEBUG")]: every current lane (build.yml, local dev) compiles tests
		// in Debug, where the signal must fire; a Release-config lane compiles the call away entirely,
		// which is a lane-config artifact, not a containment regression — the containment assertions in
		// each test stay unconditional either way.
#if DEBUG
		_recorder.Failures.Should().NotBeEmpty(
			because: "the debug fail-fast signal must fire for a violating advisory in every Debug/test run");
#else
		_recorder.Failures.Should().BeEmpty(
			because: "Debug.Fail is compiled out of a Release build, so no fail-fast signal can exist in this lane");
#endif
	}

	private static int CountOccurrences(string text, string token) {
		int count = 0;
		int index = 0;
		while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0) {
			count++;
			index += token.Length;
		}
		return count;
	}

	[Test]
	[Description("End-to-end through the public build: a grammar-valid family the redactor rewrites (\"Bearer Sans\") makes the NotInCatalog advisory violate the contract, and the guard redacts it in place — every interpolation, not just the first — announces the substitution on the warnings channel (the only signal an MCP caller can see), echoes only redacted text to the logger, and fires the debug fail-fast; the build itself still succeeds because the containment is advisory-level.")]
	public void TryBuildTheme_ShouldContainAndAnnounceViolation_WhenAdvisoryInterpolatesARedactorTrippingFamily() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.HeadingFont = "Bearer Sans";
		_googleFontsCatalog.LookupAsync("Bearer Sans", Arg.Any<CancellationToken>())
			.Returns(GoogleFontAvailability.NotInCatalog);

		// Act
		bool ok = _command.TryBuildTheme(options, out string css, out _, out IReadOnlyList<string> warnings, out string error);

		// Assert
		ok.Should().BeTrue(because: "a contract violation is contained, not escalated into a build failure");
		error.Should().BeNull(because: "the violation travels the warnings channel, never the error channel");
		css.Should().Be("built-css", because: "the theme itself builds normally around the contained advisory");
		AssertDebugFailSignal();
		warnings.Should().HaveCount(2,
			because: "the redacted advisory plus exactly one companion announcement must come back");
		warnings[0].Should().NotContain("Bearer",
			because: "the family appears twice in the NotInCatalog advisory and every occurrence must be rewritten");
		CountOccurrences(warnings[0], "[redacted]").Should().Be(2,
			because: "both interpolations of the family must be redacted in place, not truncated away");
		warnings[0].Should().Contain("was not found in Google Fonts",
			because: "the substitution is a redaction, not a truncation — the advisory stays readable");
		warnings[1].Should().Be(RedactionContractAdvisory,
			because: "the companion advisory is the only substitution signal an MCP caller can see — the logger echo is suppressed in MCP server mode — and its literal is a caller-visible contract");
		_logger.Received(1).WriteWarning(Arg.Any<string>());
		_logger.Received(1).WriteWarning(Arg.Is<string>(line =>
			line.Contains("non-redaction contract") && line.Contains("[redacted]") && !line.Contains("Bearer")));
	}

	[Test]
	[Description("A compliant advisory in the same build passes through byte-identical while its violating sibling is redacted in place, and the companion advisory is appended once, at the end — the guard contains exactly the violator, nothing around it.")]
	public void TryBuildTheme_ShouldKeepCompliantAdvisoryByteIdentical_WhenASiblingAdvisoryViolates() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.HeadingFont = "Bearer Sans";
		options.BodyFont = "Verdana";
		_googleFontsCatalog.LookupAsync("Bearer Sans", Arg.Any<CancellationToken>())
			.Returns(GoogleFontAvailability.NotInCatalog);
		_googleFontsCatalog.LookupAsync("Verdana", Arg.Any<CancellationToken>())
			.Returns(GoogleFontAvailability.NotInCatalog);

		// Act
		bool ok = _command.TryBuildTheme(options, out _, out _, out IReadOnlyList<string> warnings, out string error);

		// Assert
		ok.Should().BeTrue(because: "advisory containment never fails the build");
		error.Should().BeNull(because: "a successful build carries no error");
		warnings.Should().HaveCount(3,
			because: "two font advisories plus exactly one companion announcement must come back");
		warnings.Should().ContainSingle(warning => warning == CompliantVerdanaAdvisory,
			because: "a contract-honoring advisory beside a violator must reach the caller byte-identical");
		string redacted = warnings.Single(warning => warning.Contains("[redacted]"));
		redacted.Should().NotContain("Bearer",
			because: "the violator must be redacted in its own slot with every occurrence rewritten");
		warnings[^1].Should().Be(RedactionContractAdvisory,
			because: "the companion advisory is appended once, after the advisories it reports on");
	}

	[Test]
	[Description("With both font slots tripping the redactor — one through the NotInCatalog template, one through the Unverified template — each violator is redacted in place and the companion advisory still appears exactly once: the announcement reports the event, not each violation.")]
	public void TryBuildTheme_ShouldAnnounceOnce_WhenBothFontAdvisoriesViolate() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.HeadingFont = "Bearer Sans";
		options.BodyFont = "Bearer Grotesk";
		_googleFontsCatalog.LookupAsync("Bearer Sans", Arg.Any<CancellationToken>())
			.Returns(GoogleFontAvailability.NotInCatalog);
		_googleFontsCatalog.LookupAsync("Bearer Grotesk", Arg.Any<CancellationToken>())
			.Returns(GoogleFontAvailability.Unverified);

		// Act
		bool ok = _command.TryBuildTheme(options, out _, out _, out IReadOnlyList<string> warnings, out string error);

		// Assert
		ok.Should().BeTrue(because: "advisory containment never fails the build");
		error.Should().BeNull(because: "a successful build carries no error");
		warnings.Should().HaveCount(3,
			because: "two redacted advisories plus exactly one companion announcement must come back — no per-violation duplicates");
		warnings.Where(warning => warning == RedactionContractAdvisory).Should().ContainSingle(
			because: "several violations in one build are one contract event, announced once");
		warnings[^1].Should().Be(RedactionContractAdvisory,
			because: "the announcement comes after the advisories it reports on");
		warnings.Should().ContainSingle(warning =>
				warning.Contains("was not found in Google Fonts") && warning.Contains("[redacted]") && !warning.Contains("Bearer"),
			because: "the NotInCatalog violator must be redacted in place through its own template");
		warnings.Should().ContainSingle(warning =>
				warning.Contains("could not verify") && warning.Contains("[redacted]") && !warning.Contains("Bearer"),
			because: "the Unverified violator must be redacted in place through its own template");
		_logger.Received(2).WriteWarning(Arg.Any<string>());
		AssertDebugFailSignal();
	}

	[Test]
	[Description("A build whose only advisory honors the contract passes through the guard as a strict no-op: the advisory arrives byte-identical, no companion advisory, no logger echo, no debug failure.")]
	public void TryBuildTheme_ShouldPassCompliantAdvisoryThroughUntouched_WhenNoAdvisoryViolates() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.FontWeights = [400, 700];

		// Act
		bool ok = _command.TryBuildTheme(options, out _, out _, out IReadOnlyList<string> warnings, out string error);

		// Assert
		ok.Should().BeTrue(because: "a compliant advisory build succeeds");
		error.Should().BeNull(because: "a successful build carries no error");
		warnings.Should().Equal([FontWeightsAdvisory],
			because: "a contract-honoring advisory must reach the caller byte-identical, with no companion advisory appended");
		_recorder.Failures.Should().BeEmpty(
			because: "the fail-fast signal exists only for contract violations");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("A clean build — published fonts, explicit accent — produces no advisories and no guard side effects at all: nothing appended, nothing logged, nothing failed.")]
	public void TryBuildTheme_ShouldEmitNothing_WhenFontsArePublished() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.HeadingFont = "Inter";
		options.BodyFont = "Inter";

		// Act
		bool ok = _command.TryBuildTheme(options, out string css, out _, out IReadOnlyList<string> warnings, out string error);

		// Assert
		ok.Should().BeTrue(because: "published fonts build cleanly");
		error.Should().BeNull(because: "a successful build carries no error");
		css.Should().Be("built-css", because: "the built CSS comes back through the public overload");
		warnings.Should().BeEmpty(because: "no advisory fires, so the guard has nothing to inspect");
		_recorder.Failures.Should().BeEmpty(because: "no violation means no fail-fast signal");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("The companion advisory itself honors the contract it reports: the redactor returns it unchanged, so announcing a violation can never introduce a second violation.")]
	public void RedactionContractAdvisory_ShouldSurviveTheRedactorUnchanged() {
		// Arrange & Act
		string redacted = SensitiveErrorTextRedactor.Redact(RedactionContractAdvisory);

		// Assert
		redacted.Should().Be(RedactionContractAdvisory,
			because: "the substitution announcement travels the unredacted warnings channel and must be static, contract-compliant text");
	}

}
