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
	public void GetSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetSourceCodeSchemaCommand defaultCommand = new();
		FakeGetSourceCodeSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetSourceCodeSchemaCommand>(Arg.Any<GetSourceCodeSchemaOptions>())
			.Returns(resolvedCommand);
		GetSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetSourceCodeSchemaResponse response = tool.GetSchema(new GetSchemaArgs("UsrHelper") {
			OutputFile = "/tmp/out.cs", EnvironmentName = "docker_fix2" });

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
	public void GetSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetSourceCodeSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetSourceCodeSchemaCommand>(Arg.Any<GetSourceCodeSchemaOptions>())
			.Returns(_ => throw new System.InvalidOperationException("Environment 'missing' is not registered."));
		GetSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetSourceCodeSchemaResponse response = tool.GetSchema(new GetSchemaArgs("UsrHelper") {
			EnvironmentName = "missing" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("missing");
		ConsoleLogger.Instance.ClearMessages();
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
