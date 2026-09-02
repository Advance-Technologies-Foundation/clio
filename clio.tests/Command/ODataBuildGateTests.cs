using System;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ODataBuildGateTests
{
	private IRemoteEntitySchemaDesignerClient _client = null!;
	private IRetryDelay _retryDelay = null!;
	private ILogger _logger = null!;
	private RemoteCommandOptions _options = null!;
	private ODataBuildGate _gate = null!;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_retryDelay = Substitute.For<IRetryDelay>();
		_logger = Substitute.For<ILogger>();
		_options = new RemoteCommandOptions { Uri = "https://stand.example.local" };
		_gate = new ODataBuildGate(_client, _retryDelay, _logger);
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_retryDelay.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Returns immediately without waiting when the server has no status method (a null probe result), and remembers that per environment so a second publish against the same environment does not probe again.")]
	public void WaitUntilIdle_ShouldReturnImmediately_AndRememberEnvironment_WhenProbeIsUnsupported() {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns((bool?)null);

		// Act
		_gate.WaitUntilIdle(_options, "UsrVehicle");
		_gate.WaitUntilIdle(_options, "UsrVehicle2");

		// Assert
		_retryDelay.DidNotReceiveWithAnyArgs().Wait(default);
		// because: an environment with no status method cannot be waited on, so no delay should ever be invoked
		_client.Received(1).TryGetIsODataBuildRunning(_options);
		// because: the answer is a property of the deployed platform, so the second publish against the same environment must not probe again
	}

	[Test]
	[Description("Returns immediately without waiting when the probe reports the build is already idle.")]
	public void WaitUntilIdle_ShouldReturnImmediately_WhenProbeReportsIdle() {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(false);

		// Act
		_gate.WaitUntilIdle(_options, "UsrVehicle");

		// Assert
		_retryDelay.DidNotReceiveWithAnyArgs().Wait(default);
		// because: an idle build needs no wait before the publish proceeds
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		// because: an idle build is the normal case and must not be reported as a problem
	}

	[Test]
	[Description("Waits exactly once and returns as soon as the build finishes, when the probe first reports running and then idle.")]
	public void WaitUntilIdle_ShouldWaitOnce_WhenBuildFinishesAfterOnePoll() {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(true, false);

		// Act
		_gate.WaitUntilIdle(_options, "UsrVehicle");

		// Assert
		_retryDelay.Received(1).Wait(ODataBuildGate.PollInterval);
		// because: the gate must poll once more after the first "still running" answer before returning
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Waiting for the running OData entities build", StringComparison.Ordinal)));
		// because: a caller held back by a running build must be told why the publish has not started yet
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		// because: the build finishing within the poll budget is not a problem worth warning about
	}

	[Test]
	[Description("Waits exactly PollAttemptCount times, then warns and returns without throwing, when the build never reports idle within the poll budget.")]
	public void WaitUntilIdle_ShouldWaitFullBudget_ThenWarnWithoutThrowing_WhenBuildNeverFinishes() {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(true);

		// Act
		Action act = () => _gate.WaitUntilIdle(_options, "UsrVehicle");

		// Assert
		act.Should().NotThrow(
			because: "a slow environment must not turn the caller's legitimate work into a command failure");
		_retryDelay.Received(ODataBuildGate.PollAttemptCount).Wait(ODataBuildGate.PollInterval);
		// because: the gate must spend exactly its documented poll budget before giving up
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("still running", StringComparison.Ordinal)
			&& message.Contains("UsrVehicle", StringComparison.Ordinal)));
		// because: a caller whose publish may still collide with the build must be told which schema is at risk
	}

	[TestCaseSource(nameof(ProbeFaults))]
	[Description("Absorbs an environment or transport fault raised by the first probe: the gate warns and returns instead of letting the exception abort a mutation that has already been saved.")]
	public void WaitUntilIdle_ShouldWarnAndReturn_WhenFirstProbeFaults(Exception fault) {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(_ => throw fault);

		// Act
		Action act = () => _gate.WaitUntilIdle(_options, "UsrVehicle");

		// Assert
		act.Should().NotThrow(
			because: "the schema is already saved when the gate runs, and the publisher calls the gate outside its own try block, so a probe fault would leave the mutation persisted but unpublished");
		_retryDelay.DidNotReceiveWithAnyArgs().Wait(default);
		// because: a probe that could not answer gives no reason to wait
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Could not read the OData entities build status", StringComparison.Ordinal)));
		// because: the caller must learn the wait was skipped, since a collision with a running build stays possible
	}

	[Test]
	[Description("Absorbs a transport fault raised mid-poll: the gate stops waiting and returns instead of killing the command after the publish already started waiting.")]
	public void WaitUntilIdle_ShouldStopWaitingAndReturn_WhenPollFaults() {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(
			_ => true,
			_ => throw new HttpRequestException("connection reset"));

		// Act
		Action act = () => _gate.WaitUntilIdle(_options, "UsrVehicle");

		// Assert
		act.Should().NotThrow(
			because: "a dropped connection during the wait must not fail a publish the caller legitimately asked for");
		_retryDelay.Received(1).Wait(ODataBuildGate.PollInterval);
		// because: the gate must abandon the remaining budget as soon as a poll can no longer answer
	}

	[Test]
	[Description("Does not record the environment as lacking the status method when the probe faults, so a later publish over a healthy connection probes again.")]
	public void WaitUntilIdle_ShouldNotRememberUnsupported_WhenProbeFaults() {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(
			_ => throw new HttpRequestException("connection reset"),
			_ => false);

		// Act
		_gate.WaitUntilIdle(_options, "UsrVehicle");
		_gate.WaitUntilIdle(_options, "UsrVehicle2");

		// Assert
		ProbeCallCount().Should().Be(2,
			because: "a dropped connection says nothing about whether the deployed platform exposes the status method, unlike an HTML answer");
	}

	[TestCaseSource(nameof(UnexpectedProbeFaults))]
	[Description("Absorbs a fault OUTSIDE the ODataBuildFaults allow-list too: the gate runs before the publisher's try block, so anything it lets escape strands a schema that is already saved.")]
	public void WaitUntilIdle_ShouldWarnAndReturn_WhenTheProbeFaultIsNotAnExpectedEnvironmentFault(Exception fault) {
		// Arrange
		_client.TryGetIsODataBuildRunning(_options).Returns(_ => throw fault);

		// Act
		Action act = () => _gate.WaitUntilIdle(_options, "UsrVehicle");

		// Assert
		act.Should().NotThrow(
			because: "a TimeoutException, a re-auth failure or a programming error in the probe must not be the thing that leaves a persisted schema unpublished with a raw exception instead of the publisher's actionable message");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("Could not read the OData entities build status", StringComparison.Ordinal)
			&& message.Contains("unexpected " + fault.GetType().Name, StringComparison.Ordinal)));
		// because: absorbing a fault outside the allow-list must not disguise a real defect as an ordinary busy environment
	}

	[Test]
	[Description("A second WaitUntilIdle on the same gate does not re-probe an environment that already answered that it has no status method.")]
	public void WaitUntilIdle_ShouldNotProbeAgain_WhenTheSameGateAlreadyLearnedTheMethodIsMissing() {
		// Arrange - null is the 'no such method' answer, the one result the gate is allowed to remember.
		_client.TryGetIsODataBuildRunning(_options).Returns((bool?)null);

		// Act
		_gate.WaitUntilIdle(_options, "UsrVehicle");
		_gate.WaitUntilIdle(_options, "UsrVehicle2");

		// Assert
		ProbeCallCount().Should().Be(1,
			because: "the flag is per-gate state, and the gate is resolved per command, so this is the only reuse it can actually see");
	}

	// Counts the probe calls through the recorded-calls API rather than NSubstitute's Received(),
	// so the expectation reads as a plain assertion on an observed number.
	private int ProbeCallCount() => _client.ReceivedCalls()
		.Count(call => call.GetMethodInfo().Name
			== nameof(IRemoteEntitySchemaDesignerClient.TryGetIsODataBuildRunning));

	private static readonly object[] UnexpectedProbeFaults = [
		new object[] { new TimeoutException("the status request did not complete in time") },
		new object[] { new UnauthorizedAccessException("re-authentication failed") },
		new object[] { new NullReferenceException("object reference not set") }
	];

	private static readonly object[] ProbeFaults = [
		new object[] { new HttpRequestException("connection reset") },
		new object[] { new NonJsonServiceResponseException("<html>404</html>") },
		new object[] { new InvalidOperationException("IsODataBuildRunning failed: success=false") },
		new object[] { new AggregateException(new SocketException()) }
	];
}
