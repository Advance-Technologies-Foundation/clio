using System.Collections.Generic;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class EnvironmentNotFoundErrorTests {

	[Test]
	[Description("Build includes the missing environment name and a copy-pasteable reg-web-app command.")]
	public void Build_IncludesMissingNameAndRegWebAppFix_Always() {
		// Arrange

		// Act
		string message = EnvironmentNotFoundError.Build("prod", (IEnumerable<string>?)null);

		// Assert
		message.Should().Contain("prod",
			because: "the message must name the environment that was not found");
		message.Should().Contain("clio reg-web-app prod",
			because: "the fix must be a copy-pasteable reg-web-app command for the missing environment");
		message.Should().Contain("-u <url>").And.Contain("-l <login>").And.Contain("-p <password>",
			because: "the suggested command must spell out the connection flags the user has to fill in");
	}

	[Test]
	[Description("Build lists the available environments when at least one is registered.")]
	public void Build_ListsAvailableEnvironments_WhenPresent() {
		// Arrange
		IEnumerable<string> available = ["qa", "dev"];

		// Act
		string message = EnvironmentNotFoundError.Build("prod", available);

		// Assert
		message.Should().Contain("dev",
			because: "registered environments should be listed so the user can pick an existing one");
		message.Should().Contain("qa",
			because: "every registered environment should be listed");
		message.Should().Contain("list-environments",
			because: "the hint should point at the command that inspects environments");
	}

	[Test]
	[Description("Build states that no environments are registered when the list is empty.")]
	public void Build_StatesNoneRegistered_WhenEmpty() {
		// Arrange

		// Act
		string message = EnvironmentNotFoundError.Build("prod", []);

		// Assert
		message.Should().Contain("No environments are registered",
			because: "an empty configuration should be reported explicitly so the user knows to register one");
	}

	[Test]
	[Description("Build reads the available environment names from the supplied settings repository.")]
	public void Build_UsesSettingsRepositoryNames_WhenRepositoryProvided() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["dev"] = new EnvironmentSettings(),
			["qa"] = new EnvironmentSettings()
		});

		// Act
		string message = EnvironmentNotFoundError.Build("prod", settingsRepository);

		// Assert
		message.Should().Contain("dev").And.Contain("qa",
			because: "the repository overload should enumerate the registered environment names");
		message.Should().Contain("clio reg-web-app prod",
			because: "the repository overload should still append the actionable reg-web-app fix");
	}

	[Test]
	[Description("Build degrades to the no-environments hint when the settings repository throws.")]
	public void Build_DegradesGracefully_WhenSettingsRepositoryThrows() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetAllEnvironments().Returns(_ => throw new System.InvalidOperationException("broken"));

		// Act
		string message = EnvironmentNotFoundError.Build("prod", settingsRepository);

		// Assert
		message.Should().Contain("No environments are registered",
			because: "a failure while enumerating environments must never hide the not-found error");
		message.Should().Contain("clio reg-web-app prod",
			because: "the actionable fix must still be present even when environment enumeration fails");
	}
}
