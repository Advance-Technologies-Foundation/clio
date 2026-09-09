using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command.McpServer;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95885 review round 4. Pins the advisory emitter shared by
/// <c>McpToolErrorFilter.ReportArgumentShape</c> and <c>McpServerCommand.WarnDuringStartup</c>.
/// </summary>
/// <remarks>
/// The stderr mirror is the half that actually reaches an MCP host, and it was previously unreachable
/// from a test: an in-process test is never in MCP server mode, and the fixture that drives the real
/// stdio server does not expose the child's standard error. Taking the transport flag and the writer as
/// parameters is what makes both branches — including the dead-sink swallow — executable here instead of
/// only reasoned about.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpAdvisoryLogTests {

	private static (Clio.Common.ILogger Logger, List<string> Stderr) Arrange() =>
		(Substitute.For<Clio.Common.ILogger>(), []);

	[Test]
	[Category("Unit")]
	[Description("Outside MCP server mode nothing reaches standard error, because the ordinary CLI already prints through the logger and a duplicate line on stderr would be noise (ENG-95885 review round 4).")]
	public void Emit_ShouldNotTouchStandardError_WhenNotInMcpServerMode() {
		// Arrange
		(Clio.Common.ILogger logger, List<string> stderr) = Arrange();

		// Act
		McpAdvisoryLog.Emit(logger, "plain message", isWarning: false,
			mirrorToStandardError: false, writeStandardError: stderr.Add);

		// Assert
		stderr.Should().BeEmpty(
			because: "the stderr mirror exists only because ConsoleLogger is muted on the stdio transport");
		logger.Received(1).WriteInfo("plain message");
	}

	[TestCase(true, "WAR")]
	[TestCase(false, "INF")]
	[Category("Unit")]
	[Description("In MCP server mode the line is mirrored to standard error with the severity prefix an MCP host's captured log is read by, and never to stdout — stdout is the JSON-RPC channel and a stray line there corrupts the protocol (ENG-95885 review round 4).")]
	public void Emit_ShouldMirrorToStandardError_WithSeverityPrefix(bool isWarning, string expectedPrefix) {
		// Arrange
		(Clio.Common.ILogger logger, List<string> stderr) = Arrange();

		// Act
		McpAdvisoryLog.Emit(logger, "shape line", isWarning,
			mirrorToStandardError: true, writeStandardError: stderr.Add);

		// Assert
		stderr.Should().ContainSingle(
				because: "exactly one advisory line per emit reaches the host, never zero and never two")
			.Which.Should().Be($"[{expectedPrefix}] shape line",
				because: "the prefix is how a reader separates a refusal from an accommodation");
		if (isWarning) {
			logger.Received(1).WriteWarning("shape line");
		} else {
			logger.Received(1).WriteInfo("shape line");
		}
	}

	// ObjectDisposedException has no accessible parameterless constructor, so the cases name the sink
	// failure and build it explicitly rather than going through Activator.
	[TestCase("io")]
	[TestCase("disposed")]
	[Category("Unit")]
	[Description("A dead standard-error sink is swallowed rather than propagated. A detached launcher can close stderr at any time, and the contract this emitter is built on is that losing an ADVISORY channel must never fail the operation it was describing — a tool call, or MCP transport startup (ENG-95885 review round 4).")]
	public void Emit_ShouldSwallowADeadStandardErrorSink(string sinkFailure) {
		// Arrange
		(Clio.Common.ILogger logger, List<string> _) = Arrange();
		Exception failure = sinkFailure == "io"
			? new IOException("stderr closed")
			: new ObjectDisposedException("stderr");
		void ThrowingWriter(string _) => throw failure;

		// Act
		Action emit = () => McpAdvisoryLog.Emit(logger, "shape line", isWarning: true,
			mirrorToStandardError: true, writeStandardError: ThrowingWriter);

		// Assert
		emit.Should().NotThrow(
			because: $"a {failure.GetType().Name} from the advisory sink must not surface as a failure of "
				+ "the tool call or the startup it was merely describing");
		logger.Received(1).WriteWarning("shape line");
	}

	[Test]
	[Category("Unit")]
	[Description("An exception the emitter does NOT own still propagates, so a genuine defect in a writer is not hidden by the dead-sink tolerance (ENG-95885 review round 4).")]
	public void Emit_ShouldNotSwallowAnUnrelatedException() {
		// Arrange
		(Clio.Common.ILogger logger, List<string> _) = Arrange();
		void ThrowingWriter(string _) => throw new InvalidOperationException("boom");

		// Act
		Action emit = () => McpAdvisoryLog.Emit(logger, "shape line", isWarning: false,
			mirrorToStandardError: true, writeStandardError: ThrowingWriter);

		// Assert
		emit.Should().Throw<InvalidOperationException>(
			because: "the swallow is scoped to a closed or disposed sink; widening it would hide real bugs");
	}

	[Test]
	[Category("Unit")]
	[Description("An absent logger is silent rather than fatal: the filter service-locates it from a static seam that legitimately has none, and the stderr mirror must still run (ENG-95885 review round 4).")]
	public void Emit_ShouldStillMirror_WhenThereIsNoLogger() {
		// Arrange
		List<string> stderr = [];

		// Act
		Action emit = () => McpAdvisoryLog.Emit(logger: null, "shape line", isWarning: false,
			mirrorToStandardError: true, writeStandardError: stderr.Add);

		// Assert
		emit.Should().NotThrow(because: "a missing logger is a normal state for a service-located sink");
		stderr.Should().ContainSingle(
			because: "the host channel must not be lost just because no file sink is configured");
	}

	[Test]
	[Description("The emitted text is length-bounded, because a caller-supplied fragment (an unknown argument name) is arbitrary text and an unbounded line would flood a host log (ENG-95885 review round 4).")]
	[Category("Unit")]
	public void Emit_ShouldBoundTheMessageLength() {
		// Arrange
		List<string> stderr = [];
		string overlong = new('x', 5_000);

		// Act
		string emitted = McpAdvisoryLog.Emit(logger: null, overlong, isWarning: false,
			mirrorToStandardError: true, writeStandardError: stderr.Add);

		// Assert
		emitted.Length.Should().BeLessThan(overlong.Length,
			because: "an arbitrary-length caller fragment must be truncated before it reaches a sink");
		stderr.Should().ContainSingle().Which.Length.Should().BeLessThan(overlong.Length,
			because: "the mirrored line carries the same bounded text the logger received");
	}

	[Test]
	[Category("Unit")]
	[Description("The message is BOUNDED before it is redacted, not after, so no caller-supplied string sets the size of the regex work. Redaction used to run over the whole unbounded message with the cap applied to its OUTPUT - which paid a pass per pattern over text about to be discarded, and let a crafted fragment burn SensitiveErrorTextRedactor's 1s regex budget, after which it returns a bare placeholder for the WHOLE message and the diagnostic the ADR's closing measurement depends on is gone. The ordering is pinned by a deterministic consequence rather than by timing: content sitting BEYOND the redaction-input budget must not appear in the output, which it would if redaction ran first and its placeholders compressed the earlier text enough to pull that content inside the final cap (ENG-95885 review round 9).")]
	public void Emit_ShouldBoundBeforeRedacting_SoContentPastTheBudgetCannotSurvive() {
		// Arrange — long redactable URIs ahead of a distinctive tail marker placed past the 4,000-char
		// redaction-input budget. Redact-first would collapse each URI to a short placeholder and pull
		// the marker inside the 1,000-char output cap; bound-first discards the marker before the
		// redactor ever sees it. That difference is deterministic, unlike a regex-timeout race.
		List<string> stderr = [];
		string uris = string.Concat(Enumerable.Range(0, 20).Select(index =>
			$"https://tenant-{index}-{new string('h', 180)}.creatio.com/0/rest/Probe "));
		const string tailMarker = "PAST-THE-BUDGET-MARKER";
		string message =
			$"mcp-argument-shape: tool='list-apps' outcome=WrappedFlat keys=[{uris}{tailMarker}]";

		// Act
		string emitted = McpAdvisoryLog.Emit(logger: null, message, isWarning: false,
			mirrorToStandardError: true, writeStandardError: stderr.Add);

		// Assert
		message.IndexOf(tailMarker, StringComparison.Ordinal).Should().BeGreaterThan(4_000,
			because: "the arrangement only discriminates if the marker really sits past the budget — "
				+ "asserting it keeps this test honest if the constants ever move");
		emitted.Should().NotContain(tailMarker,
			because: "content beyond the redaction-input budget is cut BEFORE redaction; if the redactor "
				+ "ran first, its placeholders would compress the URIs enough for this marker to reach "
				+ "the emitted line — which is exactly the ordering being ruled out");
		emitted.Should().StartWith("mcp-argument-shape: tool='list-apps' outcome=WrappedFlat",
			because: "the server-authored diagnostic still leads the line — bounding first must not cost "
				+ "the signal the line exists to carry");
		stderr.Should().ContainSingle(
			because: "one advisory line is mirrored, bounded the same way the logger's copy is")
			.Which.Should().Contain("outcome=WrappedFlat",
				because: "the mirrored copy carries the same surviving diagnostic");
	}

	[Test]
	[Category("Unit")]
	[Description("Emit returns exactly the text it wrote, so a caller can assert on what a reader will actually see rather than on its own pre-redaction input (ENG-95885 review round 4).")]
	public void Emit_ShouldReturnTheTextItWrote() {
		// Arrange
		List<string> stderr = [];

		// Act
		string emitted = McpAdvisoryLog.Emit(logger: null, "shape line", isWarning: true,
			mirrorToStandardError: true, writeStandardError: stderr.Add);

		// Assert
		stderr.Should().ContainSingle().Which.Should().Be($"[WAR] {emitted}",
			because: "the returned text and the mirrored line must be the same string, or an assertion on "
				+ "one would not tell you anything about the other");
	}
}
