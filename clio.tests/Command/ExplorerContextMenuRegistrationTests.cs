using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ExplorerContextMenuRegistrationTests {
	private static readonly string RepositoryRoot = Path.GetFullPath(
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	[Test]
	[Description("Explorer deploy registry verbs invoke clio directly so filenames are never interpreted by a command shell.")]
	public void DeployRegistryVerbs_ShouldInvokeClioDirectly() {
		// Arrange
		string registrationPath = Path.Combine(RepositoryRoot, "clio", "reg", "clio_context_menu_win.reg");

		// Act
		string[] deployCommands = File.ReadAllLines(registrationPath)
			.Where(line => line.StartsWith("@=", StringComparison.Ordinal)
				&& line.Contains(" deploy-creatio ", StringComparison.Ordinal))
			.ToArray();

		// Assert
		deployCommands.Should().HaveCount(2,
			because: "both ZIP association locations must expose the same safe deploy launcher");
		deployCommands.Should().OnlyContain(command => command.StartsWith("@=\"clio deploy-creatio ",
			StringComparison.Ordinal), because: "the registry must launch clio without cmd.exe or another command shell");
		deployCommands.Should().OnlyContain(command => command.Contains("--zip-file \\\"%1%\\\"",
			StringComparison.Ordinal), because: "the ZIP path must remain one quoted process argument");
		deployCommands.Should().OnlyContain(command => command.Contains("--explorer-launch", StringComparison.Ordinal),
			because: "sole-local inference and failure acknowledgement must be scoped to Explorer launches");
	}

	[Test]
	[Description("Explorer deploy launcher does not introduce a shell when a ZIP filename contains percent expansion and metacharacters.")]
	public void DeployRegistryVerb_ShouldKeepPercentExpansionFilenameAsArgumentText() {
		// Arrange
		string registrationPath = Path.Combine(RepositoryRoot, "clio", "reg", "clio_context_menu_win.reg");
		string deployCommand = File.ReadAllLines(registrationPath)
			.First(line => line.StartsWith("@=", StringComparison.Ordinal)
				&& line.Contains(" deploy-creatio ", StringComparison.Ordinal));
		const string hostileFilename = "%CMDCMDLINE% & echo INJECTION-MARKER & rem .zip";

		// Act
		string expandedCommand = deployCommand.Replace("%1%", hostileFilename, StringComparison.Ordinal);

		// Assert
		expandedCommand.Should().StartWith("@=\"clio ",
			because: "a direct executable invocation gives the filename to clio without shell expansion");
		expandedCommand.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase).Should().BeFalse(
			because: "cmd.exe expands percent variables inside quotes and can turn the filename into executable syntax");
		expandedCommand.Should().Contain($"\\\"{hostileFilename}\\\"",
			because: "the complete hostile filename must remain within the quoted ZIP argument");
	}
}
