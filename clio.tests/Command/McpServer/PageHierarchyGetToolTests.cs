using System.Collections.Generic;
using System.Net.Http;
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

	[Test]
	[Category("Unit")]
	[Description("get-page-hierarchy returns a failure envelope (not a chain) when the design-package lookup never answered, so an agent cannot read a partial answer as the page hierarchy.")]
	public void GetHierarchy_ShouldReportFailure_WhenDesignPackageLookupTransportFails() {
		// Arrange — the real command behind the tool: metadata resolves, the design-package endpoint is
		// unreachable, and the designer endpoint would still hand back a chain anchored on the wrong package.
		ConsoleLogger.Instance.ClearMessages();
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[{\"UId\":\"schema-uid\",\"PackageUId\":\"pkg-uid\"}]}");
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(Arg.Any<string>())
			.Returns(_ => throw new HttpRequestException("connection reset by peer"));
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
			new List<PageDesignerHierarchySchema> {
				new() { UId = "schema-uid", Name = "UsrLeaf_FormPage", Body = "leaf-body" }
			});
		GetPageHierarchyCommand resolvedCommand = new(
			applicationClient, urlBuilder, hierarchyClient, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetPageHierarchyCommand>(Arg.Any<GetPageHierarchyOptions>())
			.Returns(resolvedCommand);
		PageHierarchyGetTool tool = new(new FakeGetPageHierarchyCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetPageHierarchyResponse response = tool.GetHierarchy(new GetPageHierarchyArgs(
			"UsrLeaf_FormPage",
			MetadataOnly: null,
			Offset: null,
			Limit: null,
			EnvironmentName: "workbuild103",
			Uri: null,
			Login: null,
			Password: null));

		// Assert
		response.Success.Should().BeFalse(
			because: "a read that never answered must reach the MCP caller as a failure, not as a success payload");
		response.Error.Should().Contain("connection reset by peer",
			because: "the tool result names the transport failure the agent has to act on");
		response.Schemas.Should().BeNull(
			because: "no chain may be returned for a failed read, or an agent will treat it as the page hierarchy");
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
