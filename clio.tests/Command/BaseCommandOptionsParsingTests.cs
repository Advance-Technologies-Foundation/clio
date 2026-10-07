using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.YAML;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;
using OneOf;
using OneOf.Types;

namespace Clio.Tests.Command;

/// <summary>
/// Parses whole command lines of verbs whose options derive from <see cref="BaseCommandOptions"/> through the
/// verb set clio registers, so --fail-on-error and --fail-on-warning are checked the way a user types them.
/// </summary>
// [NonParallelizable] because the options write the process-global GlobalContext.FailOnError.
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class BaseCommandOptionsParsingTests {
	private bool _originalFailOnError;

	[SetUp]
	public void SetUp() {
		_originalFailOnError = GlobalContext.FailOnError;
		GlobalContext.FailOnError = false;
	}

	[TearDown]
	public void TearDown() {
		GlobalContext.FailOnError = _originalFailOnError;
	}

	[Test]
	[Description("clio assert fs --fail-on-error parses and turns FailOnError on; the long name used to be declared with its dashes, so the parser rejected it as unknown.")]
	public void Parse_ShouldSetFailOnError_WhenAssertIsGivenFailOnError() {
		// Arrange
		string[] args = ["assert", "fs", "--fail-on-error"];

		// Act
		object options = Parse(args);

		// Assert
		AssertOptions assertOptions = options.Should().BeOfType<AssertOptions>(
			because: "--fail-on-error is an option of assert, not an unknown option").Subject;
		assertOptions.Scope.Should().Be("fs", because: "the scope value must still bind next to the flag");
		assertOptions.FailOnError.Should().BeTrue(because: "--fail-on-error must set FailOnError");
		GlobalContext.FailOnError.Should().BeTrue(because: "FailOnError is stored in GlobalContext");
		assertOptions.FailOnWarning.Should().BeFalse(because: "--fail-on-warning was not given");
	}

	[Test]
	[Description("clio hosts --fail-on-error --fail-on-warning parses and turns both flags on.")]
	public void Parse_ShouldSetBothFlags_WhenHostsIsGivenFailOnErrorAndFailOnWarning() {
		// Arrange
		string[] args = ["hosts", "--fail-on-error", "--fail-on-warning"];

		// Act
		object options = Parse(args);

		// Assert
		HostsOptions hostsOptions = options.Should().BeOfType<HostsOptions>(
			because: "both flags are options of hosts").Subject;
		hostsOptions.FailOnError.Should().BeTrue(because: "--fail-on-error must set FailOnError");
		hostsOptions.FailOnWarning.Should().BeTrue(because: "--fail-on-warning must set FailOnWarning");
	}

	[Test]
	[Description("mcp-server and mcp-http accept --fail-on-error so an existing client configuration keeps starting, but the flag is unsupported there and never reaches GlobalContext (ENG-102487).")]
	public void Parse_ShouldAcceptFailOnErrorWithoutStrictMode_WhenMcpServerVerbIsGivenFailOnError(
		[Values("mcp-server", "mcp-http")] string verb) {
		// Arrange
		string[] args = [verb, "--fail-on-error"];

		// Act
		object options = Parse(args);

		// Assert
		McpHostCommandOptions mcpOptions = options.Should().BeAssignableTo<McpHostCommandOptions>(
			because: $"{verb} options declare the unsupported fail-on flags on McpHostCommandOptions").Subject;
		mcpOptions.FailOnError.Should().BeTrue(because: "the flag is recorded so the host can warn that it is ignored");
		mcpOptions.FailOnWarning.Should().BeFalse(because: "--fail-on-warning was not given");
		GlobalContext.FailOnError.Should().BeFalse(
			because: "a worker never receives the flag, so the host must not apply it in-process either");
	}

	[Test]
	[Description("The legacy ----fail-on-error / ----fail-on-warning spellings, the only ones the parser used to accept, still parse through the hidden aliases.")]
	public void Parse_ShouldSetBothFlags_WhenLegacyFourDashSpellingsAreGiven() {
		// Arrange
		string[] args = ["assert", "fs", "----fail-on-error", "----fail-on-warning"];

		// Act
		object options = Parse(args);

		// Assert
		AssertOptions assertOptions = options.Should().BeOfType<AssertOptions>(
			because: "scripts written against the old spelling must keep working").Subject;
		assertOptions.FailOnError.Should().BeTrue(because: "----fail-on-error is the hidden alias of --fail-on-error");
		assertOptions.FailOnWarning.Should().BeTrue(because: "----fail-on-warning is the hidden alias of --fail-on-warning");
	}

	[Test]
	[Description("A new spelling of one flag and the legacy spelling of the other parse together, and neither flag's unset sibling clears it.")]
	public void Parse_ShouldSetBothFlags_WhenNewAndLegacySpellingsAreMixed() {
		// Arrange
		string[] args = ["hosts", "--fail-on-error", "----fail-on-warning"];

		// Act
		object options = Parse(args);

		// Assert
		HostsOptions hostsOptions = options.Should().BeOfType<HostsOptions>(
			because: "both spellings are options of hosts").Subject;
		hostsOptions.FailOnError.Should().BeTrue(
			because: "the unset ----fail-on-error alias must not clear the flag --fail-on-error set");
		hostsOptions.FailOnWarning.Should().BeTrue(
			because: "the unset --fail-on-warning option must not clear the flag ----fail-on-warning set");
	}

	[Test]
	[Description("Without the flags both stay off, so the hidden aliases do not switch them on by being parsed as unset.")]
	public void Parse_ShouldLeaveFlagsOff_WhenNeitherFlagIsGiven() {
		// Arrange
		string[] args = ["assert", "fs"];

		// Act
		object options = Parse(args);

		// Assert
		AssertOptions assertOptions = options.Should().BeOfType<AssertOptions>(
			because: "assert fs without the flags is a valid command line").Subject;
		assertOptions.FailOnError.Should().BeFalse(because: "--fail-on-error was not given");
		assertOptions.FailOnWarning.Should().BeFalse(because: "--fail-on-warning was not given");
	}

	[TestCase("--fail-on-error")]
	[TestCase("fail-on-error")]
	[Description("A YAML scenario step can turn the strict flag on with either key: the legacy key --fail-on-error binds the hidden alias and the key fail-on-error the main option.")]
	public void Activate_ShouldTurnFailOnErrorOn_WhenYamlStepSetsEitherKeyToTrue(string key) {
		// Arrange
		Step step = CreateAssertStep(key, "true");

		// Act
		Activate(step);

		// Assert
		GlobalContext.FailOnError.Should().BeTrue(
			because: $"the YAML key {key} set to true must turn on the process-global flag the installers read");
	}

	[Test]
	[Description("After a step turns the strict flag on, a step with the legacy YAML key --fail-on-error set to false leaves it on: the key binds the hidden alias, whose setter only turns the flag on. run-scenario activates every step before running any, so the flag then applies to all installs in the scenario (ENG-102481).")]
	public void Activate_ShouldLeaveFailOnErrorOn_WhenLegacyYamlKeyIsFalseAfterTrue() {
		// Arrange
		Step enablingStep = CreateAssertStep("--fail-on-error", "true");
		Step legacyFalseStep = CreateAssertStep("--fail-on-error", "false");
		Activate(enablingStep);
		GlobalContext.FailOnError.Should().BeTrue(because: "the legacy key set to true binds the alias and turns the flag on");

		// Act
		Activate(legacyFalseStep);

		// Assert
		GlobalContext.FailOnError.Should().BeTrue(
			because: "the alias setter ignores false so that an unset alias never clears the main option on the command line");
	}

	[Test]
	[Description("After a step turns the strict flag on, a step with the YAML key fail-on-error set to false turns it off, which is the documented way to clear it.")]
	public void Activate_ShouldTurnFailOnErrorOff_WhenYamlKeyIsFalseAfterTrue() {
		// Arrange
		Step enablingStep = CreateAssertStep("fail-on-error", "true");
		Step disablingStep = CreateAssertStep("fail-on-error", "false");
		Activate(enablingStep);
		GlobalContext.FailOnError.Should().BeTrue(because: "the key set to true turns the flag on");

		// Act
		Activate(disablingStep);

		// Assert
		GlobalContext.FailOnError.Should().BeFalse(because: "the main option assigns the value as given, so false clears the flag");
	}

	[TestCase("--fail-on-warning")]
	[TestCase("fail-on-warning")]
	[Description("A YAML scenario step accepts either fail-on-warning key and binds it, so a scenario that still carries the flag keeps activating; the value has no effect anywhere.")]
	public void Activate_ShouldBindFailOnWarning_WhenYamlStepSetsEitherKeyToTrue(string key) {
		// Arrange
		Step step = CreateAssertStep(key, "true");

		// Act
		AssertOptions options = Activate(step);

		// Assert
		options.FailOnWarning.Should().BeTrue(because: $"the YAML key {key} must bind to FailOnWarning");
		GlobalContext.FailOnError.Should().BeFalse(because: "fail-on-warning must not switch on the strict install-log check");
	}

	private static Step CreateAssertStep(string key, string value) =>
		new() {
			Action = "assert",
			Description = "assert filesystem",
			Options = new Dictionary<object, object> {
				{"scope", "fs"},
				{key, value}
			}
		};

	private static AssertOptions Activate(Step step) {
		Func<string, OneOf<object, None>> noLookup = _ => new None();
		Tuple<OneOf<None, object>, string> activated =
			step.Activate(typeof(AssertOptions).Assembly.GetTypes(), noLookup, noLookup);
		return activated.Item1.Value.Should().BeOfType<AssertOptions>(
			because: "the assert action resolves to AssertOptions").Subject;
	}

	private static object Parse(string[] args) {
		using Parser parser = new(settings => settings.HelpWriter = null);
		ParserResult<object> result = parser.ParseArguments(Program.NormalizeCommandLineArgs(args),
			Program.GetCommandOptionTypes().ToArray());
		string errors = result is NotParsed<object> notParsed
			? string.Join(", ", notParsed.Errors.Select(error => error is NamedError named ? $"{error.Tag} {named.NameInfo.NameText}" : error.Tag.ToString()))
			: string.Empty;
		result.Tag.Should().Be(ParserResultType.Parsed, because: $"'{string.Join(" ", args)}' must parse; errors: {errors}");
		return ((Parsed<object>)result).Value;
	}
}
