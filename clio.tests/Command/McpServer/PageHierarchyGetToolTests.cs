using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class PageHierarchyGetToolTests {

	[Test]
	[Category("Unit")]
	[Description("get-page-hierarchy resolves the command for the requested environment and forwards schema/paging options.")]
	public void GetHierarchy_Should_Resolve_Command_And_Forward_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetPageHierarchyCommand defaultCommand = new();
		FakeGetPageHierarchyCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetPageHierarchyCommand>(Arg.Any<GetPageHierarchyOptions>())
			.Returns(resolvedCommand);
		PageHierarchyGetTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetPageHierarchyResponse response = tool.GetHierarchy(new GetPageHierarchyArgs(
			"UsrApplicants_FormPage",
			MetadataOnly: true,
			Offset: 2,
			Limit: 5,
			EnvironmentName: "workbuild103",
			Uri: null,
			Login: null,
			Password: null));

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command returns a successful canned response");
		commandResolver.Received(1).Resolve<GetPageHierarchyCommand>(Arg.Is<GetPageHierarchyOptions>(o =>
			o.SchemaName == "UsrApplicants_FormPage"
			&& o.Environment == "workbuild103"
			&& o.MetadataOnly
			&& o.Offset == 2
			&& o.Limit == 5));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the startup-injected command must not run for an environment-scoped call");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the environment-resolved command is the one that executes");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrApplicants_FormPage",
			because: "the resolved command receives the requested schema name");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeGetPageHierarchyCommand : GetPageHierarchyCommand {
		public GetPageHierarchyOptions CapturedOptions { get; private set; }

		public FakeGetPageHierarchyCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IPageDesignerHierarchyClient>(),
				Substitute.For<ILogger>()) {
		}

		public override bool TryGetHierarchy(GetPageHierarchyOptions options, out GetPageHierarchyResponse response) {
			CapturedOptions = options;
			response = new GetPageHierarchyResponse { Success = true, SchemaName = options.SchemaName };
			return true;
		}
	}
}
