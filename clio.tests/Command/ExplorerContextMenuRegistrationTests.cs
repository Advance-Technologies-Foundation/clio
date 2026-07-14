using System;
using System.Diagnostics;
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
		deployCommands.Should().OnlyContain(command => command.Contains("/c clio ", StringComparison.Ordinal),
			because: "the whole command payload must not be wrapped in quotes that expose filename metacharacters");
		deployCommands.Should().OnlyContain(command => command.Contains("--explorer-launch", StringComparison.Ordinal),
			because: "sole-local inference and prompt suppression must be scoped to Explorer launches");
	}

	[Test]
	[Description("Explorer deploy launcher keeps command metacharacters in the ZIP filename argument instead of executing them.")]
	public void DeployRegistryVerb_ShouldNotExecuteMetacharactersFromZipPath() {
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Windows cmd parsing is only available on Windows.");
		}

		// Arrange
		string registrationPath = Path.Combine(RepositoryRoot, "clio", "reg", "clio_context_menu_win.reg");
		string deployCommand = File.ReadAllLines(registrationPath)
			.First(line => line.StartsWith("@=", StringComparison.Ordinal)
				&& line.Contains(" deploy-creatio ", StringComparison.Ordinal));
		string decodedCommand = deployCommand[3..^1].Replace("\\\"", "\"");
		const string commandPrefix = "cmd.exe /d /c ";
		string payload = decodedCommand[commandPrefix.Length..]
			.Replace("%1%", "safe & echo INJECTION-MARKER & rem .zip", StringComparison.Ordinal)
			.Replace("pause", "echo PAUSE-MARKER", StringComparison.Ordinal);
		string stubDirectory = Path.Combine(Path.GetTempPath(), $"clio-explorer-launch-{Guid.NewGuid():N}");
		Directory.CreateDirectory(stubDirectory);
		File.WriteAllText(Path.Combine(stubDirectory, "clio.cmd"), "@exit /b 1\r\n");
		ProcessStartInfo startInfo = new("cmd.exe") {
			Arguments = $"/d /c {payload}",
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		startInfo.Environment["PATH"] = $"{stubDirectory}{Path.PathSeparator}{startInfo.Environment["PATH"]}";

		try {
			// Act
			using Process process = Process.Start(startInfo)!;
			string standardOutput = process.StandardOutput.ReadToEnd();
			process.WaitForExit(5000).Should().BeTrue(
				because: "the failure-aware launcher should return after the test replaces pause with an echo marker");

			// Assert
			standardOutput.Should().Contain("PAUSE-MARKER",
				because: "the stub clio failure should enter the conditional failure branch");
			standardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
				.Should().NotContain("INJECTION-MARKER",
					because: "metacharacters inside the quoted ZIP filename must remain argument text");
		}
		finally {
			Directory.Delete(stubDirectory, true);
		}
	}
}
