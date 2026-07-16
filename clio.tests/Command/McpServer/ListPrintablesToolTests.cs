using System;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListPrintablesToolTests {

	/// <summary>
	/// Fake command capturing the options it was invoked with and returning a canned
	/// successful probe response — lets the tool tests assert argument mapping and
	/// environment-scoped resolution without any transport.
	/// </summary>
	private sealed class FakeListPrintablesCommand : ListPrintablesCommand {
		public FakeListPrintablesCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public ListPrintablesOptions CapturedOptions { get; private set; }

		public override bool TryGetPrintables(ListPrintablesOptions options, out ListPrintablesResponse response) {
			CapturedOptions = options;
			response = new ListPrintablesResponse {
				Success = true,
				Count = 1,
				Printables = [
					new PrintableSummary(
						"11111111-1111-1111-1111-111111111111",
						"Contact card",
						ConvertInPdf: true,
						ShowInCard: true,
						ShowInSection: false,
						"Contact",
						null)
				]
			};
			return true;
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the command for the requested environment via the resolver and maps every argument onto the options — the startup-time default command must stay unused.")]
	public void ListPrintables_ShouldResolveCommandForRequestedEnvironment_WhenEnvironmentNamePassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListPrintablesCommand defaultCommand = new();
		FakeListPrintablesCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListPrintablesCommand>(Arg.Any<ListPrintablesOptions>())
			.Returns(resolvedCommand);
		ListPrintablesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListPrintablesResponse response = tool.ListPrintables(
			new ListPrintablesArgs("Contact", "docker_fix2", null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command returns a successful probe result");
		response.Printables.Should().ContainSingle(because: "the canned command result flows through unchanged")
			.Which.TemplateId.Should().Be("11111111-1111-1111-1111-111111111111",
				because: "the printable summary must surface verbatim");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command should run");
		resolvedCommand.CapturedOptions.EntityName.Should().Be("Contact",
			because: "the entity-name argument is mapped onto the options");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2",
			because: "the environment argument is mapped onto the options");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the startup-time default command must not be used for an environment-bound call");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure envelope when command resolution throws (e.g. unknown environment) instead of surfacing an exception over MCP.")]
	public void ListPrintables_ShouldReturnError_WhenCommandResolutionFails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListPrintablesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListPrintablesCommand>(Arg.Any<ListPrintablesOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'missing' is not registered."));
		ListPrintablesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListPrintablesResponse response = tool.ListPrintables(
			new ListPrintablesArgs(null, "missing", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "resolution failed, so no printables could be listed");
		response.Error.Should().Contain("missing",
			because: "the failure should surface the unresolved environment name");
		ConsoleLogger.Instance.ClearMessages();
	}

}
