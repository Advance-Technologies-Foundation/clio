using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class GetClassicSchemaToolTests {

	private const string SampleUId = "948080fc-031e-4d88-9239-47bcedaa92bc";

	[Test]
	[Category("Unit")]
	public void GetSchema_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClassicSchemaCommand defaultCommand = new();
		FakeGetClassicSchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicSchemaCommand>(Arg.Any<GetClassicSchemaOptions>())
			.Returns(resolvedCommand);
		GetClassicSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetClassicSchemaResponse response = tool.GetSchema(new GetClassicSchemaArgs(SampleUId) {
			OutputFile = "/tmp/out.js", EnvironmentName = "dev" });

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaUId.Should().Be(SampleUId);
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/out.js");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void GetSchema_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetClassicSchemaCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicSchemaCommand>(Arg.Any<GetClassicSchemaOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		GetClassicSchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetClassicSchemaResponse response = tool.GetSchema(new GetClassicSchemaArgs(SampleUId) {
			EnvironmentName = "dev" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeGetClassicSchemaCommand : GetClassicSchemaCommand {
		public GetClassicSchemaOptions CapturedOptions { get; private set; }

		public FakeGetClassicSchemaCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSchema(GetClassicSchemaOptions options, out GetClassicSchemaResponse response) {
			CapturedOptions = options;
			response = new GetClassicSchemaResponse {
				Success = true,
				SchemaUId = options.SchemaUId
			};
			return true;
		}
	}
}
