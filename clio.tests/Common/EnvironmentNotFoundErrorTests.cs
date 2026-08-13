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
		string message = EnvironmentNotFoundError.Build("prod", (IEnumerable<string>?)null, isMcpContext: false);

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
		string message = EnvironmentNotFoundError.Build("prod", available, isMcpContext: false);

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
		string message = EnvironmentNotFoundError.Build("prod", [], isMcpContext: false);

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
		string message = EnvironmentNotFoundError.Build("prod", settingsRepository, isMcpContext: false);

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
		string message = EnvironmentNotFoundError.Build("prod", settingsRepository, isMcpContext: false);

		// Assert
		message.Should().Contain("No environments are registered",
			because: "a failure while enumerating environments must never hide the not-found error");
		message.Should().Contain("clio reg-web-app prod",
			because: "the actionable fix must still be present even when environment enumeration fails");
	}

	[Test]
	[Description("Build routes the caller to clio-run reg-web-app and names the loaded-copy limitation in an MCP context.")]
	public void Build_SuggestsClioRunRegWebApp_WhenMcpContext() {
		// Arrange
		IEnumerable<string> available = ["dev"];

		// Act
		string message = EnvironmentNotFoundError.Build("prod", available, isMcpContext: true);

		// Assert
		message.Should().Contain("clio-run",
			because: "only clio-run reaches reg-web-app inside the running MCP server, so the file and the "
				+ "server state move together");
		message.Should().Contain("\"command\":\"reg-web-app\"").And.Contain("\"environment-name\":\"prod\"",
			because: "an agent must be able to copy the clio-run arguments without guessing their names");
		message.Should().Contain("loaded from appsettings.json at start",
			because: "the message must name the loaded copy, otherwise an agent edits the file from another "
				+ "process and keeps retrying the same failing call");
		message.Should().NotContain("clio reg-web-app prod -u",
			because: "the shell command registers the environment in another process and would send an "
				+ "MCP caller down the exact loop this message exists to prevent");
	}

	[Test]
	[Description("Build keeps the shell reg-web-app command and omits MCP-only guidance in a CLI context.")]
	public void Build_KeepsShellCommand_WhenCliContext() {
		// Arrange

		// Act
		string message = EnvironmentNotFoundError.Build("prod", ["dev"], isMcpContext: false);

		// Assert
		message.Should().Contain("clio reg-web-app prod",
			because: "a shell user needs the command they can paste into their terminal");
		message.Should().NotContain("clio-run",
			because: "clio-run is an MCP tool and means nothing on the command line");
		message.Should().NotContain("loaded from appsettings.json at start",
			because: "a CLI process reads settings once per command, so the loaded-copy caveat does not apply");
	}
}
