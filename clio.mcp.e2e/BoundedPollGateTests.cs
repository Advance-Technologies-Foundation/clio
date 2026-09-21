using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for <see cref="BoundedPollGate.PollUntilAsync"/>'s budget-exhaustion contract. They drive the
/// poll with in-memory probe delegates (no MCP server, no stand, no network I/O) and a zero poll interval,
/// so they validate the "nothing is swallowed" guarantee locally and are categorized <c>Unit</c> rather
/// than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class BoundedPollGateTests {
	private static readonly TimeSpan NoDelay = TimeSpan.Zero;

	/// <summary>
	/// The token the polls below wait for. Deliberately a string no probe body contains incidentally — a
	/// body worded "…without the marker" would itself satisfy a predicate searching for "marker" and make
	/// every budget-exhaustion assertion below vacuous.
	/// </summary>
	private const string Marker = "ClioPollMarker";

	/// <summary>
	/// Builds a probe that replays <paramref name="outcomes"/> one per attempt: a <see cref="Exception"/>
	/// entry is thrown, anything else is returned.
	/// </summary>
	private static Func<CancellationToken, Task<string>> ScriptedProbe(params object[] outcomes) {
		int attempt = 0;
		return _ => {
			object outcome = outcomes[attempt++];
			return outcome is Exception failure
				? Task.FromException<string>(failure)
				: Task.FromResult((string)outcome);
		};
	}

	[Test]
	[Description("Rethrows the final attempt's probe failure when an early attempt succeeded but the last one threw, instead of returning the stale early result.")]
	public async Task PollUntilAsync_ShouldRethrowLastProbeFailure_WhenAnEarlyAttemptSucceededAndTheFinalAttemptFailed() {
		// Arrange — the succeed-then-fail interleaving: attempt 1 returns an unsatisfying body, attempts
		// 2-3 throw. Before the fix `hasResult` stayed sticky from attempt 1, so the poll returned that
		// stale body and discarded every exception, which at the PageSyncToolE2ETests call site turned a
		// genuine EntitySchemaStructuredResultParser.Extract parse error into a much vaguer "marker not in
		// body" assertion failure.
		InvalidOperationException finalFailure = new("envelope could not be parsed on the final probe");
		Func<CancellationToken, Task<string>> probeAsync = ScriptedProbe(
			"stale body from an early probe",
			new InvalidOperationException("envelope could not be parsed"),
			finalFailure);

		// Act
		Func<Task> poll = () => BoundedPollGate.PollUntilAsync(
			probeAsync,
			body => body.Contains(Marker, StringComparison.Ordinal),
			maxAttempts: 3,
			NoDelay,
			CancellationToken.None,
			isTransientProbeFailure: probeException => probeException is InvalidOperationException);

		// Assert
		(await poll.Should().ThrowAsync<InvalidOperationException>(
				because: "the budget ran out on a FAILED attempt, so the real diagnostic must be surfaced rather than a stale earlier result"))
			.Which.Should().BeSameAs(finalFailure,
				because: "the exception surfaced must be the one the LAST attempt threw, not an earlier one");
	}

	[Test]
	[Description("Rethrows the last probe failure when every attempt failed, which is the case that already worked before the succeed-then-fail gap was closed.")]
	public async Task PollUntilAsync_ShouldRethrowLastProbeFailure_WhenEveryAttemptFailed() {
		// Arrange
		InvalidOperationException finalFailure = new("still unparsable on the last attempt");
		Func<CancellationToken, Task<string>> probeAsync = ScriptedProbe(
			new InvalidOperationException("unparsable"),
			finalFailure);

		// Act
		Func<Task> poll = () => BoundedPollGate.PollUntilAsync(
			probeAsync,
			body => body.Contains(Marker, StringComparison.Ordinal),
			maxAttempts: 2,
			NoDelay,
			CancellationToken.None,
			isTransientProbeFailure: probeException => probeException is InvalidOperationException);

		// Assert
		(await poll.Should().ThrowAsync<InvalidOperationException>(
				because: "no attempt ever succeeded, so the last observed failure is the only diagnostic there is"))
			.Which.Should().BeSameAs(finalFailure);
	}

	[Test]
	[Description("Returns the last probe result when the budget is exhausted on a SUCCESSFUL attempt, leaving pass/fail to the caller's own assertions.")]
	public async Task PollUntilAsync_ShouldReturnLastResult_WhenTheFinalAttemptSucceededButWasNotSatisfying() {
		// Arrange — a transient failure in the middle must not be promoted to a throw once a later attempt
		// answers cleanly: the caller's own assertions still decide, and they get a result to report.
		Func<CancellationToken, Task<string>> probeAsync = ScriptedProbe(
			new InvalidOperationException("unparsable mid-write"),
			"final body, still not carrying it");

		// Act
		string last = await BoundedPollGate.PollUntilAsync(
			probeAsync,
			body => body.Contains(Marker, StringComparison.Ordinal),
			maxAttempts: 2,
			NoDelay,
			CancellationToken.None,
			isTransientProbeFailure: probeException => probeException is InvalidOperationException);

		// Assert
		last.Should().Be("final body, still not carrying it",
			because: "a successful probe clears the remembered failure, so an exhausted budget returns the last answer for the caller to assert on");
	}

	[Test]
	[Description("Returns as soon as a probe result satisfies the caller's predicate, without consuming the remaining budget.")]
	public async Task PollUntilAsync_ShouldReturnImmediately_WhenTheFirstProbeIsSatisfying() {
		// Arrange
		List<int> attempts = [];
		int attempt = 0;
		Func<CancellationToken, Task<string>> probeAsync = _ => {
			attempts.Add(++attempt);
			return Task.FromResult(Marker + " observed in the body");
		};

		// Act
		string observed = await BoundedPollGate.PollUntilAsync(
			probeAsync,
			body => body.Contains(Marker, StringComparison.Ordinal),
			maxAttempts: 6,
			NoDelay,
			CancellationToken.None);

		// Assert
		observed.Should().Be(Marker + " observed in the body");
		attempts.Should().HaveCount(1,
			because: "a genuinely immediate write must still pass on the first attempt rather than burning the whole polling budget");
	}

	[Test]
	[Description("Propagates a probe exception that the caller's transient-failure predicate does not match, rather than treating it as 'not ready yet'.")]
	public async Task PollUntilAsync_ShouldPropagateImmediately_WhenTheProbeFailureIsNotTransient() {
		// Arrange
		Func<CancellationToken, Task<string>> probeAsync = ScriptedProbe(new NotSupportedException("a real abort"));

		// Act
		Func<Task> poll = () => BoundedPollGate.PollUntilAsync(
			probeAsync,
			body => body.Contains(Marker, StringComparison.Ordinal),
			maxAttempts: 3,
			NoDelay,
			CancellationToken.None,
			isTransientProbeFailure: probeException => probeException is InvalidOperationException);

		// Assert
		await poll.Should().ThrowAsync<NotSupportedException>(
			because: "only the exceptions the caller classifies as transient are a 'keep polling' signal; anything else aborts the poll unchanged");
	}
}
