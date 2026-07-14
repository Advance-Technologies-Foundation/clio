using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class ExplorerContextMenuRegistrationTests {
	private static readonly string RepositoryRoot = Path.GetFullPath(
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	[Test]
	[Description("Explorer deploy registry verbs pause only after deploy-creatio returns a non-zero exit code.")]
	public void DeployRegistryVerbs_ShouldPauseOnFailure() {
		// Arrange
		string registrationPath = Path.Combine(RepositoryRoot, "clio", "reg", "clio_context_menu_win.reg");

		// Act
		string[] deployCommands = File.ReadAllLines(registrationPath)
			.Where(line => line.StartsWith("@=", StringComparison.Ordinal)
				&& line.Contains(" deploy-creatio ", StringComparison.Ordinal))
			.ToArray();

		// Assert
		deployCommands.Should().HaveCount(2,
			because: "both ZIP association locations must expose the same failure-safe deploy launcher");
		deployCommands.Should().OnlyContain(command => command.Contains("|| (echo. & pause)", StringComparison.Ordinal),
			because: "the terminal should pause only after a non-zero deploy-creatio exit code");
	}
}
