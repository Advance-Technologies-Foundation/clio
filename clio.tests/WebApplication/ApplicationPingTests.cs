using System;
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
}
