using System;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The fail-on flags are unsupported on the MCP verbs: still parsed so an existing client configuration keeps
/// starting, but never written to the process-global <see cref="GlobalContext.FailOnError"/>, so an install made
/// through MCP behaves the same in-process and in a worker.
/// </summary>
/// <remarks>
/// [NonParallelizable] because the fixture reads the process-global <see cref="GlobalContext.FailOnError"/>,
/// which other fixtures set.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpHostCommandOptionsTests {

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

	private static T ParseVerb<T>(params string[] args) where T : McpHostCommandOptions {
		using Parser parser = new(with => with.HelpWriter = null);
		T parsed = null;
		ParserResult<object> result = parser.ParseArguments(args, typeof(McpServerCommandOptions),
			typeof(McpHttpServerCommandOptions));
		result.WithParsed(o => parsed = o as T);
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "an existing MCP client configuration that passes a fail-on flag must keep starting");
		return parsed;
	}

	[TestCase("mcp-server", "--fail-on-error", "--fail-on-warning")]
	[TestCase("mcp", "--fail-on-error", "--fail-on-warning")]
	[TestCase("mcp-server", "----fail-on-error", "----fail-on-warning")]
	[TestCase("mcp-server", "--fail-on-error", "----fail-on-warning")]
	[Description("mcp-server parses both fail-on spellings but does not switch on the process-global strict install check.")]
	public void McpServer_ShouldParseFailOnFlagsWithoutSettingGlobalContext(string verb, string failOnError,
		string failOnWarning) {
		// Arrange & Act
		McpServerCommandOptions options = ParseVerb<McpServerCommandOptions>(verb, failOnError, failOnWarning);

		// Assert
		options.Should().NotBeNull(because: "the verb must resolve to the stdio MCP options type");
		options!.FailOnError.Should().BeTrue(because: "the flag is still recorded so the host can warn about it");
		options.FailOnWarning.Should().BeTrue(because: "the flag is still recorded so the host can warn about it");
		GlobalContext.FailOnError.Should().BeFalse(
			because: "only the host would see a process-global value; a worker never does, so strictness would depend on where the call ran");
	}

	[TestCase("--fail-on-error")]
	[TestCase("----fail-on-error")]
	[Description("mcp-http parses both fail-on-error spellings but does not switch on the process-global strict install check.")]
	public void McpHttp_ShouldParseFailOnFlagsWithoutSettingGlobalContext(string failOnError) {
		// Arrange & Act
		McpHttpServerCommandOptions options = ParseVerb<McpHttpServerCommandOptions>("mcp-http", failOnError);

		// Assert
		options.Should().NotBeNull(because: "the verb must resolve to the HTTP MCP options type");
		options!.FailOnError.Should().BeTrue(because: "the flag is still recorded so the host can warn about it");
		GlobalContext.FailOnError.Should().BeFalse(
			because: "the long-lived HTTP host serves many callers and must not carry a process-wide strict install mode");
	}

	[TestCase(nameof(McpHostCommandOptions.FailOnError))]
	[TestCase(nameof(McpHostCommandOptions.FailOnErrorAlias))]
	[TestCase(nameof(McpHostCommandOptions.FailOnWarning))]
	[TestCase(nameof(McpHostCommandOptions.FailOnWarningAlias))]
	[Description("The fail-on flags are hidden from MCP verb help because they are not supported there.")]
	public void FailOnOptions_ShouldBeHidden(string propertyName) {
		// Arrange
		PropertyInfo property = typeof(McpHostCommandOptions).GetProperty(propertyName);

		// Act
		OptionAttribute option = property!.GetCustomAttribute<OptionAttribute>();

		// Assert
		option.Should().NotBeNull(because: "the flag must still be declared so existing configurations parse");
		option!.Hidden.Should().BeTrue(because: "an unsupported flag must not be advertised in the verb's help");
	}

	[TestCase(typeof(McpServerCommandOptions))]
	[TestCase(typeof(McpHttpServerCommandOptions))]
	[Description("Neither MCP options type inherits BaseCommandOptions, whose setter writes the process-global strict flag.")]
	public void McpOptionTypes_ShouldNotInheritBaseCommandOptions(Type optionsType) {
		// Arrange
		Type baseCommandOptionsType = typeof(BaseCommandOptions);

		// Act
		bool inheritsBaseCommandOptions = baseCommandOptionsType.IsAssignableFrom(optionsType);

		// Assert
		inheritsBaseCommandOptions.Should().BeFalse(
			because: "BaseCommandOptions.FailOnError writes GlobalContext, which a worker process never receives");
		optionsType.Should().NotBeAssignableTo<BaseCommandOptions>(
			because: "BaseCommandOptions.FailOnError writes GlobalContext, which a worker process never receives");
	}

	[Test]
	[Description("No warning is produced when neither fail-on flag was passed.")]
	public void DescribeIgnoredFailOnOptions_ShouldReturnNull_WhenNoFlagPassed() {
		// Act
		string warning = McpHostCommandOptions.DescribeIgnoredFailOnOptions(new McpServerCommandOptions());

		// Assert
		warning.Should().BeNull(because: "a host started without the flags has nothing to warn about");
	}

	[Test]
	[Description("The warning names every ignored fail-on flag so the operator knows what to remove.")]
	public void DescribeIgnoredFailOnOptions_ShouldNameEveryIgnoredFlag() {
		// Arrange
		McpHttpServerCommandOptions options = new() { FailOnError = true, FailOnWarning = true };

		// Act
		string warning = McpHostCommandOptions.DescribeIgnoredFailOnOptions(options);

		// Assert
		warning.Should().Contain("--fail-on-error", because: "the warning names every ignored flag");
		warning.Should().Contain("--fail-on-warning", because: "the warning names every ignored flag");
		warning.Should().Contain("IGNORED",
			because: "the operator must learn that the configured flag has no effect on an MCP server");
		warning.Should().NotContain("worker",
			because: "the same text is logged by mcp-http, which never relays a call to a worker process");
	}

	[Test]
	[Description("The stdio host logs a startup warning when a fail-on flag was passed.")]
	public void WarnIgnoredFailOnOptions_ShouldLogWarning_WhenFailOnErrorPassed() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		McpServerCommandOptions options = new() { FailOnError = true };

		// Act
		string warning = McpServerCommand.WarnIgnoredFailOnOptions(options, logger);

		// Assert
		warning.Should().Contain("--fail-on-error", because: "the warning names the ignored flag");
		logger.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("--fail-on-error")));
	}

	[Test]
	[Description("The stdio host stays silent when no fail-on flag was passed.")]
	public void WarnIgnoredFailOnOptions_ShouldNotLog_WhenNoFlagPassed() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();

		// Act
		string warning = McpServerCommand.WarnIgnoredFailOnOptions(new McpServerCommandOptions(), logger);

		// Assert
		warning.Should().BeNull(because: "nothing was ignored");
		logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("The HTTP host logs a startup warning when a fail-on flag was passed.")]
	public void HttpWarnIgnoredFailOnOptions_ShouldLogWarning_WhenFailOnWarningPassed() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		McpHttpServerCommandOptions options = new() { FailOnWarning = true };

		// Act
		string warning = McpHttpServerCommand.WarnIgnoredFailOnOptions(options, logger);

		// Assert
		warning.Should().Contain("--fail-on-warning", because: "the warning names the ignored flag");
		logger.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("--fail-on-warning")));
	}

	[Test]
	[Description("The HTTP host stays silent when no fail-on flag was passed.")]
	public void HttpWarnIgnoredFailOnOptions_ShouldNotLog_WhenNoFlagPassed() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();

		// Act
		string warning = McpHttpServerCommand.WarnIgnoredFailOnOptions(new McpHttpServerCommandOptions(), logger);

		// Assert
		warning.Should().BeNull(because: "nothing was ignored");
		logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}
}
