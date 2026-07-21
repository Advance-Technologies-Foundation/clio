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
[Property("Module", "Core")]
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
		ThreadSafeStringWriter consoleOutput = new();
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
		ThreadSafeStringWriter consoleOutput = new();
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
		suggestionLines.Should().Contain("  clio get-app-list",
			because: "the closest visible list alias should be suggested when it beats the canonical name on token overlap");
		suggestionLines.Should().Contain("  clio get-pkg-list",
			because: "the alias sharing the same get/list intent should be suggested over its canonical name");
		outputLines.Should().Contain("See all commands: clio help",
			because: "the user should get a generic recovery path after an unknown command");
		outputLines.Should().Contain("See command help: clio <command> --help",
			because: "the output should point the user to command-level help after suggestions");
	}

	[Test]
	[Description("Surfaces the shortest matching alias instead of the canonical name when the alias is a closer fit.")]
	public void ExecuteCommands_WithAliasLikeInput_ShouldSuggestShortestMatchingAlias() {
		ThreadSafeStringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["envss"];

		Program.ExecuteCommands(args);
		string[] outputLines = consoleOutput.ToString()
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		string[] suggestionLines = outputLines
			.Where(line => line.StartsWith("  clio ", StringComparison.Ordinal))
			.ToArray();

		suggestionLines.Should().Contain("  clio envs",
			because: "the shortest alias that best matches the typo should be surfaced so the user can pick the concise form");
		suggestionLines.Should().NotContain("  clio list-environments",
			because: "when an alias beats the canonical name on similarity, only the closer alias should be displayed");
		suggestionLines.Should().Equal(suggestionLines.OrderBy(line => line, StringComparer.Ordinal).ToArray(),
			because: "alias-driven suggestions should follow the same alphabetical rendering as every other unknown command");
	}

	[Test]
	[Description("Reflects the alias closest to a typo such as 'encvs' instead of forcing the user to read the canonical name.")]
	public void ExecuteCommands_WithTypoNearShortAlias_ShouldSuggestThatAlias() {
		ThreadSafeStringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);
		string[] args = ["encvs"];

		Program.ExecuteCommands(args);
		string[] outputLines = consoleOutput.ToString()
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		string[] suggestionLines = outputLines
			.Where(line => line.StartsWith("  clio ", StringComparison.Ordinal))
			.ToArray();

		outputLines.Should().Contain("Maybe you meant:",
			because: "an unknown verb that is one edit away from a known alias should still produce a suggestion block");
		suggestionLines.Should().Contain("  clio envs",
			because: "the alias 'envs' is one character away from 'encvs' and should be the surfaced suggestion");
		suggestionLines.Should().NotContain("  clio list-environments",
			because: "the user is closer to the short alias and should not be redirected to the longer canonical form");
	}

	[Test]
	[Description("Downweights short aliases for longer unknown commands so relevant skill commands remain visible.")]
	public void ExecuteCommands_WithSkillInput_ShouldPreferSkillCommandsOverShortAliasMatches() {
		ThreadSafeStringWriter consoleOutput = new();
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
		ThreadSafeStringWriter consoleOutput = new();
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
		ThreadSafeStringWriter consoleOutput = new();
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
		ThreadSafeStringWriter consoleOutput = new();
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
		ThreadSafeStringWriter consoleOutput = new();
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
		ThreadSafeStringWriter consoleOutput = new();
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
	[Description("Renders real command help through the --help switch for a verb whose name is a textual prefix of another registered verb, instead of the previously empty CommandLineSDK dispatch output (ENG-93886).")]
	public void ExecuteCommands_WithCreateDataBindingHelpSwitch_ShouldRenderCommandHelp() {
		ThreadSafeStringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["create-data-binding", "--help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "the parser-driven help path should succeed once the dispatch fix bypasses the CommandLineSDK prefix-collision bug");
		output.Should().NotBeEmpty(because: "create-data-binding --help previously returned zero bytes because CommandLineSDK failed to dispatch help for a verb name that is a prefix of create-data-binding-db");
		output.Should().Contain("create-data-binding - Create or regenerate a package data binding",
			because: "the rendered help should come from the same manual command-help renderer used by `clio help create-data-binding`");
		output.Should().Contain("create-data-binding-db",
			because: "the SEE ALSO section should point to the DB-first alternative so agents discover the working persistence path");
	}

	[Test]
	[Description("Keeps create-data-binding-db --help unchanged so the create-data-binding dispatch fix does not regress its already-working sibling (ENG-93886 AC3).")]
	public void ExecuteCommands_WithCreateDataBindingDbHelpSwitch_ShouldStayUnaffected() {
		ThreadSafeStringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["create-data-binding-db", "--help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(0, because: "create-data-binding-db --help already worked before the fix and must keep working");
		output.Should().Contain("Creates a DB-first package data binding by persisting row data directly to the remote Creatio database",
			because: "the DB-first command description must remain exactly as documented");
	}

	[Test]
	[Description("Confirms the create-data-binding --help dispatch fix generalizes to every other verb broken by the same CommandLineSDK prefix-collision shape (ENG-93886 discovery: create-app/create-app-section).")]
	public void ExecuteCommands_WithCreateAppHelpSwitches_ShouldRenderCommandHelpForBothPrefixCollisionVerbs() {
		ThreadSafeStringWriter createAppOutput = new();
		Console.SetOut(createAppOutput);
		Console.SetError(createAppOutput);
		int createAppExitCode = Program.ExecuteCommands(["create-app", "--help"]);

		ThreadSafeStringWriter createAppSectionOutput = new();
		Console.SetOut(createAppSectionOutput);
		Console.SetError(createAppSectionOutput);
		int createAppSectionExitCode = Program.ExecuteCommands(["create-app-section", "--help"]);

		createAppExitCode.Should().Be(0, because: "create-app is a strict name-prefix of create-app-section and hit the same CommandLineSDK dispatch bug as create-data-binding");
		createAppOutput.ToString().Should().NotBeEmpty(because: "create-app --help previously returned zero bytes for the same reason as create-data-binding");
		createAppSectionExitCode.Should().Be(0, because: "create-app-section --help must keep succeeding after the dispatch fix");
		createAppSectionOutput.ToString().Should().NotBeEmpty(because: "create-app-section --help previously returned zero bytes for the same reason as create-data-binding");
	}

	[Test]
	[Description("Keeps a disabled experimental command's --help output empty so the shared dispatch fix cannot leak feature-toggled-off command help (ENG-93886 risk: shared chokepoint blast radius).")]
	public void ExecuteCommands_WithDisabledExperimentalCommandHelpSwitch_ShouldNotLeakCommandHelp() {
		ThreadSafeStringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		int exitCode = Program.ExecuteCommands(["ring", "--help"]);
		string output = consoleOutput.ToString();

		exitCode.Should().Be(1, because: "a feature-toggled-off command must remain indistinguishable from an unknown verb even through the new --help dispatch short-circuit");
		output.Should().NotContain("Install, update, launch",
			because: "the disabled ring command's help text must not leak through the new --help dispatch short-circuit");
	}

	[Test]
	[Description("Treats -h as a real argument, not a help request, when the target verb has already claimed -h for its own option (publish-app's --app-hub short name; ENG-93886 regression).")]
	public void IsUnclaimedHelpFlagToken_WithVerbOwnedShortH_ShouldReturnFalse() {
		bool result = Program.IsUnclaimedHelpFlagToken("-h", typeof(PublishWorkspaceCommandOptions));

		result.Should().BeFalse(because: "publish-app already binds -h to its own --app-hub option, so -h must not be misread as a help request by the new dispatch short-circuit");
	}

	[Test]
	[Description("Treats -h as a genuine help request for a verb that has not claimed -h for any of its own options (ENG-93886 core fix).")]
	public void IsUnclaimedHelpFlagToken_WithNoVerbOwnedShortH_ShouldReturnTrue() {
		bool result = Program.IsUnclaimedHelpFlagToken("-h", typeof(CreateDataBindingOptions));

		result.Should().BeTrue(because: "create-data-binding does not define its own -h option, so -h should be treated as a genuine help request");
	}

	[Test]
	[Description("Treats the long-form --help as a genuine help request even for a verb that claims the short -h for its own option, because that verb does not claim the long name 'help' (ENG-93886 regression symmetry).")]
	public void IsUnclaimedHelpFlagToken_WithLongHelpOnVerbOwningShortH_ShouldReturnTrue() {
		bool result = Program.IsUnclaimedHelpFlagToken("--help", typeof(HealthCheckOptions));

		result.Should().BeTrue(because: "healthcheck claims only the short -h (--web-host), not the long --help, so --help must still trigger real help");
	}

	[Test]
	[Description("Excludes hidden commands from the unknown-command suggestions.")]
	public void ExecuteCommands_WithHiddenCommandAlias_ShouldNotSuggestHiddenCommand() {
		ThreadSafeStringWriter consoleOutput = new();
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
	[Description("Keeps disabled experimental commands out of unknown-command suggestions.")]
	public void ExecuteCommands_WithDisabledExperimentalCommand_ShouldNotSuggestExperimentalCommand() {
		// Arrange
		ThreadSafeStringWriter consoleOutput = new();
		Console.SetOut(consoleOutput);
		Console.SetError(consoleOutput);

		// Act
		Program.ExecuteCommands(["ring"]);
		string output = consoleOutput.ToString();

		// Assert
		output.Should().NotContain("clio ring",
			because: "a disabled experimental command must remain undiscoverable on recovery surfaces");
	}

	[Test]
	[Description("Falls back to generic help when the input is too dissimilar to any visible command.")]
	public void ExecuteCommands_WithLowConfidenceUnknownVerb_ShouldShowOnlyHelpHints() {
		ThreadSafeStringWriter consoleOutput = new();
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
		// Resolve against the mock file system's own current directory (where MockExamplesFolder
		// stored the file) rather than the real process drive root. The latter is C:\ on dev
		// machines but F:\ on the CI runner, which made this test drive-dependent.
		string manifestFilePath = FileSystem.Path.Combine(FileSystem.Directory.GetCurrentDirectory(), manifestFileName);
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
		// Resolve against the mock file system's own current directory (where MockExamplesFolder
		// stored the file) rather than the real process drive root, which is C:\ on dev machines
		// but F:\ on the CI runner.
		string manifestFilePath = FileSystem.Path.Combine(FileSystem.Directory.GetCurrentDirectory(), manifestFileName);
		EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
		FileSystem commonFileSystem = new(FileSystem);
		EnvironmentOptions environmentOptionsFromManifestFile
			= Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
		environmentOptionsFromManifestFile.Should().BeNull();
	}

	private sealed class ThreadSafeStringWriter : StringWriter {
		private readonly object _sync = new();

		public override void Write(char value) {
			lock (_sync) {
				base.Write(value);
			}
		}

		public override void Write(char[] buffer, int index, int count) {
			lock (_sync) {
				base.Write(buffer, index, count);
			}
		}

		public override void Write(string value) {
			lock (_sync) {
				base.Write(value);
			}
		}

		public override void WriteLine(string value) {
			lock (_sync) {
				base.WriteLine(value);
			}
		}

		public override string ToString() {
			lock (_sync) {
				return base.ToString();
			}
		}
	}

	#endregion
}
