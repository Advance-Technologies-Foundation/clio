using System;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Tests.Infrastructure;
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
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"healthy",
			SettingsRepository.AppSettingsFile,
			"dev",
			"dev",
			1,
			[],
			[],
			true,
			true));
		settingsRepository.IsEnvironmentExists("missing-env").Returns(false);
		ToolCommandResolver resolver = new(settingsRepository, settingsBootstrapService);
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

	[Test]
	[Description("Allows explicit URI-based command resolution even when the persisted bootstrap report is broken.")]
	[Category("Unit")]
	public void Resolve_Should_Accept_Explicit_Uri_When_Bootstrap_Report_Is_Broken() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"broken",
			SettingsRepository.AppSettingsFile,
			null,
			null,
			0,
			[new SettingsIssue("settings-file-unreadable", "appsettings.json is unreadable.")],
			[],
			true,
			false));
		ToolCommandResolver resolver = new(settingsRepository, settingsBootstrapService);
		EnvironmentOptions options = new() {
			Uri = "http://localhost",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		try {
			// Act
			Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

			// Assert
			act.Should().NotThrow(
				because: "explicit URI-based MCP execution should stay available even when named-environment bootstrap is broken");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}
}
