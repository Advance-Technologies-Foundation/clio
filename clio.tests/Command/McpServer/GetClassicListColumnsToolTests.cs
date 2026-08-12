using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class GetClassicListColumnsToolTests {

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve maps schema-name and environment-name to an environment-scoped command and returns its source-aware response.")]
	public void Resolve_ShouldUseEnvironmentScopedCommand_WhenArgumentsAreValid() {
		// Arrange
		FakeGetClassicListColumnsCommand defaultCommand = CreateCommand();
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(new GetClassicListColumnsArgs("ContactSectionV2") {
			EnvironmentName = "dev"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command returns a successful source-aware response");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContactSectionV2",
			because: "schema-name identifies the Classic section to inspect");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "environment-name must drive tenant-scoped command resolution");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the startup command must not execute for an environment-scoped request");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a typed failure when the MCP request explicitly passes null args.")]
	public void Resolve_ShouldReturnTypedFailure_WhenArgsAreNull() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(null);

		// Assert
		response.Success.Should().BeFalse(because: "args:null is invalid but must not escape as an exception");
		response.Error.Should().Contain("args", because: "the typed failure should identify the missing argument object");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve redacts backend URIs from both error and notes before returning the response to an MCP caller.")]
	public void Resolve_ShouldRedactSensitiveText_WhenCommandResponseContainsBackendUri() {
		// Arrange
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand(new GetClassicListColumnsResponse {
			Success = false,
			Error = "POST https://secret-host.example.com/0/DataService failed",
			Notes = ["Loaded from https://secret-host.example.com/0/schema"]
		});
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(new GetClassicListColumnsArgs("ContactSectionV2") {
			EnvironmentName = "dev"
		});

		// Assert
		response.Error.Should().Contain("[redacted-uri]",
			because: "the MCP error channel must not expose a backend URI");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "the backend host is sensitive connection detail");
		response.Notes.Should().ContainSingle().Which.Should().Contain("[redacted-uri]",
			because: "notes are a second response channel and need the same redaction");
	}

	private static FakeGetClassicListColumnsCommand CreateCommand(
		GetClassicListColumnsResponse response = null) => new(response);

	private sealed class FakeGetClassicListColumnsCommand : GetClassicListColumnsCommand {
		public GetClassicListColumnsOptions CapturedOptions { get; private set; }
		private readonly GetClassicListColumnsResponse _response;

		public FakeGetClassicListColumnsCommand(GetClassicListColumnsResponse response)
			: base(Substitute.For<IClassicListColumnResolver>(), ConsoleLogger.Instance) {
			_response = response;
		}

		public override bool TryResolve(
			GetClassicListColumnsOptions options,
			out GetClassicListColumnsResponse response) {
			CapturedOptions = options;
			response = _response ?? new GetClassicListColumnsResponse {
				Success = true,
				SectionSchema = options.SchemaName,
				Entity = "Contact",
				Source = "entity-default",
				Columns = [new ClassicListColumnInfo("Name", "Full name")]
			};
			return response.Success;
		}
	}
}
