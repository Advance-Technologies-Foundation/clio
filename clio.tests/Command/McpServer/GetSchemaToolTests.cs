using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class GetSchemaToolTests {

	[Test]
	[Category("Unit")]
	[Description("Routes schema-type='source-code' through the get-source-code-schema command pipeline.")]
	public void GetSchema_SourceCodeMode_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetSourceCodeSchemaCommand defaultCommand = new();
		FakeGetSourceCodeSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetSourceCodeSchemaCommand>(Arg.Any<GetSourceCodeSchemaOptions>())
			.Returns(resolvedCommand);
		GetSchemaTool tool = new(
			defaultCommand,
			ConsoleLogger.Instance,
			commandResolver,
			clientUnit: null!,
			sql: null!,
			entityProperties: null!,
			entityColumnProperties: null!);

		object result = tool.Get(new GetSchemaArgs(
			SchemaType: SchemaCreateTool.SchemaTypeSourceCode,
			SchemaName: "UsrHelper",
			OutputFile: "/tmp/out.cs",
			EnvironmentName: "docker_fix2"));

		GetSourceCodeSchemaResponse response = result.Should().BeOfType<GetSourceCodeSchemaResponse>().Subject;
		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrHelper");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/out.cs");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces environment resolution errors from the source-code path as a Response.Error.")]
	public void GetSchema_SourceCodeMode_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetSourceCodeSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetSourceCodeSchemaCommand>(Arg.Any<GetSourceCodeSchemaOptions>())
			.Returns(_ => throw new System.InvalidOperationException("Environment 'missing' is not registered."));
		GetSchemaTool tool = new(
			defaultCommand,
			ConsoleLogger.Instance,
			commandResolver,
			clientUnit: null!,
			sql: null!,
			entityProperties: null!,
			entityColumnProperties: null!);

		object result = tool.Get(new GetSchemaArgs(
			SchemaType: SchemaCreateTool.SchemaTypeSourceCode,
			SchemaName: "UsrHelper",
			EnvironmentName: "missing"));

		GetSourceCodeSchemaResponse response = result.Should().BeOfType<GetSourceCodeSchemaResponse>().Subject;
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("missing");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an unknown schema-type value with a clear listing of allowed values.")]
	public void GetSchema_Should_Reject_Unknown_SchemaType() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetSourceCodeSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetSchemaTool tool = new(
			defaultCommand,
			ConsoleLogger.Instance,
			commandResolver,
			clientUnit: null!,
			sql: null!,
			entityProperties: null!,
			entityColumnProperties: null!);

		object result = tool.Get(new GetSchemaArgs(
			SchemaType: "bogus",
			SchemaName: "UsrHelper"));

		CommandExecutionResult err = result.Should().BeOfType<CommandExecutionResult>().Subject;
		err.ExitCode.Should().Be(-1,
			because: "an unknown schema-type should be rejected without invoking any underlying command");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects 'column' for non-entity schema-types so the column fold only honors entity reads.")]
	public void GetSchema_NonEntity_Should_Reject_Column_Argument() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetSourceCodeSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetSchemaTool tool = new(
			defaultCommand,
			ConsoleLogger.Instance,
			commandResolver,
			clientUnit: null!,
			sql: null!,
			entityProperties: null!,
			entityColumnProperties: null!);

		object result = tool.Get(new GetSchemaArgs(
			SchemaType: SchemaCreateTool.SchemaTypeSourceCode,
			SchemaName: "UsrHelper",
			EnvironmentName: "dev",
			Column: "UsrName"));

		CommandExecutionResult err = result.Should().BeOfType<CommandExecutionResult>().Subject;
		err.ExitCode.Should().Be(-1,
			because: "the column parameter is only honored for schema-type='entity' and must be rejected otherwise");
	}

	private sealed class FakeGetSourceCodeSchemaCommand : GetSourceCodeSchemaCommand {
		public GetSourceCodeSchemaOptions CapturedOptions { get; private set; }

		public FakeGetSourceCodeSchemaCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSchema(GetSourceCodeSchemaOptions options, out GetSourceCodeSchemaResponse response) {
			CapturedOptions = options;
			response = new GetSourceCodeSchemaResponse {
				Success = true,
				SchemaName = options.SchemaName
			};
			return true;
		}
	}
}
