using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Mcp.E2E;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Behavioural guard over the destructive-opt-in gate the DB-first data-binding arrange step runs
/// through: with the opt-in off nothing that reaches a Creatio stand may be invoked.
/// </summary>
/// <remarks>
/// This replaces a source-text ordering assertion over <c>DataBindingDbFixtureBase.cs</c>. That guard
/// matched comments and string literals too, so an implementation that moved the real check after the
/// first stand touch while leaving a matching comment in front of it still passed. Here the gate is
/// executed with recording delegates instead, so the order is measured rather than read.
/// </remarks>
[TestFixture]
[Category("Unit")]
public sealed class McpDestructiveArrangeGateTests {

	private const string DeniedMarker = "denied";

	[Test]
	[Description("With the destructive opt-in off, the gate denies the run and invokes neither the clio-executable resolver, nor the stand-pinging environment resolver, nor the remaining arrange steps.")]
	public async Task RunAsync_ShouldTouchNothing_WhenTheDestructiveOptInIsOff() {
		// Arrange
		McpE2ESettings settings = new() { AllowDestructiveMcpTests = false };
		List<string> calls = [];
		DestructiveArrangeSteps steps = RecordingSteps(calls, denyThrows: true);

		// Act
		Func<Task> act = () => DestructiveArrangeGate.RunAsync(
			settings,
			touchesStand: true,
			steps,
			environmentName => {
				calls.Add("remaining-steps");
				return Task.FromResult("context");
			});

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*" + DeniedMarker + "*",
				because: "the gate has to end the run through the injected deny hook instead of returning a context");
		calls.Should().Equal(["is-authorized", "deny"],
			because: "nothing that reaches the stand - not the clio-executable resolver, not the ping-app environment probe, not the arrange commands - may run while the opt-in is off");
		settings.ClioProcessPath.Should().BeNull(
			because: "resolving the executable builds clio locally in preparation for spawning it against the stand, so it belongs after the gate too");
	}

	[Test]
	[Description("The gate still fails closed when the injected deny hook returns instead of throwing, so the guarantee does not depend on Assert.Ignore's behaviour.")]
	public async Task RunAsync_ShouldStillFailClosed_WhenTheDenyHookReturns() {
		// Arrange
		McpE2ESettings settings = new() { AllowDestructiveMcpTests = false };
		List<string> calls = [];
		DestructiveArrangeSteps steps = RecordingSteps(calls, denyThrows: false);

		// Act
		Func<Task> act = () => DestructiveArrangeGate.RunAsync(
			settings,
			touchesStand: true,
			steps,
			environmentName => {
				calls.Add("remaining-steps");
				return Task.FromResult("context");
			});

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*AllowDestructiveMcpTests*",
				because: "a deny hook that returns must not let the arrange step fall through to the stand, and the failure has to name the switch that turns the run on");
		calls.Should().Equal(["is-authorized", "deny"],
			because: "the denial is final regardless of what the deny hook does");
		settings.ClioProcessPath.Should().BeNull(
			because: "nothing that prepares a clio process may run once the gate denied the arrange step");
	}

	[Test]
	[Description("With the destructive opt-in on, the gate authorizes first and only then resolves the executable, the environment and the remaining arrange steps.")]
	public async Task RunAsync_ShouldAuthorizeBeforeEveryStandTouch_WhenTheOptInIsOn() {
		// Arrange
		McpE2ESettings settings = new() { AllowDestructiveMcpTests = true };
		List<string> calls = [];
		DestructiveArrangeSteps steps = RecordingSteps(calls, denyThrows: true);
		string? environmentPassedOn = null;

		// Act
		string context = await DestructiveArrangeGate.RunAsync(
			settings,
			touchesStand: true,
			steps,
			environmentName => {
				calls.Add("remaining-steps");
				environmentPassedOn = environmentName;
				return Task.FromResult("context");
			});

		// Assert
		context.Should().Be("context",
			because: "an authorized arrange step returns the context its caller owns from here on");
		calls.Should().Equal(["is-authorized", "resolve-clio-process-path", "resolve-environment", "remaining-steps"],
			because: "the opt-in decision has to precede the executable lookup, the ping-app probe and the first clio command");
		environmentPassedOn.Should().Be("sandbox-environment",
			because: "the remaining arrange steps run against the environment the gate resolved");
		settings.ClioProcessPath.Should().Be("clio-process-path",
			because: "the arrange step spawns the freshly resolved executable, so the gate writes it back into the settings it hands on");
	}

	[Test]
	[Description("An arrange step that never reaches a stand skips the environment probe and receives no environment name.")]
	public async Task RunAsync_ShouldSkipTheEnvironmentProbe_WhenTheArrangeStepNeverReachesAStand() {
		// Arrange
		McpE2ESettings settings = new() { AllowDestructiveMcpTests = false };
		List<string> calls = [];
		DestructiveArrangeSteps steps = RecordingSteps(calls, denyThrows: true);
		string? environmentPassedOn = "not-assigned";

		// Act
		string context = await DestructiveArrangeGate.RunAsync(
			settings,
			touchesStand: false,
			steps,
			environmentName => {
				calls.Add("remaining-steps");
				environmentPassedOn = environmentName;
				return Task.FromResult("context");
			});

		// Assert
		context.Should().Be("context",
			because: "an arrange step that never reaches a stand needs no opt-in and runs to completion");
		calls.Should().Equal(["is-authorized", "resolve-clio-process-path", "remaining-steps"],
			because: "pinging an environment is itself a stand touch, so it must not happen for a stand-free arrange step");
		environmentPassedOn.Should().BeNull(
			because: "there is no environment to hand on when none was resolved");
	}

	private static DestructiveArrangeSteps RecordingSteps(List<string> calls, bool denyThrows) =>
		new(
			(touchesStand, allowDestructiveMcpTests) => {
				calls.Add("is-authorized");
				return DestructiveStandAuthorization.IsAuthorized(touchesStand, allowDestructiveMcpTests);
			},
			message => {
				calls.Add("deny");
				if (denyThrows) {
					//Assert.Ignore throws; a test double that throws its own exception keeps this fixture
					//out of the Skipped bucket the pre-merge lane gate counts.
					throw new InvalidOperationException(DeniedMarker + ": " + message);
				}
			},
			() => {
				calls.Add("resolve-clio-process-path");
				return "clio-process-path";
			},
			_ => {
				calls.Add("resolve-environment");
				return Task.FromResult("sandbox-environment");
			});
}
