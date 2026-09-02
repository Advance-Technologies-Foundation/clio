using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class CreateBusinessProcessToolTests {
	private const string SampleDescriptor =
		"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	[Test]
	[Description("Resolves the create-business-process MCP tool for the requested environment and forwards the inline descriptor and package override into command options.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Resolve_Command_And_Forward_Descriptor() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand defaultCommand = new();
		FakeCreateBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>())
			.Returns(resolvedCommand);
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", SampleDescriptor, "MyApp"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the create-business-process tool should forward a valid command payload for the requested environment");
		commandResolver.Received(1).Resolve<CreateBusinessProcessCommand>(Arg.Is<CreateBusinessProcessOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.DescriptorJson == SampleDescriptor &&
			options.PackageName == "MyApp"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the startup one");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded create-business-process options");
		resolvedCommand.CapturedOptions!.DescriptorJson.Should().Be(SampleDescriptor,
			because: "the inline descriptor must be carried through to the command without modification");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process emits the deterministic compile-not-required note after a successful create, so an agent does not mistake 'created' for 'must be compiled to run' and force compile-creatio (ENG-95706).")]
	public void CreateBusinessProcess_Should_Emit_CompileNotRequiredNote_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>())
			.Returns(resolvedCommand);
		CreateBusinessProcessTool tool = new(new FakeCreateBusinessProcessCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", SampleDescriptor, "MyApp"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the fake command reports a successful create");
		result.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "a clio-built process is interpreted and needs no compile; the response note is the one channel the agent cannot skip, and it stops the force-compile the incident showed (ENG-95706)");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process suppresses the compile-not-required note when the create FAILS — a failed mutation must not be told 'no compile needed' (ENG-95706; mirrors UpdateEntitySchema_Should_NotEmitCompileNotRequiredNote_WhenUpdateFails).")]
	public void CreateBusinessProcess_Should_Not_Emit_CompileNotRequiredNote_On_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand resolvedCommand = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>())
			.Returns(resolvedCommand);
		CreateBusinessProcessTool tool = new(new FakeCreateBusinessProcessCommand(exitCode: 1), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", SampleDescriptor, "MyApp"));

		// Assert
		result.ExitCode.Should().NotBe(0, because: "the fake command reports a failed create");
		result.Note.Should().NotBe(CommandExecutionResult.CompileNotRequiredNote,
			because: "a success-only signal must not ride a failed mutation — a failed create may still need follow-up work");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a descriptor that contains a sendEmail element with its full email block verbatim — the tool is an opaque pass-through, so the new element type and every email field (mode, sender, To/Cc recipients, subject, HTML body — including the [[param:…]] / [[element:…]] process-macro placeholders clio never resolves or rewrites, the server does — importance, ignoreErrors, manual-mode performer), plus the readData element, flows and parameters the macros reference, ride through to the command byte-for-byte.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Forward_SendEmail_Descriptor_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string sendEmailDescriptor =
			"{\"name\":\"UsrSendEmailProc\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"ReadOrder\",\"type\":\"readData\",\"readData\":{\"source\":\"Order\",\"mode\":\"first\"}},{\"name\":\"SendEmail1\","
			+ "\"type\":\"sendEmail\",\"email\":{\"mode\":\"manual\",\"sender\":\"sales@example.com\","
			+ "\"subject\":\"Order update\",\"body\":\"<p>Hello [[param:ContactName]], order [[element:ReadOrder.ResultEntity.Number]]</p>\",\"bodyFormat\":\"html\","
			+ "\"to\":[{\"value\":\"to@example.com\"}],\"cc\":[{\"processParameter\":\"ManagerContact\"}],"
			+ "\"importance\":\"high\",\"ignoreErrors\":true,"
			+ "\"performer\":{\"type\":\"role\",\"role\":\"All employees\",\"showPage\":true}}}],"
			+ "\"flows\":[{\"source\":\"ReadOrder\",\"target\":\"SendEmail1\"}],"
			+ "\"parameters\":[{\"name\":\"ContactName\",\"type\":\"Text\",\"direction\":\"In\"}]}";
		FakeCreateBusinessProcessCommand defaultCommand = new();
		FakeCreateBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>())
			.Returns(resolvedCommand);
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", sendEmailDescriptor, null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid sendEmail descriptor must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded sendEmail descriptor");
		resolvedCommand.CapturedOptions!.DescriptorJson.Should().Be(sendEmailDescriptor,
			because: "the sendEmail element, its whole email block, and the [[param:…]] / [[element:…]] body "
				+ "placeholders must pass through byte-for-byte (opaque pass-through) — clio never resolves or "
				+ "rewrites the macros; the server does");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the environment name is empty.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("   ", SampleDescriptor, null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment name is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the descriptor is empty.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Fail_When_Descriptor_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", "   ", null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty descriptor is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a descriptor that contains a changeAccessRights element with its full accessRights block verbatim — the tool is an opaque pass-through, so the target object, considerTimeInFilter, both permission collections and every grantee kind (role by name, employee by contact formula, and selectedEmployees with its Contact-rooted filter), plus the element record filter that decides WHICH records are affected, ride through to the command byte-for-byte. Guards the one thing this repo CAN assert about the accessRights contract: that clio does not reshape it on the way to the server.")]
	[Category("Unit")]
	public void CreateBusinessProcess_Should_Forward_AccessRights_Descriptor_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string accessRightsDescriptor =
			"{\"name\":\"UsrGrantRightsProc\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"GrantRights\","
			+ "\"type\":\"changeAccessRights\",\"caption\":\"Grant rights\",\"accessRights\":{\"object\":\"Order\","
			+ "\"considerTimeInFilter\":true,"
			+ "\"add\":[{\"operations\":[\"read\",\"edit\"],\"level\":\"delegate\","
			+ "\"grantee\":{\"type\":\"role\",\"role\":\"All employees\"}},"
			+ "{\"operations\":[\"delete\"],\"grantee\":{\"type\":\"selectedEmployees\","
			+ "\"filter\":{\"conditions\":[{\"column\":\"Name\",\"comparison\":\"contain\",\"value\":\"Supervisor\"}]}}}],"
			+ "\"remove\":[{\"operations\":[\"delete\"],"
			+ "\"grantee\":{\"type\":\"employee\",\"contact\":\"[#SysVariable.CurrentUserContact#]\"}}]},"
			+ "\"filter\":{\"object\":\"Order\",\"conditions\":[{\"column\":\"Id\",\"comparison\":\"equal\","
			+ "\"processParameter\":\"OrderId\"}]}}],"
			+ "\"parameters\":[{\"name\":\"OrderId\",\"type\":\"Guid\",\"direction\":\"In\"}]}";
		FakeCreateBusinessProcessCommand defaultCommand = new();
		FakeCreateBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>())
			.Returns(resolvedCommand);
		CreateBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			new CreateBusinessProcessArgs("docker_fix2", accessRightsDescriptor, null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid changeAccessRights descriptor must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded accessRights descriptor");
		resolvedCommand.CapturedOptions!.DescriptorJson.Should().Be(accessRightsDescriptor,
			because: "the accessRights block, every grantee kind and the element record filter must pass "
				+ "through byte-for-byte — the server owns the semantics, and this element has no output "
				+ "parameters, so a reshaping here would change what rights are written with nothing to report it");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateBusinessProcessCommand : CreateBusinessProcessCommand {
		private readonly int _exitCode;

		public CreateBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeCreateBusinessProcessCommand(int exitCode = 0)
			: base(Substitute.For<ICreateBusinessProcessService>(), Substitute.For<IProcessDescriber>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public override int Execute(CreateBusinessProcessOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
