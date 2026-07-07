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
public sealed class GetCreatioInfoToolTests {
	private const string ApplicationInfoMarker = "ApplicationInfoService.svc/GetApplicationInfo";

	private const string ApplicationInfoResponse =
		"""{ "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.3292" } } }""";

	// Builds the REAL (sealed) GetCreatioInfoCommand backed by a substituted client so the tool's
	// resolve -> execute path runs end to end without I/O. The cliogate path is disabled (incompatible)
	// so the command reports the ApplicationInfoService base and returns success.
	private static GetCreatioInfoCommand CreateRealCommand(out IApplicationClient client) {
		client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ApplicationInfoResponse);
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(false);
		EnvironmentSettings env = new() { Uri = "https://creatio.test", IsNetCore = true };
		return new GetCreatioInfoCommand(client, env, gateway) { Logger = ConsoleLogger.Instance };
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable describe-environment MCP tool name so clients and tests share one contract identifier.")]
	public void GetInfo_Should_Advertise_Stable_Tool_Name() {
		// Act
		string toolName = GetCreatioInfoTool.ToolName;

		// Assert
		toolName.Should().Be("describe-environment",
			because: "the MCP contract should keep a stable describe-environment tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves GetCreatioInfoCommand for the requested environment, maps the environment-name argument, and returns the real command exit code.")]
	public void GetInfo_Should_Resolve_Command_For_Environment_And_Return_Exit_Code() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		GetCreatioInfoCommand resolvedCommand = CreateRealCommand(out _);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetCreatioInfoCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		GetCreatioInfoTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result = tool.GetInfo(new GetCreatioInfoArgs(EnvironmentName: "sandbox"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should return the real describe-environment command exit code");
			commandResolver.Received(1).Resolve<GetCreatioInfoCommand>(Arg.Is<EnvironmentOptions>(options =>
				options.Environment == "sandbox"));
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the optional timeout argument into the request issued by the resolved command.")]
	public void GetInfo_Should_Map_Timeout_When_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		GetCreatioInfoCommand resolvedCommand = CreateRealCommand(out IApplicationClient client);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetCreatioInfoCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		GetCreatioInfoTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			tool.GetInfo(new GetCreatioInfoArgs(EnvironmentName: "sandbox", Timeout: 45_000));

			// Assert
			client.Received(1).ExecutePostRequest(
				Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
				Arg.Any<string>(), 45_000, Arg.Any<int>(), Arg.Any<int>());
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a mis-spelled camelCase environmentName argument with an actionable rename hint instead of silently describing the default environment.")]
	public void GetInfo_Should_Reject_Legacy_CamelCase_Argument() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetCreatioInfoTool tool = new(ConsoleLogger.Instance, commandResolver);
		GetCreatioInfoArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("sandbox")
			}
		};

		try {
			// Act
			CommandExecutionResult result = tool.GetInfo(args);

			// Assert
			result.ExitCode.Should().Be(1,
				because: "an unbindable camelCase argument is a caller-actionable validation failure, not a silent default");
			result.Output.Select(message => message.Value?.ToString() ?? string.Empty)
				.Should().Contain(value => value.Contains("environment-name"),
					because: "the failure must tell the agent the canonical kebab-case name to use");
			commandResolver.DidNotReceive().Resolve<GetCreatioInfoCommand>(Arg.Any<EnvironmentOptions>());
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes read-only, non-destructive, idempotent MCP metadata and a source-independent description for describe-environment.")]
	public void GetInfo_Should_Expose_Expected_Mcp_Metadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetCreatioInfoTool)
			.GetMethod(nameof(GetCreatioInfoTool.GetInfo))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		System.ComponentModel.DescriptionAttribute description =
			(System.ComponentModel.DescriptionAttribute)typeof(GetCreatioInfoTool)
				.GetMethod(nameof(GetCreatioInfoTool.GetInfo))!
				.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
				.Single();

		// Assert
		attribute.Name.Should().Be(GetCreatioInfoTool.ToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeTrue(
			because: "describe-environment only reads instance metadata and must be advertised as read-only");
		attribute.Destructive.Should().BeFalse(
			because: "describe-environment must not mutate Creatio state");
		attribute.Idempotent.Should().BeTrue(
			because: "re-describing an environment yields the same report and is safe to repeat");
		description.Description.Should().Contain("source-independent",
			because: "the description must state the shape is the same with or without cliogate");
		description.Description.Should().Contain("get-guidance name=describe-environment",
			because: "the description should point the agent at the field-catalogue guidance");
	}
}
