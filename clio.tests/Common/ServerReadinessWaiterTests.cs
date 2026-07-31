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

	private const string SelectUrl = "http://sandbox.local/0/DataService/json/SyncReply/SelectQuery";

	private static HealthCheckCommand CreateHealthCheckCommand() =>
		Substitute.For<HealthCheckCommand>(
			Substitute.For<IApplicationClient>(),
			new EnvironmentSettings(),
			Substitute.For<IJsonResponseFormater>());

	private static ServerReadinessWaiter CreateWaiter(
		HealthCheckCommand healthCheckCommand,
		ILogger logger = null,
		IApplicationClient applicationClient = null,
		IServiceUrlBuilder serviceUrlBuilder = null) =>
		new(healthCheckCommand,
			applicationClient ?? Substitute.For<IApplicationClient>(),
			serviceUrlBuilder ?? Substitute.For<IServiceUrlBuilder>(),
			logger ?? Substitute.For<ILogger>()) {
			Sleep = _ => { }
		};

	[Test]
	[Description("Returns true immediately once the first health-check probe succeeds, after the initial delay.")]
	public void WaitForReady_Should_ReturnTrue_WhenFirstProbeSucceeds() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

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
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

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
		// No-op sleep: the loop still respects the REAL wall-clock Timeout below, so this
		// terminates in well under a second without a real Thread.Sleep in the test.
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand, logger);

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
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);
		waiter.Sleep = _ => callOrder.Add("sleep");

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(30)
		});

		// Assert
		callOrder.Should().Equal(["sleep", "execute"],
			because: "the initial delay must elapse before the first readiness probe is attempted");
	}

	[Test]
	[Description("Probes at least once even when the timeout budget is not greater than the initial delay, so a short --ready-timeout never yields a false negative for a healthy instance.")]
	public void WaitForReady_Should_ProbeAtLeastOnce_WhenTimeoutNotGreaterThanInitialDelay() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

		// Act — Timeout <= InitialDelay used to compute the deadline before the delay elapsed, so the
		// loop was never entered and a healthy instance returned "not ready".
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true,
			InitialDelay = TimeSpan.FromSeconds(10), Timeout = TimeSpan.Zero
		});

		// Assert
		ready.Should().BeTrue(
			because: "the deadline starts after the initial delay and at least one probe always runs when the caller waits");
		healthCheckCommand.Received(1).Execute(Arg.Any<HealthCheckOptions>());
	}

	[Test]
	[Description("Caps a single probe's request timeout to what is left of the readiness budget, so a small waitTimeoutSeconds cannot be overshot by the inherited 100s health-check default.")]
	public void WaitForReady_Should_Cap_ProbeTimeout_To_RemainingBudget() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(5)
		});

		// Assert
		healthCheckCommand.Received(1).Execute(Arg.Is<HealthCheckOptions>(options =>
			options.TimeOut <= 5_000 && options.TimeOut >= 1_000));
		healthCheckCommand.Received(1).Execute(Arg.Is<HealthCheckOptions>(options => options.MaxAttempts == 1));
	}

	[Test]
	[Description("Never lets a probe timeout exceed the inherited 100s default, so a long readiness budget only ever tightens - never loosens - a single request.")]
	public void WaitForReady_Should_Not_Raise_ProbeTimeout_Above_InheritedDefault() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(3600)
		});

		// Assert
		healthCheckCommand.Received(1).Execute(Arg.Is<HealthCheckOptions>(options => options.TimeOut == 100_000));
	}

	[Test]
	[Description("Gives the guaranteed first probe a usable window even when the readiness budget is already exhausted, so a zero timeout does not degenerate into an instant-fail request.")]
	public void WaitForReady_Should_Floor_ProbeTimeout_When_BudgetExhausted() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.Zero
		});

		// Assert
		healthCheckCommand.Received(1).Execute(Arg.Is<HealthCheckOptions>(options => options.TimeOut == 1_000));
	}

	[Test]
	[Description("Forwards the requested Uri and IsNetCore to each health-check probe unchanged.")]
	public void WaitForReady_Should_Propagate_Uri_And_IsNetCore_To_HealthCheckOptions() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand);

		// Act
		waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://my-env.local", IsNetCore = false, Timeout = TimeSpan.FromSeconds(30)
		});

		// Assert
		healthCheckCommand.Received(1).Execute(Arg.Is<HealthCheckOptions>(options =>
			options.Uri == "http://my-env.local" && options.IsNetCore == false));
	}

	// ---- ENG-94417: authenticated application-layer readiness gate (R1) ----

	[Test]
	[Description("When RequireAuthenticatedReadiness is false (the default, e.g. the installer path), a passing liveness probe alone reports ready and no authenticated round-trip is attempted.")]
	public void WaitForReady_Should_NotAttemptAuthRoundTrip_When_AuthReadinessNotRequired() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ServerReadinessWaiter waiter = CreateWaiter(healthCheckCommand, applicationClient: applicationClient);

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(30),
			RequireAuthenticatedReadiness = false
		});

		// Assert
		ready.Should().BeTrue(because: "liveness-only readiness is preserved when the authenticated gate is not requested");
		applicationClient.DidNotReceive().Login();
		applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("R1: with authenticated readiness required, a passing liveness probe is NOT enough — the waiter also logs in and issues an authenticated round-trip, and reports ready only once that round-trip returns a genuine JSON answer.")]
	public void WaitForReady_Should_ReturnTrue_When_AuthRoundTrip_Returns_GenuineJson() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rows\":[{\"Id\":\"1\"}],\"success\":true}");
		ServerReadinessWaiter waiter = CreateWaiter(
			healthCheckCommand, applicationClient: applicationClient, serviceUrlBuilder: serviceUrlBuilder);

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromSeconds(30),
			RequireAuthenticatedReadiness = true
		});

		// Assert
		ready.Should().BeTrue(because: "the authenticated application-layer round-trip returned a genuine JSON answer");
		applicationClient.Received(1).Login();
		applicationClient.Received(1).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("R1 / regression: liveness answers but the authenticated round-trip returns the login page (warm-up), so the waiter keeps polling and reports NOT ready until the deadline — the false-ready shape from the bug.")]
	public void WaitForReady_Should_ReturnFalse_When_LivenessPasses_But_AuthRoundTrip_ReturnsLoginPage() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><head><title>Creatio</title></head><body><form action=\"/Login/NuiLogin.aspx\"></form></body></html>");
		ServerReadinessWaiter waiter = CreateWaiter(
			healthCheckCommand, applicationClient: applicationClient, serviceUrlBuilder: serviceUrlBuilder);

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromMilliseconds(50),
			RequireAuthenticatedReadiness = true
		});

		// Assert
		ready.Should().BeFalse(
			because: "a liveness ping that answers while the app still serves a login page must NOT be reported ready");
		applicationClient.Received().Login();
	}

	[Test]
	[Description("R1 / regression: liveness answers but the authenticated round-trip throws transiently during warm-up, treated as not-ready (keep polling) rather than a hard failure.")]
	public void WaitForReady_Should_ReturnFalse_When_AuthRoundTrip_Throws() {
		// Arrange
		HealthCheckCommand healthCheckCommand = CreateHealthCheckCommand();
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		applicationClient.When(client => client.Login())
			.Do(_ => throw new System.Net.WebException("connection refused during warm-up"));
		ServerReadinessWaiter waiter = CreateWaiter(
			healthCheckCommand, applicationClient: applicationClient, serviceUrlBuilder: serviceUrlBuilder);

		// Act
		bool ready = waiter.WaitForReady(new ServerReadinessOptions {
			Uri = "http://sandbox.local", IsNetCore = true, Timeout = TimeSpan.FromMilliseconds(50),
			RequireAuthenticatedReadiness = true
		});

		// Assert
		ready.Should().BeFalse(because: "a throwing authenticated round-trip during warm-up is not-ready, not a genuine answer");
	}

	[Test]
	[Description("Classifier: a genuine DataService JSON answer is accepted, while a login page, a 401 auth-failure envelope, an empty body, and a non-JSON body are all rejected.")]
	public void IsGenuineAuthenticatedJsonAnswer_Should_Classify_Answers_Correctly() {
		// Assert — genuine JSON answers
		ServerReadinessWaiter.IsGenuineAuthenticatedJsonAnswer("{\"rows\":[],\"success\":true}")
			.Should().BeTrue(because: "a well-formed DataService JSON envelope proves the app answered an authenticated request");
		ServerReadinessWaiter.IsGenuineAuthenticatedJsonAnswer("[]")
			.Should().BeTrue(because: "a JSON array is still genuine JSON from a serving application layer");

		// Assert — warm-up / not-authenticated shapes
		ServerReadinessWaiter.IsGenuineAuthenticatedJsonAnswer(
				"<html><head><title>Login</title></head><body><a href=\"/Login/Login.html\"></a></body></html>")
			.Should().BeFalse(because: "an HTML login page is a warm-up / unauthenticated response, not a genuine answer");
		ServerReadinessWaiter.IsGenuineAuthenticatedJsonAnswer(
				"{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"x\"}")
			.Should().BeFalse(because: "the JSON 401 auth-failure envelope means the session is not authenticated yet");
		ServerReadinessWaiter.IsGenuineAuthenticatedJsonAnswer(string.Empty)
			.Should().BeFalse(because: "an empty body is not a genuine answer");
		ServerReadinessWaiter.IsGenuineAuthenticatedJsonAnswer("not-json")
			.Should().BeFalse(because: "a non-JSON plain-text body is not a genuine DataService answer");
	}
}
