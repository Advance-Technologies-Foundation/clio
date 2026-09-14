using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ATF.Repository.Exceptions;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit coverage for the one place that decides which process-library read failures degrade to
/// "not established" and which are defects that must surface.
/// </summary>
/// <remarks>
/// The type exists because two call sites wrote the ladder separately and drifted apart on the first edit,
/// yet only two of its six entries were reached by any test — through the reader, incidentally. Dropping
/// <c>JsonException</c>, <c>HttpRequestException</c>, <c>TimeoutException</c> or
/// <c>InvalidOperationException</c> turned a degraded answer into an unhandled exception inside the MCP
/// server with nothing going red, which is exactly the drift this class was created to prevent.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessLibReadTests {

	private const string Degraded = "degraded";

	/// <summary>
	/// One case per caught type. Constructed here rather than in <c>[TestCase]</c> arguments because an
	/// exception instance is not a compile-time constant.
	/// </summary>
	private static readonly Exception[] DegradingFailures = [
		new WebException("simulated transport failure"),
		new HttpRequestException("simulated http failure"),
		new TaskCanceledException("simulated request timeout"),
		new OperationCanceledException("simulated cancellation"),
		new JsonException("simulated payload failure"),
		new TimeoutException("simulated synchronous timeout"),
		new InvalidOperationException("simulated provider state failure")
	];

	[Test]
	[TestCaseSource(nameof(DegradingFailures))]
	[Description("Every failure type the ladder lists is turned into the caller's degraded answer. Each entry is a separate case so removing one from the ladder reddens exactly one test and names it, instead of leaving the whole class of failure to escape into the MCP server unnoticed.")]
	public void Guarded_Should_ApplyOnFailure_When_TheReadRaisesACaughtType(Exception failure) {
		// Arrange
		Func<string> read = () => throw failure;

		// Act
		Func<string> act = () => ProcessLibRead.Guarded(read, e => $"{Degraded}: {e.Message}");

		// Assert
		string answer = act.Should().NotThrow(
				because: $"{failure.GetType().Name} means the environment did not answer usefully, which is a "
					+ "fact that could not be established rather than a defect in clio")
			.Which;
		answer.Should().Be($"{Degraded}: {failure.Message}",
			because: "the degraded answer must carry the reason, since the caller reports WHY the facts are "
				+ "absent and not merely that they are");
	}

	[Test]
	[Description("An ATF expression failure propagates. It means the LINQ at the call site is wrong, so degrading it would hide a defect in clio behind a version warning and a green describe — the one failure this ladder deliberately does not list.")]
	public void Guarded_Should_Propagate_When_TheReadRaisesAnAtfExpressionFailure() {
		// Arrange
		Func<string> read = () => throw new ExpressionConvertException();

		// Act
		Func<string> act = () => ProcessLibRead.Guarded(read, _ => Degraded);

		// Assert
		act.Should().Throw<ExpressionConvertException>(
			because: "a wrong call-site expression is a defect to surface, and catching it would make every "
				+ "describe answer 'the version facts were not established' for a bug in this repository");
	}

	[Test]
	[Description("A read that completes inside its budget returns its own answer, so the budget cannot quietly replace a good result.")]
	public void WithinBudget_Should_ReturnTheReadsAnswer_When_ItFinishesInTime() {
		// Arrange & Act
		string answer = ProcessLibRead.WithinBudget(TimeSpan.FromSeconds(30), () => "read", () => "expired");

		// Assert
		answer.Should().Be("read",
			because: "the budget is a ceiling on waiting, not a filter on the answer");
	}

	[Test]
	[Description("A read that outruns its budget answers through the expiry path rather than blocking. The abandoned read cannot be cancelled — neither the ATF synchronous path nor Creatio.Client takes a token — so the budget is the only thing that returns control to the caller.")]
	public void WithinBudget_Should_Answer_When_TheReadOutrunsTheBudget() {
		// Arrange
		Func<string> slow = () => {
			Thread.Sleep(TimeSpan.FromSeconds(2));
			return "read";
		};

		// Act
		string answer = ProcessLibRead.WithinBudget(TimeSpan.FromMilliseconds(50), slow, () => "expired");

		// Assert
		answer.Should().Be("expired",
			because: "a read with no timeout of its own would otherwise run to the ATF library's default while "
				+ "the MCP read deadline expires and takes the whole describe with it");
	}

	[Test]
	[Description("A failure inside the budget surfaces as itself, not wrapped in the AggregateException that Task.Wait raises. The ladder above is written against concrete types, so a wrapper would make every one of its entries unreachable when a budget is in play.")]
	public void WithinBudget_Should_SurfaceTheOriginalException_When_TheReadFails() {
		// Arrange
		Func<string> failing = () => throw new InvalidOperationException("simulated provider state failure");

		// Act
		Func<string> act = () =>
			ProcessLibRead.WithinBudget(TimeSpan.FromSeconds(30), failing, () => "expired");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the budget wrapper must be transparent to the exception the read raised, or the "
					+ "guarded ladder outside it stops matching anything")
			.WithMessage("simulated provider state failure");
	}

}
