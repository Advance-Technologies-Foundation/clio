using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class GenerateSourceCodeToolTests
{

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name so callers and tests share one identifier.")]
	public void GenerateSourceCodeTool_Should_Advertise_Stable_Tool_Name() {
		GenerateSourceCodeTool.GenerateSourceCodeToolName
			.Should().Be("generate-source-code",
				because: "the MCP tool name must remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool must not be marked destructive since source code generation does not delete or overwrite persistent data.")]
	public void GenerateSourceCode_Should_Not_Be_Marked_As_Destructive() {
		McpServerToolAttribute attribute = GetToolAttribute();
		attribute.Destructive.Should().BeFalse(
			because: "generate-source-code regenerates schema sources without removing existing data");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool must be marked idempotent because running generate-source-code multiple times yields the same result.")]
	public void GenerateSourceCode_Should_Be_Marked_As_Idempotent() {
		McpServerToolAttribute attribute = GetToolAttribute();
		attribute.Idempotent.Should().BeTrue(
			because: "generating source code for the same schemas produces the same outcome on repeated calls");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware command and maps all MCP arguments into GenerateSourceCodeOptions.")]
	public void GenerateSourceCode_Should_Resolve_Command_And_Map_Arguments() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.GenerateSourceCode(new GenerateSourceCodeArgs(
			"dev", Modified: false, Required: false, Background: false));

		result.ExitCode.Should().Be(0,
			because: "a valid generate-source-code request should execute successfully");
		commandResolver.Received(1).Resolve<GenerateSourceCodeCommand>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolvedCommand.CapturedOptions!.Environment.Should().Be("dev");
		resolvedCommand.CapturedOptions.Modified.Should().BeFalse();
		resolvedCommand.CapturedOptions.Required.Should().BeFalse();
		resolvedCommand.CapturedOptions.Background.Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the --modified flag to the command options when the caller sets modified=true.")]
	public void GenerateSourceCode_Should_Forward_Modified_Flag() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeArgs("dev", Modified: true, Required: false, Background: false));

		resolvedCommand.CapturedOptions!.Modified.Should().BeTrue(
			because: "the modified flag must be forwarded from MCP args to command options");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the --required flag to the command options when the caller sets required=true.")]
	public void GenerateSourceCode_Should_Forward_Required_Flag() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeArgs("dev", Modified: false, Required: true, Background: false));

		resolvedCommand.CapturedOptions!.Required.Should().BeTrue(
			because: "the required flag must be forwarded from MCP args to command options");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the --background flag to the command options when the caller sets background=true.")]
	public void GenerateSourceCode_Should_Forward_Background_Flag() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeArgs("dev", Modified: false, Required: false, Background: true));

		resolvedCommand.CapturedOptions!.Background.Should().BeTrue(
			because: "the background flag must be forwarded from MCP args to command options");
	}

	[Test]
	[Category("Unit")]
	[Description("Defaults all optional flags to false when the caller omits them.")]
	public void GenerateSourceCode_Should_Default_All_Flags_To_False_When_Omitted() {
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		tool.GenerateSourceCode(new GenerateSourceCodeArgs("dev", null, null, null));

		resolvedCommand.CapturedOptions!.Modified.Should().BeFalse(
			because: "omitting modified should default to false (generate all)");
		resolvedCommand.CapturedOptions.Required.Should().BeFalse(
			because: "omitting required should default to false");
		resolvedCommand.CapturedOptions.Background.Should().BeFalse(
			because: "omitting background should default to false (synchronous)");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error result when the requested environment cannot be resolved.")]
	public void GenerateSourceCode_Should_Report_Invalid_Environment_As_Command_Result() {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		GenerateSourceCodeTool tool = new(new FakeGenerateSourceCodeCommand(), ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.GenerateSourceCode(
			new GenerateSourceCodeArgs("missing-env", null, null, null));

		result.ExitCode.Should().Be(1,
			because: "resolver failures are expected validation errors and must surface with exit code 1, not the unexpected-exception code -1");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			message.Value != null &&
			message.Value.ToString()!.Contains("missing-env"),
			because: "the environment-resolution failure must surface in the output");
	}


	[Test]
	[Category("Unit")]
	[Description("Maps the MCP 'timeout' argument onto GenerateSourceCodeOptions.TimeOut so a long generation is not cut off at the default request timeout.")]
	public void GenerateSourceCode_ShouldMapTimeoutOntoOptions_WhenTimeoutIsSupplied() {
		// Arrange
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.GenerateSourceCode(
			new GenerateSourceCodeArgs("dev", null, null, null, Timeout: 120000));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a positive timeout is a valid request and must execute");
		resolvedCommand.CapturedOptions!.TimeOut.Should().Be(120000,
			because: "the MCP 'timeout' argument must reach the command options as the request timeout");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves the request timeout at its default when the caller omits the 'timeout' argument.")]
	public void GenerateSourceCode_ShouldLeaveDefaultTimeout_WhenTimeoutIsOmitted() {
		// Arrange
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);
		int defaultTimeOut = new GenerateSourceCodeOptions().TimeOut;

		// Act
		tool.GenerateSourceCode(new GenerateSourceCodeArgs("dev", null, null, null));

		// Assert
		resolvedCommand.CapturedOptions!.TimeOut.Should().Be(defaultTimeOut,
			because: "omitting timeout must leave the command's own default untouched rather than overwriting it with zero");
	}

	[Test]
	[Category("Unit")]
	[TestCase(0)]
	[TestCase(-1)]
	[Description("Rejects a non-positive 'timeout' as a validation error without resolving or running the command.")]
	public void GenerateSourceCode_ShouldRejectNonPositiveTimeout_WhenTimeoutIsZeroOrNegative(int timeout) {
		// Arrange
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.GenerateSourceCode(
			new GenerateSourceCodeArgs("dev", null, null, null, Timeout: timeout));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a non-positive timeout is a caller mistake and must surface as a validation error");
		result.Output.Should().Contain(message =>
			message.Value != null && message.Value.ToString()!.Contains("'timeout' must be a positive"),
			because: "the error must name the offending argument so the caller can correct it");
		commandResolver.DidNotReceive().Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a camelCase environmentName with an explicit rename hint instead of silently running against a null environment.")]
	public void GenerateSourceCode_ShouldRejectCamelCaseEnvironmentName_WithRenameHint() {
		// Arrange
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);
		GenerateSourceCodeArgs args = new(null!, null, null, null) {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = ToJsonElement("dev")
			}
		};

		// Act
		CommandExecutionResult result = tool.GenerateSourceCode(args);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "an unbound argument must fail loudly rather than be dropped by the JSON binder");
		result.Output.Should().Contain(message =>
			message.Value != null && message.Value.ToString()!.Contains("'environmentName' -> 'environment-name'"),
			because: "the caller must be told the exact rename to apply");
		commandResolver.DidNotReceive().Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a genuinely unknown argument under 'Unknown args' together with the valid-field hint.")]
	public void GenerateSourceCode_ShouldRenameTimeOutSpelling_RatherThanCallingItUnknown() {
		// Arrange
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);
		GenerateSourceCodeArgs args = new("dev", null, null, null) {
			ExtensionData = new Dictionary<string, JsonElement> {
				["timeOut"] = ToJsonElement("600000")
			}
		};

		// Act
		CommandExecutionResult result = tool.GenerateSourceCode(args);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "an unbindable argument must not be silently ignored");
		result.Output.Should().Contain(message =>
			message.Value != null && message.Value.ToString()!.Contains("Rename: 'timeOut' -> 'timeout'"),
			because: "'timeOut' matches the C# property name, so it is the likeliest mis-spelling and must earn a rename hint rather than a bare unknown-argument list");
		commandResolver.DidNotReceive().Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Reports a genuinely unrecognized argument under 'Unknown args' together with the list of accepted field names, so the caller can correct the call in one round-trip.")]
	[Category("Unit")]
	public void GenerateSourceCode_ShouldReportUnknownArgument_WithValidFieldHint() {
		// Arrange
		FakeGenerateSourceCodeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		GenerateSourceCodeTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);
		GenerateSourceCodeArgs args = new("dev", null, null, null) {
			ExtensionData = new Dictionary<string, JsonElement> {
				["bogusField"] = ToJsonElement("x")
			}
		};

		// Act
		CommandExecutionResult result = tool.GenerateSourceCode(args);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "an unbindable argument must not be silently ignored");
		result.Output.Should().Contain(message =>
			message.Value != null && message.Value.ToString()!.Contains("Unknown args: 'bogusField'"),
			because: "the unknown argument must be named back to the caller");
		result.Output.Should().Contain(message =>
			message.Value != null && message.Value.ToString()!.Contains("Valid fields: environment-name, modified, required, background, timeout."),
			because: "the rejection must list the accepted field names so the caller can correct the call in one round-trip");
		commandResolver.DidNotReceive().Resolve<GenerateSourceCodeCommand>(Arg.Any<EnvironmentOptions>());
	}

	private static JsonElement ToJsonElement(string value) =>
		JsonDocument.Parse($"\"{value}\"").RootElement.Clone();

	private static McpServerToolAttribute GetToolAttribute() =>
		typeof(GenerateSourceCodeTool)
			.GetMethod(nameof(GenerateSourceCodeTool.GenerateSourceCode))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

	private sealed class FakeGenerateSourceCodeCommand : GenerateSourceCodeCommand
	{
		public GenerateSourceCodeOptions? CapturedOptions { get; private set; }

		public FakeGenerateSourceCodeCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) { }

		public override int Execute(GenerateSourceCodeOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

}
