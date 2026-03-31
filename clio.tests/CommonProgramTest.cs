using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[NonParallelizable]
internal class CommonProgramTest : BaseClioModuleTests{
	private TextWriter _originalConsoleError;
	private TextWriter _originalConsoleOut;

	#region Methods: Public

	public override void Setup() {
		base.Setup();
		_originalConsoleOut = Console.Out;
		_originalConsoleError = Console.Error;
		Console.SetOut(TextWriter.Null);
		Console.SetError(TextWriter.Null);
	}

	public override void TearDown() {
		Console.SetOut(_originalConsoleOut);
		Console.SetError(_originalConsoleError);
		base.TearDown();
	}

	[Test]
	public void ApplyEnvManifestOptionsTest() {
		EnvironmentOptions optionsFromFile = new() {
			Uri = "http://file",
			Login = "fileLogin",
			Password = "filePassword",
			ClientId = "fileClientId",
			IsNetCore = true,
			ClientSecret = "fileClientSecret",
			AuthAppUri = "fileAuthAppUri"
		};
		EnvironmentOptions optionsFromCommandLine = new() {
			Environment = "myEnv"
		};
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
		optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
		optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
		optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
	}

	[Test]
	public void ApplyEnvManifestOptionsWhenOptionInFileNullAndCommandLineIsEmpty() {
		EnvironmentOptions optionsFromFile = null;
		EnvironmentOptions optionsFromCommandLine = new();
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
		optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
		optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
		optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
	}

	[Test]
	public void ApplyEnvManifestOptionsWhenOptionInFileNullAndCommandLineOptionsIsNullTest() {
		EnvironmentOptions optionsFromFile = null;
		EnvironmentOptions optionsFromCommandLine = null;
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		resultOptions.Should().BeNull();
	}

	[Test]
	public void ApplyEnvManifestOptionsWhenOptionInFileNullTest() {
		EnvironmentOptions optionsFromFile = null;
		EnvironmentOptions optionsFromCommandLine = new() {
			Environment = "myEnv"
		};
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
		optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
		optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
		optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
	}

	[Test]
	public void ApplyManifestOptionsOnlyFromFileTest() {
		EnvironmentOptions optionsFromFile = new() {
			Uri = "http://file",
			Login = "fileLogin",
			Password = "filePassword",
			ClientId = "fileClientId",
			IsNetCore = true,
			ClientSecret = "fileClientSecret",
			AuthAppUri = "fileAuthAppUri"
		};
		EnvironmentOptions optionsFromCommandLine = new();
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromFile.Uri.Should().Be(resultOptions.Uri);
		optionsFromFile.Login.Should().Be(resultOptions.Login);
		optionsFromFile.Password.Should().Be(resultOptions.Password);
		optionsFromFile.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromFile.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromFile.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
		optionsFromFile.IsNetCore.Should().Be(resultOptions.IsNetCore);
	}

	[Test]
	public void ApplyManifestOptionsTest() {
		EnvironmentOptions optionsFromFile = new() {
			Uri = "http://file"
		};
		EnvironmentOptions optionsFromCommandLine = new() {
			Uri = "http://commandline"
		};
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		resultOptions.Uri.Should().Be("http://commandline");
	}

	[Test]
	public void IsCfgOpenCommand_WithCfgAndDifferentSubcommand_ShouldBeFalse() {
		// Arrange
		string[] args = ["cfg", "other"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeFalse("because 'cfg' was provided with a subcommand different from 'open'");
	}

	[Test]
	public void IsCfgOpenCommand_WithCfgOpenArguments_ShouldBeTrue() {
		// Arrange
		string[] args = ["cfg", "open"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeTrue("because 'cfg' and 'open' arguments were provided");
	}

	[Test]
	public void IsCfgOpenCommand_WithDifferentArguments_ShouldBeFalse() {
		// Arrange
		string[] args = ["other", "command"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should()
			   .BeFalse("because different arguments were provided instead of 'cfg' and 'open'");
	}

	[Test]
	public void IsCfgOpenCommand_WithEmptyArguments_ShouldBeFalse() {
		// Arrange
		string[] args = [];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeFalse("because no arguments were provided");
	}

	[Test]
	public void IsCfgOpenCommand_WithOnlyCfgArgument_ShouldBeFalse() {
		// Arrange
		string[] args = ["cfg"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeFalse("because only 'cfg' argument was provided without 'open'");
	}

	[Test]
	[Description("Sets MCP mode when the invoked command is the MCP alias even if debug flags precede it.")]
	public void IsMcpServerMode_WithDebugFlagAndMcpAlias_ShouldBeTrue() {
		string[] args = ["--debug", "mcp", "--help"];
		Program.Main(args);
		Program.IsMcpServerMode.Should().BeTrue("because MCP mode should follow the invoked command name instead of scanning all argument values");
	}

	[Test]
	[Description("Keeps MCP mode disabled when mcp appears only as a non-command argument value.")]
	public void IsMcpServerMode_WithNonMcpCommandAndMcpArgumentValue_ShouldBeFalse() {
		string[] args = ["other", "mcp"];
		Program.Main(args);
		Program.IsMcpServerMode.Should().BeFalse("because only the invoked command should enable MCP mode");
	}

	[Test]
	[Description("Prints up to three suggestions and help hints for an unknown top-level command.")]
	public void ExecuteCommands_WithUnknownVerb_ShouldPrintSuggestionsAndKeepExitCodeOne() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["get-list"];

		int exitCode = Program.ExecuteCommands(args);
		string[] outputLines = consoleOutput.ToString()
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		string[] suggestionLines = outputLines
			.Where(line => line.StartsWith("  clio ", StringComparison.Ordinal))
			.ToArray();

		exitCode.Should().Be(1, because: "an unknown command must still fail the invocation");
		outputLines.Should().Contain("Maybe you meant:",
			because: "the parse-error flow should append recovery suggestions for unknown verbs");
		suggestionLines.Should().HaveCount(3,
			because: "the compact unknown-command UX should show at most three command suggestions");
		suggestionLines.Should().Contain("  clio get-app-list",
			because: "the closest visible list command should be suggested");
		suggestionLines.Should().Contain("  clio get-pkg-list",
			because: "commands sharing the same get/list intent should be suggested");
		outputLines.Should().Contain("See all commands: clio help",
			because: "the user should get a generic recovery path after an unknown command");
		outputLines.Should().Contain("See command help: clio <command> --help",
			because: "the output should point the user to command-level help after suggestions");
	}

	[Test]
	[Description("Uses aliases for ranking but prints the canonical command name in suggestions.")]
	public void ExecuteCommands_WithAliasLikeInput_ShouldSuggestCanonicalCommandName() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["envss"];

		Program.ExecuteCommands(args);
		string[] outputLines = consoleOutput.ToString()
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		string firstSuggestion = outputLines.First(line => line.StartsWith("  clio ", StringComparison.Ordinal));

		firstSuggestion.Should().Be("  clio show-web-app-list",
			because: "alias similarity should rank the environment listing command first while output stays canonical");
		outputLines.Should().NotContain("  clio envs",
			because: "the CLI should display canonical command names instead of aliases in suggestion output");
	}

	[Test]
	[Description("Excludes hidden commands from the unknown-command suggestions.")]
	public void ExecuteCommands_WithHiddenCommandAlias_ShouldNotSuggestHiddenCommand() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["execc"];

		Program.ExecuteCommands(args);
		string output = consoleOutput.ToString();

		output.Should().NotContain("execute-assembly-code",
			because: "hidden commands must stay undiscoverable in the suggestion output");
		output.Should().NotContain("Maybe you meant:",
			because: "an exact match to a hidden alias should not produce visible suggestions");
		output.Should().Contain("See all commands: clio help",
			because: "generic recovery hints should still be shown when no suggestion is safe to display");
	}

	[Test]
	[Description("Falls back to generic help when the input is too dissimilar to any visible command.")]
	public void ExecuteCommands_WithLowConfidenceUnknownVerb_ShouldShowOnlyHelpHints() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["zzzzzz"];

		Program.ExecuteCommands(args);
		string output = consoleOutput.ToString();

		output.Should().NotContain("Maybe you meant:",
			because: "low-confidence input should not produce misleading command suggestions");
		output.Should().Contain("See all commands: clio help",
			because: "the CLI should still offer a generic way to recover from a bad command");
		output.Should().Contain("See command help: clio <command> --help",
			because: "generic recovery guidance should remain available without specific suggestions");
	}

	[Test]
	public void ReadEnvironmentOptionsFromManifestFile() {
		FileSystem.MockExamplesFolder("deployments-manifest");
		string manifestFileName = "full-creatio-config.yaml";
		IEnvironmentManager environmentManager = Container.GetRequiredService<IEnvironmentManager>();
		string manifestFilePath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), manifestFileName);
		EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
		FileSystem commonFileSystem = new(FileSystem);
		EnvironmentOptions environmentOptionsFromManifestFile
			= Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
		envSettingsFromFile.Uri.Should().Be(environmentOptionsFromManifestFile.Uri);
		envSettingsFromFile.Login.Should().Be(environmentOptionsFromManifestFile.Login);
		envSettingsFromFile.Password.Should().Be(environmentOptionsFromManifestFile.Password);
	}

	[Test]
	public void ReadEnvironmentOptionsFromOnlySettingsManifestFile() {
		FileSystem.MockExamplesFolder("deployments-manifest");
		string manifestFileName = "only-settings.yaml";
		IEnvironmentManager environmentManager = Container.GetRequiredService<IEnvironmentManager>();
		string manifestFilePath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), manifestFileName);
		EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
		FileSystem commonFileSystem = new(FileSystem);
		EnvironmentOptions environmentOptionsFromManifestFile
			= Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
		environmentOptionsFromManifestFile.Should().BeNull();
	}

	#endregion
}
