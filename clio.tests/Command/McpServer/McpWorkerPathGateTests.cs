using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The process-level gate on the worker path (ENG-95262 Stage 6): stdio only, and never from inside a
/// worker.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both refusals are correctness properties, not hardening.</b> On <c>mcp-http</c> the caller's
/// credentials live in the parent's <c>HttpContext</c> and the channel that would hand them to a child is
/// the deferred Stage 5, so a relayed cohort tool would either fail or execute under a DIFFERENT identity —
/// a privilege boundary crossed silently. And a worker serves stdio too, so without the recursion guard a
/// relayed call would spawn a worker of its own, and that worker another, without end.
/// </para>
/// <para>
/// Every case states the transport and the worker flag through the gate's injected readers rather than the
/// process-wide statics, so nothing here can leak into a parallel fixture.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpWorkerPathGateTests {

	[Test]
	[Category("Unit")]
	[Description("An ordinary stdio host that is not itself a worker may spawn workers — the one combination that is allowed.")]
	public void Evaluate_ShouldReportAvailable_OnAStdioHostThatIsNotAWorker() {
		// Arrange
		McpWorkerPathGate sut = new(() => McpHostTransportKind.Stdio, () => false);

		// Act
		McpWorkerPathAvailability availability = sut.Evaluate();

		// Assert
		availability.Should().Be(McpWorkerPathAvailability.Available,
			because: "on stdio no secret crosses the boundary — the child reads appsettings.json itself and receives only the environment NAME — so the credential objection does not apply");
	}

	[Test]
	[Category("Unit")]
	[Description("The HTTP host may NOT spawn workers: the credential channel a child would need is the deferred Stage 5, so relaying would fail or silently change identity.")]
	public void Evaluate_ShouldReportTransportNotStdio_OnAnHttpHost() {
		// Arrange
		McpWorkerPathGate sut = new(() => McpHostTransportKind.Http, () => false);

		// Act
		McpWorkerPathAvailability availability = sut.Evaluate();

		// Assert
		availability.Should().Be(McpWorkerPathAvailability.HostTransportNotStdio,
			because: "the gate is keyed on the DECLARED transport, not on whether a credential context happens to be null — the latter would open the worker path for the first unauthenticated mcp-http request");
	}

	[Test]
	[Category("Unit")]
	[Description("An undeclared transport is fail-closed: a host, a CLI process or a hand-built container that never stated a transport must not spawn workers on an unstated assumption.")]
	public void Evaluate_ShouldReportTransportNotStdio_WhenNoTransportWasDeclared() {
		// Arrange
		McpWorkerPathGate sut = new(() => McpHostTransportKind.Unknown, () => false);

		// Act
		McpWorkerPathAvailability availability = sut.Evaluate();

		// Assert
		availability.Should().Be(McpWorkerPathAvailability.HostTransportNotStdio,
			because: "the zero value must be the closed one; a future transport whose author forgets to declare would otherwise inherit the worker path along with the credential problem");
	}

	[Test]
	[Category("Unit")]
	[Description("A worker process never spawns workers, and the recursion guard is reported even though a worker's own transport IS stdio — so a transport-only check would have passed here.")]
	public void Evaluate_ShouldReportProcessIsWorker_WhenThisProcessIsAWorkerOnStdio() {
		// Arrange
		McpWorkerPathGate sut = new(() => McpHostTransportKind.Stdio, () => true);

		// Act
		McpWorkerPathAvailability availability = sut.Evaluate();

		// Assert
		availability.Should().Be(McpWorkerPathAvailability.ProcessIsWorker,
			because: "the child receives the very tools/call the parent relayed, so the decision that sent it there would send it on again — unbounded process creation, not a slow call");
	}

	[Test]
	[Category("Unit")]
	[Description("The recursion guard is checked BEFORE the transport, so a worker is refused for being a worker rather than being reported under whichever transport it happens to serve.")]
	public void Evaluate_ShouldPreferTheRecursionGuard_WhenBothConditionsWouldRefuse() {
		// Arrange
		McpWorkerPathGate sut = new(() => McpHostTransportKind.Unknown, () => true);

		// Act
		McpWorkerPathAvailability availability = sut.Evaluate();

		// Assert
		availability.Should().Be(McpWorkerPathAvailability.ProcessIsWorker,
			because: "the reason has to name the condition that cannot be argued with; reporting a transport problem for a worker would send a reader looking for a credential channel that is not the issue");
	}

}
