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
	[Description("Prints a compatibility version line for legacy callers that still invoke clio with the root --version flag.")]
	public void Main_WithRootVersionFlag_ShouldReturnZeroAndPrintCompatibilityVersion() {
		// Arrange
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		string[] args = ["--version"];

		// Act
		int exitCode = Program.Main(args);
		string output = consoleOutput.ToString();

		// Assert
		exitCode.Should().Be(0, because: "legacy updaters still invoke the root --version flag after installing a new binary");
		output.Should().NotContain("clio ", because: "older updater builds compare the root --version output to the semantic version string directly");
		output.Should().Contain(".", because: "the compatibility output should include the installed semantic version");
	}

	[Test]
	[Description("Prints up to ten alphabetically sorted suggestions and help hints for an unknown top-level command.")]
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
		suggestionLines.Should().HaveCount(10,
			because: "the expanded unknown-command UX should show up to ten command suggestions when enough matches exist");
		suggestionLines.Should().Equal(suggestionLines.OrderBy(line => line, StringComparer.Ordinal).ToArray(),
			because: "the rendered suggestion list should now be sorted alphabetically for easier scanning");
		suggestionLines.Should().Contain("  clio list-apps",
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
		string[] suggestionLines = outputLines
			.Where(line => line.StartsWith("  clio ", StringComparison.Ordinal))
			.ToArray();

		suggestionLines.Should().Contain("  clio show-web-app-list",
			because: "alias similarity should still surface the environment listing command while output stays canonical");
		suggestionLines.Should().Equal(suggestionLines.OrderBy(line => line, StringComparer.Ordinal).ToArray(),
			because: "alias-driven suggestions should follow the same alphabetical rendering as every other unknown command");
		outputLines.Should().NotContain("  clio envs",
			because: "the CLI should display canonical command names instead of aliases in suggestion output");
	}

	[Test]
	[Description("Downweights short aliases for longer unknown commands so relevant skill commands remain visible.")]
	public void ExecuteCommands_WithSkillInput_ShouldPreferSkillCommandsOverShortAliasMatches() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["skill"];

		Program.ExecuteCommands(args);
		string[] outputLines = consoleOutput.ToString()
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		string[] suggestionLines = outputLines
			.Where(line => line.StartsWith("  clio ", StringComparison.Ordinal))
			.ToArray();

		suggestionLines.Should().Contain("  clio delete-skill",
			because: "the closest singular skill command should remain visible for skill-like input");
		suggestionLines.Should().Contain("  clio install-skills",
			because: "the plural install command should not be pushed out by short alias matches");
		suggestionLines.Should().Contain("  clio update-skill",
			because: "all visible skill management commands should remain discoverable from the shared root word");
		suggestionLines.Should().NotContain("  clio build-workspace",
			because: "low-confidence fallback commands should not fill the list once relevant skill matches are available");
		suggestionLines.Should().NotContain("  clio execute-sql-script",
			because: "short aliases such as sql should not outrank relevant canonical skill commands for longer input");
		suggestionLines.Should().Equal(suggestionLines.OrderBy(line => line, StringComparer.Ordinal).ToArray(),
			because: "the rendered suggestions should still be sorted alphabetically after ranking");
	}

	[Test]
	[Description("Renders flat top-level help with canonical commands sorted alphabetically.")]
	public void ExecuteCommands_WithHelpArgument_ShouldRenderAlphabeticalCanonicalRootHelp() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "the built-in help flow should succeed");
		output.Should().Contain("clio - Creatio CLI",
			because: "the top-level help should start with the product heading");
		output.Should().Contain("Commands:",
			because: "the top-level help should label the flat command list");
		output.Should().NotContain("Application Management",
			because: "the top-level help should now render as one alphabetical list instead of grouped sections");
		output.IndexOf("  activate-pkg", StringComparison.Ordinal).Should()
			.BeLessThan(output.IndexOf("  add-data-binding-row", StringComparison.Ordinal),
				because: "commands should be sorted alphabetically across the full top-level list");
		output.IndexOf("  add-data-binding-row", StringComparison.Ordinal).Should()
			.BeLessThan(output.IndexOf("  add-item", StringComparison.Ordinal),
				because: "commands should be sorted alphabetically across the full top-level list");
		output.IndexOf("  build-docker-image", StringComparison.Ordinal).Should()
			.BeLessThan(output.IndexOf("  build-workspace", StringComparison.Ordinal),
				because: "commands should be sorted alphabetically across the full top-level list");
		output.Should().Contain("  ping-app",
			because: "the top-level help should list canonical command names");
		output.Should().NotContain(Environment.NewLine + "  ping" + Environment.NewLine,
			because: "aliases should not be rendered as separate top-level entries");
		output.Should().NotContain("execute-assembly-code",
			because: "hidden commands must not appear in top-level help");
		output.Should().Contain("Run `clio <command> --help` for command details.",
			because: "the footer should point the user to command-level help");
	}

	[Test]
	[Description("Shows canonical command help when help is requested through an alias.")]
	public void ExecuteCommands_WithAliasHelp_ShouldRenderCanonicalCommandHelp() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["ping", "--help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "asking for help through an alias should still succeed");
		output.Should().Contain("ping-app - Verify connectivity to a Creatio environment",
			because: "alias help should resolve to the canonical command");
		output.Should().Contain("clio ping-app [options]",
			because: "usage should be rendered with the canonical command name");
		output.Should().Contain("clio ping-app -e dev",
			because: "examples should be rendered with the canonical command name");
		output.Should().NotContain("clio ping [options]",
			because: "alias names should not leak into the help contract");
		output.Should().NotContain("-, --timeout",
			because: "options without a short name should be rendered without a dangling short-form placeholder");
	}

	[Test]
	[Description("Renders the rich manual add-item help when command help is requested through the built-in help command.")]
	public void ExecuteCommands_WithBuiltInAddItemHelp_ShouldRenderManualHelpText() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["help", "add-item"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "the built-in help flow should succeed for known commands");
		output.Should().Contain("COMMAND TYPE",
			because: "manual command help should be shown instead of the generated fallback contract");
		output.Should().Contain("DETAIL COLLECTIONS",
			because: "rich manual sections from add-item.txt should survive the runtime help flow");
		output.Should().Contain("MODEL VALIDATION",
			because: "manual explanatory sections should remain visible in command help");
		output.Should().NotContain("ENVIRONMENT OPTIONS",
			because: "manual add-item help should no longer be merged with inherited environment options");
		output.Should().NotContain("REQUIREMENTS",
			because: "manual add-item help should not get a duplicated generated requirements section");
	}

	[Test]
	[Description("Renders the rich manual add-item help when help is requested through the command parser path.")]
	public void ExecuteCommands_WithAddItemHelpSwitch_ShouldRenderManualHelpText() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["add-item", "--help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "the parser-driven help path should succeed for known commands");
		output.Should().Contain("COMMAND TYPE",
			because: "parser-driven help should read the manual txt file when it exists");
		output.Should().Contain("DETAIL COLLECTIONS",
			because: "manual custom sections should remain visible in parser-driven help");
		output.Should().Contain("MODEL VALIDATION",
			because: "manual validation guidance should remain visible in parser-driven help");
		output.Should().NotContain("ENVIRONMENT OPTIONS",
			because: "parser-driven manual help should not append inherited environment options");
		output.Should().NotContain("REQUIREMENTS",
			because: "parser-driven manual help should not append a generated requirements section");
	}

	[Test]
	[Description("Keeps sparse manual help unchanged instead of appending generated syntax sections at runtime.")]
	public void ExecuteCommands_WithSparseManualHelp_ShouldPreferManualHelpOnly() {
		StringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["set-pkg-version", "--help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "the parser-driven help path should succeed for known commands");
		output.Should().Contain("COMMAND TYPE",
			because: "existing manual sections should remain visible in runtime help");
		output.Should().NotContain("ARGUMENTS",
			because: "manual help should stay authoritative even when it is sparse");
		output.Should().NotContain("OPTIONS",
			because: "manual help should no longer be merged with generated options");
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
