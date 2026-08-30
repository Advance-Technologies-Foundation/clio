using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Command")]
public class NewPkgCommandTestCase
{

	[Test, Category("Integration")] 
	[Ignore("unstable behavior in CI, needs refactoring")]
	public void Execute_CreatesNewPackageInFileSystem() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
			Maintainer = "TestMaintainer"
		});
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand, logger);
		NewPkgOptions options = new NewPkgOptions { Name = "Test" };
		command.Execute(options);
		Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), options.Name)).Should().BeTrue();
	}

	[Test, Category("Unit")]
	public void Execute_ChangesReferences_WhenRebaseSpecifiedAndNotEqualsToNuget() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
			Maintainer = "TestMaintainer"
		});
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand, logger);
		NewPkgOptions options = new NewPkgOptions { Name = "Test", Rebase = "src" };
		try {
			command.Execute(options);
			referenceCommand.Received(1).Execute(Arg.Is<ReferenceOptions>(e => e.ReferenceType == options.Rebase));
		} finally {
			DeletePackageDirectory(options.Name);
		}
	}

	private static void DeletePackageDirectory(string packageName){
		string packagePath = Path.Combine(Directory.GetCurrentDirectory(), packageName);
		if (Directory.Exists(packagePath)) {
			Directory.Delete(packagePath, true);
		}
	}

	[Test, Category("Unit")]
	[Description("Execute should return 1 and log only the message (no stack trace) when exception occurs in normal mode")]
	public void Execute_ShouldLogMessageOnly_WhenExceptionOccurs_InNormalMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings { Maintainer = "TestMaintainer" });
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		referenceCommand.Execute(Arg.Any<ReferenceOptions>()).Returns(_ => throw new Exception("ref error"));
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand, logger);
		try {
			int result = command.Execute(new NewPkgOptions { Name = "TestNewPkgNormalMode", Rebase = "src" });

			result.Should().Be(1);
			logger.Received(1).WriteError("ref error");
			logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
			DeletePackageDirectory("TestNewPkgNormalMode");
			DeletePackageDirectory("TestNewPkgDebugMode");
		}
	}

	[Test, Category("Unit")]
	[Description("Execute should log full stack trace when exception occurs in debug mode")]
	public void Execute_ShouldLogFullStackTrace_WhenExceptionOccurs_InDebugMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings { Maintainer = "TestMaintainer" });
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		referenceCommand.Execute(Arg.Any<ReferenceOptions>()).Returns(_ => throw new Exception("ref error"));
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand, logger);
		try {
			command.Execute(new NewPkgOptions { Name = "TestNewPkgDebugMode", Rebase = "src" });

			logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
			DeletePackageDirectory("TestNewPkgNormalMode");
			DeletePackageDirectory("TestNewPkgDebugMode");
		}
	}

	[Test, Category("Unit")]
	[Description("ReferenceCommand must be resolvable from the DI container that Program.cs uses (guards against issue #674 where the command could not be constructed for the ref-to / new-pkg verbs)")]
	public void ReferenceCommand_ShouldResolveFromContainer_WhenRegisteredInBindingsModule() {
		// Arrange — build the same container Program.cs resolves commands from.
		IServiceProvider container = new BindingsModule().Register(new EnvironmentSettings());

		// Act
		ReferenceCommand command = container.GetRequiredService<ReferenceCommand>();

		// Assert
		command.Should().NotBeNull(
			because: "Program.cs resolves ReferenceCommand from DI for the ref-to verb and all of its dependencies must be registered");
	}

	[Test, Category("Unit")]
	[Description("NewPkgCommand must be resolvable from the DI container that Program.cs uses, including its Command<ReferenceOptions> dependency (guards against the wiring regression from issue #674)")]
	public void NewPkgCommand_ShouldResolveFromContainer_WhenRegisteredInBindingsModule() {
		// Arrange — build the same container Program.cs resolves commands from.
		IServiceProvider container = new BindingsModule().Register(new EnvironmentSettings());

		// Act
		NewPkgCommand command = container.GetRequiredService<NewPkgCommand>();

		// Assert
		command.Should().NotBeNull(
			because: "Program.cs resolves NewPkgCommand from DI for the new-pkg verb and its Command<ReferenceOptions> dependency must be registered");
	}

	[Test, Category("Unit")]
	[Description("The Command<ReferenceOptions> dependency that NewPkgCommand requires must resolve to ReferenceCommand (this is the exact registration that broke in issue #674)")]
	public void CommandReferenceOptions_ShouldResolveToReferenceCommand_WhenRegisteredInBindingsModule() {
		// Arrange — build the same container Program.cs resolves commands from.
		IServiceProvider container = new BindingsModule().Register(new EnvironmentSettings());

		// Act
		Command<ReferenceOptions> command = container.GetRequiredService<Command<ReferenceOptions>>();

		// Assert
		command.Should().BeOfType<ReferenceCommand>(
			because: "NewPkgCommand depends on Command<ReferenceOptions> and issue #674 broke exactly this mapping");
	}

	[Test, Category("Unit")]
	[Description("Passes the package project file to the reference command, not the package directory (issue 1279)")]
	public void Execute_PassesProjectFilePath_ToReferenceCommand() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
			Maintainer = "TestMaintainer"
		});
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand, logger);
		NewPkgOptions options = new NewPkgOptions {Name = "TestProjectFilePathPkg", Rebase = "src"};
		string packagePath = Path.Combine(Directory.GetCurrentDirectory(), options.Name);
		ReferenceOptions captured = null;
		referenceCommand.When(c => c.Execute(Arg.Any<ReferenceOptions>()))
			.Do(ci => captured = ci.ArgAt<ReferenceOptions>(0));

		try {
			// Act
			command.Execute(options);

			// Assert
			referenceCommand.Received(1).Execute(Arg.Any<ReferenceOptions>());
			captured.Should().NotBeNull(because: "the reference command must be invoked for -r/--References");
			captured.Path.Should().Be(
				Path.Combine(packagePath, $"{options.Name}.csproj"),
				because: "the reference command loads the path with XElement.Load; a directory "
					+ "raises UnauthorizedAccessException, reported as 'Access to the path is denied'");
		} finally {
			if (Directory.Exists(packagePath)) {
				Directory.Delete(packagePath, true);
			}
		}
	}

	[Test, Category("Unit")]
	[Description("Creates no directory whose name contains a literal backslash; on Windows both "
		+ "separators mean the same nested path, so this only bites on macOS and Linux (issue 1279)")]
	public void Execute_CreatesNestedFilesCsDirectory() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
			Maintainer = "TestMaintainer"
		});
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = new NewPkgCommand(settingsRepository, referenceCommand, logger);
		string packageName = "TestNestedCsPkg";
		string packagePath = Path.Combine(Directory.GetCurrentDirectory(), packageName);

		try {
			// Act
			command.Execute(new NewPkgOptions {Name = packageName});

			// Assert
			//CreateEmptyClass creates Files/cs through Path.Combine anyway, so only the
			//placeholder proves that CreatePackageDirectories used the nested path
			File.Exists(Path.Combine(packagePath, "Files", "cs", "placeholder.txt")).Should().BeTrue(
				because: "a backslash is a legal file-name character on Unix, so a hard-coded "
					+ "Windows separator put the placeholder in a directory named 'Files\\cs'");
		} finally {
			if (Directory.Exists(packagePath)) {
				Directory.Delete(packagePath, true);
			}
		}
	}

}
