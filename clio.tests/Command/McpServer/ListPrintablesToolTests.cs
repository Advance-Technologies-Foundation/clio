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

	/// <summary>
	/// Fake command whose probe fails with a transport error carrying a sensitive target URI — exercises
	/// the MCP-boundary redaction of the command-produced error (the raw exception message the command
	/// stores on the failure response).
	/// </summary>
	private sealed class FailingListPrintablesCommand : ListPrintablesCommand {
		public FailingListPrintablesCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetPrintables(ListPrintablesOptions options, out ListPrintablesResponse response) {
			response = new ListPrintablesResponse {
				Success = false,
				Error = "SelectQuery failed against https://tenant-secret.creatio.com/0/DataService/json/reply/SelectQuery"
			};
			return false;
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

	[Test]
	[Category("Unit")]
	[Description("Redacts the command-produced failure error at the MCP boundary — a raw transport exception message can carry the target URI/host or credentials, so the tool must scrub it before returning it to the client (mirrors the resolution-failure redaction).")]
	public void ListPrintables_ShouldRedactCommandProducedError_WhenTryGetPrintablesFails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FailingListPrintablesCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListPrintablesCommand>(Arg.Any<ListPrintablesOptions>())
			.Returns(resolvedCommand);
		ListPrintablesTool tool = new(new FakeListPrintablesCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		ListPrintablesResponse response = tool.ListPrintables(
			new ListPrintablesArgs(null, "docker_fix2", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "the probe reported a transport failure");
		response.Error.Should().NotContain("tenant-secret.creatio.com",
			because: "the raw target host from the command's exception message must not cross into the MCP client transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the boundary must replace the leaked URI with the stable redaction placeholder");
		ConsoleLogger.Instance.ClearMessages();
	}

}
