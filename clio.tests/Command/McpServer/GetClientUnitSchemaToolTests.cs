using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class GetClientUnitSchemaToolTests {

	[Test]
	[Category("Unit")]
	public void GetSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		FakeGetClientUnitSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(resolvedCommand);
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetClientUnitSchemaResponse response = tool.GetSchema(new GetClientUnitSchemaArgs("NetworkUtilities") {
			OutputFile = "/tmp/out.js", EnvironmentName = "dev" });

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("NetworkUtilities");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/out.js");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void GetSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClientUnitSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClientUnitSchemaCommand>(Arg.Any<GetClientUnitSchemaOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		GetClientUnitSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetClientUnitSchemaResponse response = tool.GetSchema(new GetClientUnitSchemaArgs("NetworkUtilities") {
			EnvironmentName = "dev" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeGetClientUnitSchemaCommand : GetClientUnitSchemaCommand {
		public GetClientUnitSchemaOptions CapturedOptions { get; private set; }

		public FakeGetClientUnitSchemaCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSchema(GetClientUnitSchemaOptions options, out GetClientUnitSchemaResponse response) {
			CapturedOptions = options;
			response = new GetClientUnitSchemaResponse {
				Success = true,
				SchemaName = options.SchemaName
			};
			return true;
		}
	}
}
