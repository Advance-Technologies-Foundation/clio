using System.Linq;
using Clio.Command;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Parses whole command lines of verbs whose options derive from <see cref="BaseCommandOptions"/> through the
/// verb set clio registers, so --fail-on-error and --fail-on-warning are checked the way a user types them.
/// </summary>
// [NonParallelizable] because the options write the process-global GlobalContext.FailOnError / FailOnWarning.
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class BaseCommandOptionsParsingTests {
	private bool _originalFailOnError;
	private bool _originalFailOnWarning;

	[SetUp]
	public void SetUp() {
		_originalFailOnError = GlobalContext.FailOnError;
		_originalFailOnWarning = GlobalContext.FailOnWarning;
		GlobalContext.FailOnError = false;
		GlobalContext.FailOnWarning = false;
	}

	[TearDown]
	public void TearDown() {
		GlobalContext.FailOnError = _originalFailOnError;
		GlobalContext.FailOnWarning = _originalFailOnWarning;
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
	[Description("mcp-server and mcp-http also derive their options from BaseCommandOptions, so --fail-on-error parses on both MCP transports.")]
	public void Parse_ShouldSetFailOnError_WhenMcpServerVerbIsGivenFailOnError(
		[Values("mcp-server", "mcp-http")] string verb) {
		// Arrange
		string[] args = [verb, "--fail-on-error"];

		// Act
		object options = Parse(args);

		// Assert
		BaseCommandOptions baseOptions = options.Should().BeAssignableTo<BaseCommandOptions>(
			because: $"{verb} options inherit --fail-on-error from BaseCommandOptions").Subject;
		baseOptions.FailOnError.Should().BeTrue(because: "--fail-on-error must set FailOnError");
		baseOptions.FailOnWarning.Should().BeFalse(because: "--fail-on-warning was not given");
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
