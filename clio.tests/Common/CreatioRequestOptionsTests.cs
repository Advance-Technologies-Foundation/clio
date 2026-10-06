using System;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// <see cref="CreatioRequestOptions.ForNextRequest"/> with and without a <see cref="RequestDeadline"/>: a call bounded
/// by a deadline cuts each request's timeout to what is left of it, and sends nothing once it is spent.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CreatioRequestOptionsTests {

	private static RequestDeadline Deadline(double budgetSeconds, double elapsedSeconds) =>
		new(TimeSpan.FromSeconds(budgetSeconds), () => TimeSpan.FromSeconds(elapsedSeconds));

	[Test]
	[Description("Without a deadline the options are used as they are: the CLI keeps its own timeout.")]
	public void ForNextRequest_ShouldKeepTheOptions_WhenThereIsNoDeadline() {
		// Arrange
		CreatioRequestOptions options = new() { TimeOut = 30_000, MaxAttempts = 3 };

		// Act
		CreatioRequestOptions next = options.ForNextRequest();

		// Assert
		next.Should().BeSameAs(options, because: "nothing bounds the call beyond each request's timeout");
	}

	[TestCase(100, 10, 30_000, TestName = "ForNextRequest_ShouldKeepTheTimeout_WhenMoreThanItIsLeft")]
	[TestCase(100, 80, 20_000, TestName = "ForNextRequest_ShouldCutTheTimeout_WhenLessThanItIsLeft")]
	[Description("A request gets its own timeout or what is left of the deadline, whichever is shorter; attempts and delay are kept.")]
	public void ForNextRequest_ShouldBoundTheTimeoutByWhatIsLeft(double budget, double elapsed, int expectedTimeOut) {
		// Arrange
		CreatioRequestOptions options = new() { TimeOut = 30_000, MaxAttempts = 1, RetryDelay = 2, Deadline = Deadline(budget, elapsed) };

		// Act
		CreatioRequestOptions next = options.ForNextRequest();

		// Assert
		next.TimeOut.Should().Be(expectedTimeOut, because: "a request may not outlast the call");
		next.MaxAttempts.Should().Be(1, because: "only the timeout is cut");
		next.RetryDelay.Should().Be(2, because: "only the timeout is cut");
		next.Deadline.Should().BeSameAs(options.Deadline, because: "every request of the call shares one deadline");
	}

	[Test]
	[Description("A spent deadline refuses the request with a TimeoutException, so the caller treats it like a hang and nothing is sent.")]
	public void ForNextRequest_ShouldThrowTimeout_WhenTheDeadlineIsSpent() {
		// Arrange
		CreatioRequestOptions options = new() { Deadline = Deadline(90, 95) };

		// Act
		Action act = () => options.ForNextRequest();

		// Assert
		act.Should().Throw<TimeoutException>(because: "no time is left for the request")
			.WithMessage("*90 s*not sent*");
		options.Deadline.IsSpent.Should().BeTrue(because: "the elapsed time is past the budget");
		options.Deadline.Remaining.Should().Be(TimeSpan.Zero, because: "the time left is never negative");
	}
}
