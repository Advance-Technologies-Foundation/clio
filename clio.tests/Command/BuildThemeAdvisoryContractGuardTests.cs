namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using Clio.UserEnvironment;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Pins <see cref="BuildThemeCommand.EnforceAdvisoryRedactionContract"/> — the Release-containment
/// backstop for the advisory non-redaction contract. Every test runs with the trace listeners swapped
/// for a recording one (see <see cref="Setup"/>) so the guard's <c>Debug.Fail</c> is captured instead of
/// fail-fasting the test host; the fixture is <see cref="NonParallelizableAttribute"/> because that swap
/// is process-global state.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Command")]
public class BuildThemeAdvisoryContractGuardTests {

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
	private ILogger _logger;
	private BuildThemeCommand _command;

	[SetUp]
	public void Setup() {
		_recorder = new RecordingTraceListener();
		_savedListeners = Trace.Listeners.Cast<TraceListener>().ToArray();
		Trace.Listeners.Clear();
		Trace.Listeners.Add(_recorder);
		_logger = Substitute.For<ILogger>();
		_command = new BuildThemeCommand(
			Substitute.For<IThemeCssBuilder>(),
			Substitute.For<IThemeTemplateProvider>(),
			Substitute.For<IPlatformVersionResolverFactory>(),
			Substitute.For<ISettingsRepository>(),
			Substitute.For<IWorkspacePathBuilder>(),
			Substitute.For<IFileSystem>(),
			_logger,
			Substitute.For<IGoogleFontsCatalog>());
	}

	[TearDown]
	public void Teardown() {
		Trace.Listeners.Clear();
		Trace.Listeners.AddRange(_savedListeners);
	}

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

	[Test]
	[Description("A violating advisory is replaced with its redacted form, the substitution is announced on the warnings channel itself (the companion advisory MCP callers can see), the logger echo carries only redacted text, and the debug fail-fast signal fires in Debug lanes — so the containment can never run invisibly.")]
	public void EnforceAdvisoryRedactionContract_ShouldStripAnnounceAndReport_WhenAdvisoryViolatesTheContract() {
		// Arrange
		List<string> warnings = ["build-theme: probe hit https://tenant.example/x?password=hunter2 unexpectedly."];

		// Act
		_command.EnforceAdvisoryRedactionContract(warnings);

		// Assert
		AssertDebugFailSignal();
		warnings[0].Should().NotContain("hunter2",
			because: "the containment must strip the violating text before the advisory can reach any caller");
		warnings[0].Should().Contain("[redacted-uri]",
			because: "the substitution must be a redaction, not a truncation — the advisory stays readable");
		warnings.Should().Contain(BuildThemeCommand.RedactionContractAdvisory,
			because: "the companion advisory is the only substitution signal an MCP caller can see — the logger echo is suppressed in MCP server mode and the flow log buffer is cleared");
		_logger.Received(1).WriteWarning(Arg.Is<string>(line =>
			line.Contains("[redacted-uri]") && !line.Contains("hunter2")));
	}

	[Test]
	[Description("With several violating advisories around a compliant one, each violator is redacted in place, the compliant advisory stays byte-identical, and the companion advisory is appended exactly once, at the end — the announcement reports the event, not each violation.")]
	public void EnforceAdvisoryRedactionContract_ShouldAnnounceOnce_WhenSeveralAdvisoriesViolateTheContract() {
		// Arrange
		const string compliant = "build-theme: font weights were ignored — they apply only to a custom heading or body font.";
		List<string> warnings = [
			"build-theme: probe hit https://tenant.example/x?password=hunter2 unexpectedly.",
			compliant,
			@"build-theme: template fell back after reading C:\secrets\template.css."
		];

		// Act
		_command.EnforceAdvisoryRedactionContract(warnings);

		// Assert
		warnings.Should().HaveCount(4,
			because: "three original advisories plus exactly one companion announcement must come back — no per-violation duplicates");
		warnings[0].Should().Contain("[redacted-uri]",
			because: "the first violator must be redacted in its own slot, not dropped or reordered");
		warnings[1].Should().Be(compliant,
			because: "a contract-honoring advisory sitting between violators must stay byte-identical");
		warnings[2].Should().Contain("[redacted-path]",
			because: "the second violator must be redacted in its own slot through the path pattern");
		warnings[3].Should().Be(BuildThemeCommand.RedactionContractAdvisory,
			because: "the companion advisory is appended once, after the advisories it reports on");
	}

	[Test]
	[Description("Compliant advisories pass through untouched: no substitution, no companion advisory, no logger echo, no debug failure — the guard is a strict no-op on the contract-honoring path every shipped advisory takes.")]
	public void EnforceAdvisoryRedactionContract_ShouldBeNoOp_WhenEveryAdvisoryHonorsTheContract() {
		// Arrange
		const string compliant = "build-theme: font weights were ignored — they apply only to a custom heading or body font.";
		List<string> warnings = [compliant];

		// Act
		_command.EnforceAdvisoryRedactionContract(warnings);

		// Assert
		warnings.Should().Equal([compliant],
			because: "a contract-honoring advisory must reach the caller byte-identical, with no companion advisory appended");
		_recorder.Failures.Should().BeEmpty(
			because: "the fail-fast signal exists only for contract violations");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("The companion advisory itself honors the contract it reports: the redactor returns it unchanged, so announcing a violation can never introduce a second violation.")]
	public void RedactionContractAdvisory_ShouldSurviveTheRedactorUnchanged() {
		// Arrange & Act
		string redacted = Clio.Command.McpServer.SensitiveErrorTextRedactor.Redact(
			BuildThemeCommand.RedactionContractAdvisory);

		// Assert
		redacted.Should().Be(BuildThemeCommand.RedactionContractAdvisory,
			because: "the substitution announcement travels the unredacted warnings channel and must be static, contract-compliant text");
	}

}
