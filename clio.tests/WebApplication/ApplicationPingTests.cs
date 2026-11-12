using System;
using System.Linq;
using Clio.Common;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.WebApplication;

/// <summary>
/// The ping says WHY an environment did not answer, so a caller that stops on a failed ping can report it
/// instead of stopping in silence.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
public sealed class ApplicationPingTests {

	[Test]
	[Description("A rejected login is reported by TryPing with the client's own message, in the CLI's readable rendering, and Ping still answers false for the same environment.")]
	public void TryPing_ShouldReportTheLoginRejection_WhenTheEnvironmentRefusesTheCredentials() {
		// Arrange
		EnvironmentSettings environment = new() { Uri = "http://localhost:5000", IsNetCore = true };
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("Unauthorized Supervisor for http://localhost:5000"));
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateOwnedClient(environment).Returns(client);
		ApplicationPing ping = new(environment, factory, Substitute.For<IServiceUrlBuilder>());

		// Act
		bool answered = ping.TryPing(environment, out string failureReason);
		bool pinged = ping.Ping(environment);

		// Assert
		answered.Should().BeFalse(because: "the environment refused the login");
		failureReason.Should().Contain("Unauthorized Supervisor for http://localhost:5000",
			because: "the reason is the client's own rejection, the one ping-app prints, which names the rejected user");
		pinged.Should().BeFalse(because: "Ping keeps its true/false contract for the callers that only branch on it");
	}

	[Test]
	[Description("An environment that answers is reported as answering, with no failure reason.")]
	public void TryPing_ShouldReportNoFailure_WhenTheEnvironmentAnswers() {
		// Arrange
		EnvironmentSettings environment = new() { Uri = "http://localhost:5000", IsNetCore = true };
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateOwnedClient(environment).Returns(client);
		ApplicationPing ping = new(environment, factory, Substitute.For<IServiceUrlBuilder>());

		// Act
		bool answered = ping.TryPing(environment, out string failureReason);

		// Assert
		answered.Should().BeTrue(because: "the ping request completed");
		failureReason.Should().BeNull(because: "there is nothing to report for an environment that answered");
	}

	[Test]
	[Description("TryPing pings the URL of the environment it is given, not of the one the ping was built for, so pinging another environment checks that environment.")]
	public void TryPing_ShouldPingTheEnvironmentItIsGiven_WhenItDiffersFromTheInjectedOne() {
		// Arrange
		EnvironmentSettings injected = new() { Uri = "http://injected.example", IsNetCore = true };
		EnvironmentSettings target = new() { Uri = "http://target.example", IsNetCore = true };
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateOwnedClient(target).Returns(client);
		ApplicationPing ping = new(injected, factory, Substitute.For<IServiceUrlBuilder>());

		// Act
		ping.TryPing(target, out _);

		// Assert
		client.ReceivedCalls().Should().ContainSingle(call =>
				call.GetMethodInfo().Name == nameof(IOwnedApplicationClient.ExecuteGetRequest)
				&& (string)call.GetArguments()[0] == "http://target.example",
			because: "a ping of the target environment must reach the target's URL, not the injected one");
	}

	[Test]
	[Description("For a .NET Framework environment the ping URL is built for the environment being pinged.")]
	public void TryPing_ShouldBuildTheFrameworkPingUrlForTheEnvironmentItIsGiven() {
		// Arrange
		EnvironmentSettings injected = new() { Uri = "http://injected.example", IsNetCore = false };
		EnvironmentSettings target = new() { Uri = "http://target.example", IsNetCore = false };
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateOwnedClient(target).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build("ping", target).Returns("http://target.example/0/ping");
		ApplicationPing ping = new(injected, factory, urlBuilder);

		// Act
		ping.TryPing(target, out _);

		// Assert
		client.ReceivedCalls().Should().ContainSingle(call =>
				call.GetMethodInfo().Name == nameof(IOwnedApplicationClient.ExecuteGetRequest)
				&& (string)call.GetArguments()[0] == "http://target.example/0/ping",
			because: "the Framework ping route must be built for the environment being pinged");
	}

	[Test]
	[Description("EnsureAnswers names an environment with no URL as not registered and does not ping it, instead of reporting a client-construction failure as a stopped site.")]
	public void EnsureAnswers_ShouldNameTheUnregisteredEnvironment_WhenItHasNoUrl() {
		// Arrange
		IApplicationPing ping = Substitute.For<IApplicationPing>();

		// Act
		Action act = () => ping.EnsureAnswers(new EnvironmentSettings(), "Workspace was not created", "dev");

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "nothing can be created from an environment that is not registered")
			.WithMessage("Workspace was not created: environment 'dev' is not registered. Register it with reg-web-app, or pass its URL with --uri.",
				because: "the message must point at the missing registration, not at a stopped site");
		ping.ReceivedCalls().Should().BeEmpty(because: "there is no URL to ping");
	}

	[Test]
	[Description("EnsureAnswers keeps the reason's own advice and adds only ping-app and credentials, without a doubled period or doubled 'make sure it is running'.")]
	public void EnsureAnswers_ShouldNotRepeatTheAdvice_WhenTheReasonAlreadyCarriesIt() {
		// Arrange
		const string refused = "Cannot connect to the application: Connection refused (127.0.0.1:9) (WebException: ConnectFailure). "
			+ "Make sure the site is running and accessible.";
		EnvironmentSettings environment = new() { Uri = "http://127.0.0.1:9", IsNetCore = true };
		IApplicationPing ping = Substitute.For<IApplicationPing>();
		ping.TryPing(environment, out Arg.Any<string>()).Returns(call => {
			call[1] = refused;
			return false;
		});

		// Act
		Action act = () => ping.EnsureAnswers(environment, "Packages were not downloaded");

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the environment did not answer").Which.Message;
		message.Should().Be("Packages were not downloaded: the environment did not answer (Cannot connect to the application: "
				+ "Connection refused (127.0.0.1:9) (WebException: ConnectFailure). Make sure the site is running and accessible). "
				+ "Check it with ping-app, and correct its registered credentials with reg-web-app if the login is rejected.",
			because: "the reason keeps its own advice, and the tail adds only what it lacks");
		message.Should().NotContain(".).", because: "a reason ending in a period must not leave a '.).' join");
	}
}
