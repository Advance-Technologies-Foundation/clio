using System;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class ToolCommandResolverTests {

	[Test]
	[Description("Rejects unknown environment names instead of resolving MCP commands against default localhost settings.")]
	[Category("Unit")]
	public void Resolve_Should_Reject_Unknown_Environment_Name() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists("missing-env").Returns(false);
		ToolCommandResolver resolver = new(settingsRepository);
		EnvironmentOptions options = new() {
			Environment = "missing-env"
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*missing-env*",
				"because resolver-based MCP commands must not fall back to default localhost credentials");
	}
}
