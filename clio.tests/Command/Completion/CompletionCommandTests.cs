using System;
using System.IO;
using System.Linq;
using Clio.Command.Completion;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.Completion;

[TestFixture]
[Property("Module", "Command")]
[NonParallelizable] // mutates Console.Out and Console.Error per test via SetUp/TearDown
public class CompletionCommandTests : BaseCommandTests<CompletionOptions> {

	private CompletionCommand _command;
	private TextWriter _originalConsoleOut;
	private TextWriter _originalConsoleError;
	private StringWriter _consoleOutput;
	private StringWriter _consoleError;

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CompletionCommand>();
		_originalConsoleOut = Console.Out;
		_originalConsoleError = Console.Error;
		_consoleOutput = new StringWriter();
		_consoleError = new StringWriter();
		Console.SetOut(_consoleOutput);
		Console.SetError(_consoleError);
	}

	[TearDown]
	public override void TearDown() {
		Console.SetOut(_originalConsoleOut);
		Console.SetError(_originalConsoleError);
		_consoleOutput?.Dispose();
		_consoleError?.Dispose();
		base.TearDown();
	}

	[TestCase("bash", "complete -o default -F _clio_completion clio")]
	[TestCase("zsh", "compdef _clio_completion clio")]
	[TestCase("powershell", "Register-ArgumentCompleter -Native -CommandName clio")]
	[TestCase("fish", "complete -c clio -f")]
	[Description("Each supported shell receives the script that registers a completion function for clio, and that script calls back into the completion engine of that same shell.")]
	public void Execute_ShouldPrintRegistrationScript_ForSupportedShell(string shell, string registrationLine) {
		// Arrange
		CompletionOptions options = new() { Shell = shell };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: $"'{shell}' is a supported shell");
		string script = _consoleOutput.ToString();
		script.Should().Contain(registrationLine, because: "the script must register the completion function with the target shell");
		script.Should().Contain($"clio __complete {shell}", because: "the script must ask the engine for completions and name its own shell");
		_consoleError.ToString().Should().BeEmpty(because: "a supported shell produces no error output");
	}

	[TestCase("pwsh")]
	[TestCase("PS")]
	[Description("The PowerShell aliases resolve to the same script, so a user who types the executable name they know still gets a working registration.")]
	public void Execute_ShouldPrintPowerShellScript_ForPowerShellAlias(string shell) {
		// Arrange
		CompletionOptions options = new() { Shell = shell };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: $"'{shell}' aliases powershell");
		string script = _consoleOutput.ToString();
		script.Should().Contain("Register-ArgumentCompleter -Native", because: "the alias must select the PowerShell registration");
		script.Should().Contain("clio __complete powershell", because: "the script must call the engine with the canonical shell name");
	}

	[Test]
	[Description("An unsupported shell fails with a message naming the supported set instead of printing a script, so a hand-edited profile can be corrected.")]
	public void Execute_ShouldFail_ForUnsupportedShell() {
		// Arrange
		CompletionOptions options = new() { Shell = "tcsh" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an unsupported shell is a usage error");
		_consoleOutput.ToString().Should().BeEmpty(because: "no script can be printed for an unsupported shell");
		_consoleError.ToString().Should()
			.Contain("'tcsh' is not a supported shell", because: "the message must name the rejected value")
			.And.Contain("bash, powershell, zsh, fish", because: "the message must list the supported shells to make the fix obvious");
	}

	[Test]
	[Description("The bash script documents the eval install line, because source <(...) is silently inert on bash 3.2, still /bin/bash on macOS.")]
	public void BashScript_ShouldDocumentEvalInstall() {
		// Act
		string script = ShellCompletionScripts.GetScript("bash");

		// Assert
		script.Should().Contain("eval \"$(clio completion bash)\"",
			because: "that is the line a bash 3.2 user can paste and have actually take effect");
		script.Should().NotContain("source <(",
			because: "sourcing a non-seekable process substitution defines nothing on bash 3.2 and fails without an error");
	}

	[Test]
	[Description("Every script the verb can print is registered for exactly the shells the catalog lists, so the completion domain and the command cannot drift apart.")]
	public void ShellCompletionScripts_ShouldSerializeEveryDeclaredShell() {
		// Act
		string[] scripts = [.. ShellCompletionScripts.Names.Select(ShellCompletionScripts.GetScript)];

		// Assert
		ShellCompletionScripts.Names.Should().Equal(["bash", "powershell", "zsh", "fish"],
			because: "the supported shell set is the contract the completion verb and the engine both read");
		scripts.Should().OnlyContain(script => !string.IsNullOrWhiteSpace(script),
			because: "every declared shell must have a printable script");
	}

	[Test]
	[Description("A name that is not canonical is rejected rather than guessed at, so only a value produced by Normalize can select a script.")]
	public void GetScript_ShouldRejectUnnormalizedShellName() {
		// Act
		Action act = () => ShellCompletionScripts.GetScript("pwsh");

		// Assert
		act.Should().Throw<ArgumentOutOfRangeException>(because: "callers must normalize first; GetScript owns canonical names only");
	}

}
