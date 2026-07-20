using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ServerReadinessWaiterTests {

	private static HealthCheckCommand CreateHealthCheckCommand() =>
		Substitute.For<HealthCheckCommand>(
			Substitute.For<IApplicationClient>(),
			new EnvironmentSettings(),
			Substitute.For<IJsonResponseFormater>());

	[Test]
	[Description("Returns true immediately once the first health-check probe succeeds, after the initial delay.")]
	public void WaitForReady_Should_ReturnTrue_WhenFirstProbeSucceeds() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = new(healthCheckCommand, Substitute.For<ILogger>()) {
			Sleep = _ => { }
		};

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(30)
		});

		// Assert
		ready.Should().BeTrue(because: "a zero exit code from the health-check probe means the instance answered");
		healthCheckCommand.Received(1).Execute(Arg.Any<HealthCheckOptions>());
	}

	[Test]
	[Description("Retries on early failed probes and returns true once a later probe succeeds, within the timeout budget.")]
	public void WaitForReady_Should_RetryThenSucceed_WhenEarlyProbesFail() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		Queue<int> results = new([1, 1, 0]);
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(_ => results.Dequeue());
		ServerReadinessWaiter waiter = new(healthCheckCommand, Substitute.For<ILogger>()) {
			Sleep = _ => { }
		};

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = false, Timeout = TimeSpan.FromSeconds(30)
		});

		// Assert
		ready.Should().BeTrue(because: "the third probe succeeded within the timeout budget");
		healthCheckCommand.Received(3).Execute(Arg.Any<HealthCheckOptions>());
	}

	[Test]
	[Description("Returns false and stops polling once the timeout budget elapses without a successful probe.")]
	public void WaitForReady_Should_ReturnFalse_WhenTimeoutElapsesBeforeReady() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(1);
		ILogger logger = Substitute.For<ILogger>();
		ServerReadinessWaiter waiter = new(healthCheckCommand, logger) {
			// No-op sleep: the loop still respects the REAL wall-clock Timeout below, so this
			// terminates in well under a second without a real Thread.Sleep in the test.
			Sleep = _ => { }
		};

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromMilliseconds(50)
		});

		// Assert
		ready.Should().BeFalse(because: "every probe failed and the timeout budget was exhausted");
		logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("did not become ready")));
	}

	[Test]
	[Description("Waits the configured initial delay before the very first probe, giving the previous app domain time to unload.")]
	public void WaitForReady_Should_Sleep_InitialDelay_Before_First_Probe() {
		// Arrange
		List<string> callOrder = [];
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(_ => {
			callOrder.Add("execute");
			return 0;
		});
		ServerReadinessWaiter waiter = new(healthCheckCommand, Substitute.For<ILogger>()) {
			Sleep = _ => callOrder.Add("sleep")
		};

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(30)
		});

		// Assert
		callOrder.Should().Equal(["sleep", "execute"],
			because: "the initial delay must elapse before the first readiness probe is attempted");
	}

	[Test]
	[Description("Forwards the requested Uri and IsNetCore to each health-check probe unchanged.")]
	public void WaitForReady_Should_Propagate_Uri_And_IsNetCore_To_HealthCheckOptions() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = new(healthCheckCommand, Substitute.For<ILogger>()) {
			Sleep = _ => { }
		};

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://my-env.local", IsNetCore = false, Timeout = TimeSpan.FromSeconds(30)
		});

		// Assert
		healthCheckCommand.Received(1).Execute(Arg.Is<HealthCheckOptions>(options =>
			options.Uri == "http://my-env.local" && options.IsNetCore == false));
	}
}
