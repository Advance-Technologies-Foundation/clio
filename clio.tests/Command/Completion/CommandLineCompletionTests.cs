using System;
using System.IO;
using System.Linq;
using Clio.Command.Completion;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.Completion;

[TestFixture]
[Property("Module", "Command")]
[NonParallelizable] // mutates Console.Out per test via SetUp/TearDown
public class CommandLineCompletionTests {

	private TextWriter _originalConsoleOut;
	private StringWriter _consoleOutput;

	[SetUp]
	public void Setup() {
		_originalConsoleOut = Console.Out;
		_consoleOutput = new StringWriter();
		Console.SetOut(_consoleOutput);
	}

	[TearDown]
	public void TearDown() {
		Console.SetOut(_originalConsoleOut);
		_consoleOutput?.Dispose();
	}

	[Test]
	[Description("A normal command line is not a completion request, so the engine declines it and leaves the exit code untouched.")]
	public void TryHandle_ShouldDecline_WhenFirstArgumentIsNotTheCompletionVerb() {
		// Arrange
		string[] args = ["reg-web-app", "-e", "dev"];

		// Act
		bool handled = CommandLineCompletion.TryHandle(args, out int exitCode);

		// Assert
		handled.Should().BeFalse(because: "the caller must still run the ordinary verb parser for a non-completion invocation");
		exitCode.Should().Be(0, because: "the declined call does not decide the process exit code");
		_consoleOutput.ToString().Should().BeEmpty(because: "declining must not write completion candidates");
	}

	[Test]
	[Description("With no verb before the cursor the engine completes command names from the assembly, and never offers its own meta-command.")]
	public void TryHandle_ShouldCompleteCommandNames_WhenNoVerbIsPresent() {
		// Act
		string[] completions = Complete("__complete", "bash", "");

		// Assert
		completions.Should().Contain("completion", because: "every declared verb is a completion candidate");
		completions.Should().Contain("reg-web-app", because: "multi-word kebab-case verbs are completed as one token");
		completions.Should().Contain("login", because: "the candidate list is the real command set, not a curated subset");
		completions.Should().NotContain("__complete", because: "the meta-command must not list itself as an answer to its own question");
	}

	[Test]
	[Description("A partially typed command name is completed to its canonical spelling, so an alias prefix expands to the real verb.")]
	public void TryHandle_ShouldCompleteAliasPrefix_ToCanonicalName() {
		// Act
		string[] completions = Complete("__complete", "bash", "hc");

		// Assert
		completions.Should().Contain("healthcheck", because: "'hc' is an alias of healthcheck and must expand to the canonical verb");
		completions.Should().NotContain("hc", because: "completion always emits the canonical spelling the parser dispatches on");
	}

	[Test]
	[Description("The current word may be omitted entirely, which is what Windows PowerShell 5.1 sends when it drops the empty trailing argument.")]
	public void TryHandle_ShouldCompleteCommandNames_WhenCurrentWordIsOmitted() {
		// Act
		string[] completions = Complete("__complete", "powershell");

		// Assert
		completions.Should().Contain("login", because: "an invocation with only the shell name means 'complete the first word'");
	}

	[Test]
	[Description("Hidden verbs are compatibility, easter-egg and probe spellings that help and the published docs deliberately hide, so completion does not advertise them.")]
	public void TryHandle_ShouldNotCompleteHiddenVerbs() {
		// Act
		string[] completions = Complete("__complete", "bash", "");

		// Assert
		completions.Should().NotContain("quiz", because: "quiz is declared Hidden and must stay invisible on every user-facing surface");
		completions.Should().NotContain("list-printables", because: "list-printables is an MCP probe surface declared Hidden");
		completions.Should().NotContain("get-target-package", because: "get-target-package is declared Hidden");
	}

	[Test]
	[Description("The argument of the completion verb has a static domain, so the shell names are completed instead of that verb's options.")]
	public void TryHandle_ShouldCompleteShellNames_ForTheCompletionVerb() {
		// Act
		string[] completions = Complete("__complete", "bash", "po", "clio", "completion");

		// Assert
		completions.Should().Equal(["powershell"], because: "'po' is the unique shell-name prefix and the program name token is dropped");
	}

	[Test]
	[Description("The shell domain offered for the completion verb is exactly the set of shells the completion command can print a script for.")]
	public void TryHandle_ShouldOfferEverySupportedShell_ForTheCompletionVerb() {
		// Act
		string[] completions = Complete("__complete", "bash", "", "clio", "completion");

		// Assert
		completions.Should().Equal(ShellCompletionScripts.Names,
			because: "completion and the completion verb must agree on the supported shell set");
	}

	[Test]
	[Description("The program name token the shells pass is dropped, so the first remaining word is read as the invoked verb.")]
	public void TryHandle_ShouldDropProgramName_SoTheVerbStillCompletesItsOptions() {
		// Act
		string[] completions = Complete("__complete", "bash", "--act", "clio", "reg-web-app");

		// Assert
		completions.Should().Contain("--active-environment", because: "the verb after the program name is the one whose options are completed");
	}

	[Test]
	[Description("An empty current word offers both dash families, so the user can pick a short or a long option.")]
	public void TryHandle_ShouldOfferBothDashFamilies_WhenNoDashIsTyped() {
		// Act
		string[] completions = Complete("__complete", "bash", "", "clio", "reg-web-app");

		// Assert
		completions.Should().Contain("--active-environment", because: "long options are offered for an empty word");
		completions.Should().Contain("-a", because: "the short form of the same option is offered for an empty word");
	}

	[Test]
	[Description("A single typed dash narrows the candidates to short options, because no long option can be spelled from a one-dash word.")]
	public void TryHandle_ShouldOfferShortOptionsOnly_WhenOneDashIsTyped() {
		// Act
		string[] completions = Complete("__complete", "bash", "-", "clio", "reg-web-app");

		// Assert
		completions.Should().Contain("-a", because: "the short family is what a one-dash word can become");
		completions.Should().NotContain(completion => completion.StartsWith("--", StringComparison.Ordinal),
			because: "a long option can never be completed from a single typed dash");
	}

	[Test]
	[Description("Two typed dashes narrow the candidates to long options, so a two-dash word never yields a short flag.")]
	public void TryHandle_ShouldOfferLongOptionsOnly_WhenTwoDashesAreTyped() {
		// Act
		string[] completions = Complete("__complete", "bash", "--", "clio", "reg-web-app");

		// Assert
		completions.Should().Contain("--active-environment", because: "long options are the only candidates for a two-dash word");
		completions.Should().NotContain("-a", because: "the short family cannot be completed from a two-dash word");
	}

	[Test]
	[Description("Hidden options are backward-compatibility aliases that help deliberately omits, so completion offers the canonical spelling only.")]
	public void TryHandle_ShouldNotCompleteHiddenOptions() {
		// Act
		string[] completions = Complete("__complete", "bash", "--check", "clio", "reg-web-app");

		// Assert
		completions.Should().Contain("--check-login", because: "the kebab-case spelling is the advertised option");
		completions.Should().NotContain("--checkLogin", because: "the camelCase form is declared Hidden and must not be advertised");
	}

	[Test]
	[Description("An unknown verb yields no candidates rather than an error, because the shell must keep the user's prompt usable.")]
	public void TryHandle_ShouldReturnNothing_ForAnUnknownVerb() {
		// Act
		string[] completions = Complete("__complete", "bash", "-", "clio", "no-such-verb");

		// Assert
		completions.Should().BeEmpty(because: "an unknown verb has no options to offer and must not fail the completion");
	}

	private string[] Complete(params string[] args) {
		bool handled = CommandLineCompletion.TryHandle(args, out int exitCode);

		handled.Should().BeTrue(because: "every __complete invocation must be fully consumed by the engine");
		exitCode.Should().Be(0, because: "a shell reads a non-zero exit code as 'no completion available'");

		return _consoleOutput
			.ToString()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.TrimEnd('\r'))
			.ToArray();
	}

}
