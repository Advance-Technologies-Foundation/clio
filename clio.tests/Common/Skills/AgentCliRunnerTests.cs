using System;
using System.Linq;
using Clio.Common.Skills;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class AgentCliRunnerTests {
	[Test]
	[Description("A .ps1 launcher is invoked through PowerShell with an execution-policy bypass.")]
	public void BuildCommand_ShouldUsePowerShell_ForPs1() {
		// Act
		(string program, string arguments) = AgentCliRunner.BuildCommand(@"C:\tools\codex.ps1", ["plugin", "update"]);

		// Assert
		program.Should().Be("powershell", because: "a .ps1 launcher must run through PowerShell");
		arguments.Should().Contain("-ExecutionPolicy Bypass -File", because: "the script runs via -File with a bypass");
		arguments.Should().Contain(@"C:\tools\codex.ps1", because: "the resolved script path is passed to -File");
		arguments.Should().EndWith("plugin update", because: "the caller's arguments follow the script path");
	}

	[Test]
	[Description("A .cmd launcher (npm shim) runs through cmd.exe /s /c with force-quoted args so cmd metacharacters in a URL are not interpreted.")]
	public void BuildCommand_ShouldUseCmdWithForceQuoting_ForCmdShim() {
		// Act
		(string program, string arguments) = AgentCliRunner.BuildCommand(
			@"C:\Users\me\AppData\Roaming\npm\codex.cmd",
			["plugin", "marketplace", "add", "https://example.com/x&calc.exe"]);

		// Assert
		program.Should().Be("cmd.exe", because: "a .cmd shim must be launched through cmd.exe");
		arguments.Should().StartWith("/s /c \"", because: "/s /c with an outer quote pair gives deterministic cmd quote stripping");
		arguments.Should().Contain("\"C:\\Users\\me\\AppData\\Roaming\\npm\\codex.cmd\"",
			because: "the shim path is force-quoted");
		arguments.Should().Contain("\"https://example.com/x&calc.exe\"",
			because: "an argument containing a cmd metacharacter must be quoted so '&' is not interpreted");
	}

	[Test]
	[Description("A native executable launches directly with its arguments and no launcher prefix.")]
	public void BuildCommand_ShouldLaunchDirectly_ForExecutable() {
		// Act
		(string program, string arguments) = AgentCliRunner.BuildCommand("/usr/local/bin/codex", ["plugin", "add"]);

		// Assert
		program.Should().Be("/usr/local/bin/codex", because: "a native executable is launched directly");
		arguments.Should().Be("plugin add", because: "arguments are passed through without a launcher prefix");
	}

	[Test]
	[Description("On Windows the bare extensionless name (an npm shell shim) is never a candidate; the .cmd variant is.")]
	public void CandidateFileNames_ShouldPreferExecutableExtensions_OnWindows() {
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Windows-specific resolution behavior.");
		}

		// Act
		string[] candidates = AgentCliRunner.CandidateFileNames("codex").ToArray();

		// Assert
		candidates.Should().NotContain("codex",
			because: "the bare extensionless npm shim cannot be launched by CreateProcess on Windows");
		candidates.Should().Contain("codex.cmd",
			because: "the .cmd shim is the launchable Windows variant");
	}

	[Test]
	[Description("On Unix the bare name is the executable candidate.")]
	public void CandidateFileNames_ShouldUseBareName_OnUnix() {
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore("Unix-specific resolution behavior.");
		}

		// Act
		string[] candidates = AgentCliRunner.CandidateFileNames("codex").ToArray();

		// Assert
		candidates.Should().ContainSingle().Which.Should().Be("codex",
			because: "on Unix the bare name is the executable");
	}
}
