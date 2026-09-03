using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// The genuinely I/O-free half of the new-pkg coverage: container wiring only, no package is created and
/// no file is touched, so these stay in the Unit tier that runs on every push. Everything that actually
/// runs the command lives in <see cref="NewPkgCommandFileSystemTestCase"/>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class NewPkgCommandTestCase
{

	[Test]
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

	[Test]
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

	[Test]
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

}

/// <summary>
/// Integration, not Unit: these tests run <see cref="NewPkgCommand.Execute"/>, which creates a real
/// package on disk. The Unit tier is defined as no I/O and no external dependencies.
/// </summary>
/// <remarks>
/// The command writes the package into the process working directory, so the fixture moves that
/// directory into a per-test scratch root. Nothing is deleted by name any more: an earlier shape removed
/// fixed names such as <c>Test</c> from wherever the test host happened to run, which would have deleted
/// a pre-existing directory of the same name together with its contents. The fixture now deletes exactly
/// the root it created, and <c>[NonParallelizable]</c> keeps the working-directory move from reaching
/// tests running alongside it.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
[Property("Module", "Command")]
public class NewPkgCommandFileSystemTestCase
{

	#region Setup/Teardown

	[SetUp]
	public void SetUp(){
		_scratchRoot = Path.Combine(TestContext.CurrentContext.TestDirectory,
			$"new-pkg-{TestContext.CurrentContext.Test.ID}");
		Directory.CreateDirectory(_scratchRoot);
		_originalCurrentDirectory = Directory.GetCurrentDirectory();
		Directory.SetCurrentDirectory(_scratchRoot);
	}

	[TearDown]
	public void TearDown(){
		Directory.SetCurrentDirectory(_originalCurrentDirectory);
		if (Directory.Exists(_scratchRoot)) {
			Directory.Delete(_scratchRoot, true);
		}
	}

	#endregion

	#region Fields: Private

	private string _originalCurrentDirectory;
	private string _scratchRoot;

	#endregion

	#region Methods: Private

	private static NewPkgCommand BuildCommand(Command<ReferenceOptions> referenceCommand, ILogger logger){
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment().Returns(new EnvironmentSettings {
			Maintainer = "TestMaintainer"
		});
		return new NewPkgCommand(settingsRepository, referenceCommand, logger);
	}

	#endregion

	[Test]
	[Ignore("unstable behavior in CI, needs refactoring")]
	[Description("Creates the package directory in the working directory")]
	public void Execute_CreatesNewPackageInFileSystem() {
		// Arrange
		NewPkgCommand command = BuildCommand(
			Substitute.For<Command<ReferenceOptions>>(), Substitute.For<ILogger>());
		NewPkgOptions options = new() { Name = "Test" };

		// Act
		command.Execute(options);

		// Assert
		Directory.Exists(Path.Combine(_scratchRoot, options.Name)).Should().BeTrue(
			because: "new-pkg creates the package directory under the working directory");
	}

	[Test]
	[Description("Invokes the reference command with the requested reference type when -r is not nuget")]
	public void Execute_ChangesReferences_WhenRebaseSpecifiedAndNotEqualsToNuget() {
		// Arrange
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		NewPkgCommand command = BuildCommand(referenceCommand, Substitute.For<ILogger>());
		NewPkgOptions options = new() { Name = "Test", Rebase = "src" };

		// Act
		command.Execute(options);

		// Assert
		referenceCommand.Received(1).Execute(Arg.Is<ReferenceOptions>(e => e.ReferenceType == options.Rebase));
	}

	[Test]
	[Description("Execute should return 1 and log only the message (no stack trace) when exception occurs in normal mode")]
	public void Execute_ShouldLogMessageOnly_WhenExceptionOccurs_InNormalMode() {
		// Arrange
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		referenceCommand.Execute(Arg.Any<ReferenceOptions>()).Returns(_ => throw new Exception("ref error"));
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = BuildCommand(referenceCommand, logger);

		try {
			// Act
			int result = command.Execute(new NewPkgOptions { Name = "TestNewPkgNormalMode", Rebase = "src" });

			// Assert
			result.Should().Be(1, because: "a failing reference command must not report success");
			logger.Received(1).WriteError("ref error");
			logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log full stack trace when exception occurs in debug mode")]
	public void Execute_ShouldLogFullStackTrace_WhenExceptionOccurs_InDebugMode() {
		// Arrange
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		referenceCommand.Execute(Arg.Any<ReferenceOptions>()).Returns(_ => throw new Exception("ref error"));
		ILogger logger = Substitute.For<ILogger>();
		NewPkgCommand command = BuildCommand(referenceCommand, logger);

		try {
			// Act
			command.Execute(new NewPkgOptions { Name = "TestNewPkgDebugMode", Rebase = "src" });

			// Assert
			//NSubstitute's Received takes no because-text; the reason is stated here instead:
			//debug mode must surface the stack trace.
			logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Passes the package project file to the reference command, not the package directory (issue 1279)")]
	public void Execute_PassesProjectFilePath_ToReferenceCommand() {
		// Arrange
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		NewPkgCommand command = BuildCommand(referenceCommand, Substitute.For<ILogger>());
		NewPkgOptions options = new() { Name = "TestProjectFilePathPkg", Rebase = "src" };
		string packagePath = Path.Combine(_scratchRoot, options.Name);
		ReferenceOptions captured = null;
		referenceCommand.When(c => c.Execute(Arg.Any<ReferenceOptions>()))
			.Do(ci => captured = ci.ArgAt<ReferenceOptions>(0));

		// Act
		command.Execute(options);

		// Assert
		referenceCommand.Received(1).Execute(Arg.Any<ReferenceOptions>());
		captured.Should().NotBeNull(because: "the reference command must be invoked for -r/--References");
		captured.Path.Should().Be(
			Path.Combine(packagePath, $"{options.Name}.csproj"),
			because: "the reference command loads the path with XElement.Load; a directory "
				+ "raises UnauthorizedAccessException, reported as 'Access to the path is denied'");
	}

	[Test]
	[Description("Returns the reference command's exit code and keeps packages.config when rebasing fails (issue 1279)")]
	public void Execute_ReturnsFailure_AndKeepsPackagesConfig_WhenReferenceCommandFails() {
		// Arrange
		Command<ReferenceOptions> referenceCommand = Substitute.For<Command<ReferenceOptions>>();
		referenceCommand.Execute(Arg.Any<ReferenceOptions>()).Returns(1);
		NewPkgCommand command = BuildCommand(referenceCommand, Substitute.For<ILogger>());
		NewPkgOptions options = new() { Name = "TestRefFailurePkg", Rebase = "unsupported" };
		string packagePath = Path.Combine(_scratchRoot, options.Name);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "an unsupported reference type reported the error, then logged Done and exited 0, "
				+ "which told the caller the package was ready");
		File.Exists(Path.Combine(packagePath, "packages.config")).Should().BeTrue(
			because: "packages.config must survive a failed rebase, or the package is left with neither "
				+ "reference form");
	}

	[Test]
	[Description("Creates no directory whose name contains a literal backslash; on Windows both "
		+ "separators mean the same nested path, so this only bites on macOS and Linux (issue 1279)")]
	public void Execute_CreatesNestedFilesCsDirectory() {
		// Arrange
		NewPkgCommand command = BuildCommand(
			Substitute.For<Command<ReferenceOptions>>(), Substitute.For<ILogger>());
		string packageName = "TestNestedCsPkg";
		string packagePath = Path.Combine(_scratchRoot, packageName);

		// Act
		command.Execute(new NewPkgOptions { Name = packageName });

		// Assert
		//CreateEmptyClass creates Files/cs through Path.Combine anyway, so only the
		//placeholder proves that CreatePackageDirectories used the nested path
		File.Exists(Path.Combine(packagePath, "Files", "cs", "placeholder.txt")).Should().BeTrue(
			because: "a backslash is a legal file-name character on Unix, so a hard-coded "
				+ "Windows separator put the placeholder in a directory named 'Files\\cs'");
	}

}
