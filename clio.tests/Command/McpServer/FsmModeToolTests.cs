using System;
using System.Linq;
using System.IO.Abstractions;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentValidation;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class FsmModeToolTests
{
	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for querying and changing FSM mode.")]
	public void FsmModeTools_Should_Advertise_Stable_Tool_Names()
	{
		// Arrange

		// Act
		string getToolName = FsmModeTool.GetFsmModeToolName;
		string setToolName = FsmModeTool.SetFsmModeToolName;

		// Assert
		getToolName.Should().Be("get-fsm-mode",
			because: "clients and tests should share one stable tool name for FSM mode detection");
		setToolName.Should().Be("set-fsm-mode",
			because: "clients and tests should share one stable tool name for FSM mode changes");
	}

	[Test]
	[Category("Unit")]
	[Description("Detects FSM mode on when GetApplicationInfo returns useStaticFileContent=false and staticFileContent=null.")]
	public void GetFsmMode_Should_Return_On_When_ApplicationInfo_Represents_Fsm_On()
	{
		// Arrange
		FsmModeTool tool = CreateTool(
			"""
			{
			  "useStaticFileContent": false,
			  "staticFileContent": null
			}
			""");

		// Act
		FsmModeStatusResult result = tool.GetFsmMode("sandbox");

		// Assert
		result.EnvironmentName.Should().Be("sandbox",
			because: "the returned status should preserve the requested environment name");
		result.Mode.Should().Be("on",
			because: "useStaticFileContent=false with staticFileContent=null represents FSM on");
		result.UseStaticFileContent.Should().BeFalse(
			because: "the structured result should preserve the raw GetApplicationInfo flag");
		result.StaticFileContent.Should().BeNull(
			because: "the structured result should preserve the raw null staticFileContent payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the known GetApplicationInfo route and POST request when querying FSM mode.")]
	public void GetFsmMode_Should_Use_Known_Route_And_Post_Request()
	{
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new()
		{
			Uri = "http://sandbox",
			Login = "Supervisor",
			Password = "Supervisor"
		};
		settingsRepository.FindEnvironment("sandbox").Returns(environmentSettings);
		applicationClientFactory.CreateEnvironmentClient(environmentSettings).Returns(applicationClient);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(
			"""
			{
			  "useStaticFileContent": false,
			  "staticFileContent": null
			}
			""");
		FsmModeStatusService service = new(settingsRepository, applicationClientFactory);

		// Act
		_ = service.GetStatus("sandbox");

		// Assert
		string expectedUrl = new ServiceUrlBuilder(environmentSettings).Build(ServiceUrlBuilder.KnownRoute.GetApplicationInfo);
		applicationClient.Received(1).ExecutePostRequest(expectedUrl, string.Empty);
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Detects FSM mode off when GetApplicationInfo returns useStaticFileContent=true and populated staticFileContent.")]
	public void GetFsmMode_Should_Return_Off_When_ApplicationInfo_Represents_Fsm_Off()
	{
		// Arrange
		FsmModeTool tool = CreateTool(
			"""
			{
			  "applicationInfo": {
			    "useStaticFileContent": true,
			    "staticFileContent": {
			      "schemasRuntimePath": "conf/content",
			      "resourcesRuntimePath": "conf/content/resources/en-US"
			    }
			  }
			}
			""");

		// Act
		FsmModeStatusResult result = tool.GetFsmMode("sandbox");

		// Assert
		result.Mode.Should().Be("off",
			because: "useStaticFileContent=true with populated staticFileContent represents FSM off");
		result.UseStaticFileContent.Should().BeTrue(
			because: "the structured result should preserve the raw GetApplicationInfo flag");
		result.StaticFileContent.Should().NotBeNull(
			because: "the structured result should preserve the populated staticFileContent payload");
		result.StaticFileContent!.SchemasRuntimePath.Should().Be("conf/content",
			because: "the result should expose schemasRuntimePath when staticFileContent is populated");
		result.StaticFileContent.ResourcesRuntimePath.Should().Be("conf/content/resources/en-US",
			because: "the result should expose resourcesRuntimePath when staticFileContent is populated");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects ambiguous GetApplicationInfo payloads instead of guessing the current FSM mode.")]
	public void GetFsmMode_Should_Reject_Ambiguous_Response()
	{
		// Arrange
		FsmModeTool tool = CreateTool(
			"""
			{
			  "useStaticFileContent": false,
			  "staticFileContent": {
			    "schemasRuntimePath": "conf/content"
			  }
			}
			""");

		// Act
		Action act = () => tool.GetFsmMode("sandbox");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Could not determine FSM mode*",
				because: "the tool should fail closed when the GetApplicationInfo payload does not match either supported FSM shape");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects empty staticFileContent objects instead of treating them as valid FSM off payloads.")]
	public void GetFsmMode_Should_Reject_Empty_Static_File_Content_Object()
	{
		// Arrange
		FsmModeTool tool = CreateTool(
			"""
			{
			  "useStaticFileContent": true,
			  "staticFileContent": {}
			}
			""");

		// Act
		Action act = () => tool.GetFsmMode("sandbox");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Could not determine FSM mode*",
				because: "an empty staticFileContent object is not a populated OFF payload and should fail closed");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects responses that expose more than one candidate payload with FSM status keys.")]
	public void GetFsmMode_Should_Reject_Multiple_Candidate_Payloads()
	{
		// Arrange
		FsmModeTool tool = CreateTool(
			"""
			{
			  "one": {
			    "useStaticFileContent": false,
			    "staticFileContent": null
			  },
			  "two": {
			    "useStaticFileContent": true,
			    "staticFileContent": {
			      "schemasRuntimePath": "conf/content"
			    }
			  }
			}
			""");

		// Act
		Action act = () => tool.GetFsmMode("sandbox");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*multiple payload candidates*",
				because: "the tool should fail closed when the response exposes more than one candidate payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps mode=on into TurnFsmCommandOptions when changing FSM mode through MCP.")]
	public void SetFsmMode_Should_Map_On_Mode_To_TurnFsm_Command_Options()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeTurnFsmCommand defaultCommand = new();
		FakeTurnFsmCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<TurnFsmCommand>(Arg.Any<TurnFsmCommandOptions>()).Returns(resolvedCommand);
		IFsmModeStatusService fsmModeStatusService = Substitute.For<IFsmModeStatusService>();
		FsmModeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, fsmModeStatusService);

		try
		{
			// Act
			CommandExecutionResult result = tool.SetFsmMode(new SetFsmModeArgs("sandbox", "on"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should forward a valid turn-fsm payload for mode on");
			commandResolver.Received(1).Resolve<TurnFsmCommand>(Arg.Is<TurnFsmCommandOptions>(options =>
				options.Environment == "sandbox" &&
				options.IsFsm == "on"));
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "the environment-aware FSM tool should execute the resolved command instance");
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved turn-fsm command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.IsFsm.Should().Be("on",
				because: "the requested mode must be preserved");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Maps mode=off into TurnFsmCommandOptions when changing FSM mode through MCP.")]
	public void SetFsmMode_Should_Map_Off_Mode_To_TurnFsm_Command_Options()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeTurnFsmCommand defaultCommand = new();
		FakeTurnFsmCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<TurnFsmCommand>(Arg.Any<TurnFsmCommandOptions>()).Returns(resolvedCommand);
		IFsmModeStatusService fsmModeStatusService = Substitute.For<IFsmModeStatusService>();
		FsmModeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, fsmModeStatusService);

		try
		{
			// Act
			CommandExecutionResult result = tool.SetFsmMode(new SetFsmModeArgs("sandbox", "off"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should forward a valid turn-fsm payload for mode off");
			commandResolver.Received(1).Resolve<TurnFsmCommand>(Arg.Is<TurnFsmCommandOptions>(options =>
				options.Environment == "sandbox" &&
				options.IsFsm == "off"));
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved turn-fsm command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.IsFsm.Should().Be("off",
				because: "the requested mode must be preserved");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Marks get-fsm-mode as read-only and set-fsm-mode as destructive in MCP metadata.")]
	public void FsmModeTools_Should_Expose_Expected_Mcp_Metadata()
	{
		// Arrange
		McpServerToolAttribute getAttribute = (McpServerToolAttribute)typeof(FsmModeTool)
			.GetMethod(nameof(FsmModeTool.GetFsmMode))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		McpServerToolAttribute setAttribute = (McpServerToolAttribute)typeof(FsmModeTool)
			.GetMethod(nameof(FsmModeTool.SetFsmMode))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		getAttribute.ReadOnly.Should().BeTrue(
			because: "querying FSM mode should not mutate the target environment");
		getAttribute.Destructive.Should().BeFalse(
			because: "querying FSM mode should be non-destructive");
		getAttribute.Idempotent.Should().BeTrue(
			because: "repeating the FSM mode query should produce the same effect");
		setAttribute.ReadOnly.Should().BeFalse(
			because: "changing FSM mode mutates the target environment");
		setAttribute.Destructive.Should().BeTrue(
			because: "turning FSM mode on or off changes runtime behavior and package storage state");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for FSM tools references the exact tool names and the follow-up compilation suggestion.")]
	public void FsmModePrompt_Should_Reference_Exact_Tool_Names_And_Compilation_Follow_Up()
	{
		// Arrange

		// Act
		string getPrompt = FsmAndCompilePrompt.GetFsmMode("sandbox");
		string setPrompt = FsmAndCompilePrompt.SetFsmMode("sandbox", "on");

		// Assert
		getPrompt.Should().Contain(FsmModeTool.GetFsmModeToolName,
			because: "the prompt should reference the exact MCP tool name for FSM status detection");
		getPrompt.Should().Contain(FsmModeTool.SetFsmModeToolName,
			because: "the prompt should guide agents to the companion FSM mode change tool");
		setPrompt.Should().Contain(CompileCreatioTool.CompileCreatioToolName,
			because: "the FSM mode change prompt should point agents to the recommended full compilation step");
	}

	private static FsmModeTool CreateTool(string responsePayload)
	{
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		settingsRepository.FindEnvironment("sandbox").Returns(new EnvironmentSettings
		{
			Uri = "http://sandbox",
			Login = "Supervisor",
			Password = "Supervisor"
		});
		applicationClientFactory.CreateEnvironmentClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(responsePayload);

		IFsmModeStatusService fsmModeStatusService = new FsmModeStatusService(settingsRepository, applicationClientFactory);
		return new FsmModeTool(
			new FakeTurnFsmCommand(),
			ConsoleLogger.Instance,
			Substitute.For<IToolCommandResolver>(),
			fsmModeStatusService);
	}

	private sealed class FakeTurnFsmCommand : TurnFsmCommand
	{
		public TurnFsmCommandOptions? CapturedOptions { get; private set; }

		public FakeTurnFsmCommand()
			: base(
				new SetFsmConfigCommand(
					Substitute.For<IValidator<SetFsmConfigOptions>>(),
					Substitute.For<ISettingsRepository>(),
					new Clio.Common.FileSystem(new System.IO.Abstractions.FileSystem()),
					Substitute.For<ILogger>()),
				new LoadPackagesToFileSystemCommand(
					Substitute.For<Clio.Package.IFileDesignModePackages>(),
					Substitute.For<ILogger>()),
				new LoadPackagesToDbCommand(
					Substitute.For<Clio.Package.IFileDesignModePackages>(),
					Substitute.For<ILogger>()),
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				new RestartCommand(Substitute.For<IApplicationClient>(), new EnvironmentSettings()))
		{
		}

		public override int Execute(TurnFsmCommandOptions options)
		{
			CapturedOptions = options;
			return 0;
		}
	}
}
