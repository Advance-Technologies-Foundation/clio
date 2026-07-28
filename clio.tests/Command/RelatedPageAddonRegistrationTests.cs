namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

/// <summary>
/// Verifies the related-page commands and their MCP tools resolve from the REAL <see cref="BindingsModule"/> DI
/// graph. The behavioral command tests construct the SUT directly with mocks, which cannot catch a missing or
/// broken container registration — that would pass every unit test and only fail at CLI/MCP runtime. Resolving
/// through the container here closes that gap: a dropped <c>AddTransient</c> or an unresolvable dependency fails
/// this fixture instead.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RelatedPageAddonRegistrationTests : BaseClioModuleTests {
	[Test]
	[Description("create-related-page-addon command resolves from the real BindingsModule DI graph, so a missing/broken registration fails here rather than only at CLI runtime.")]
	public void CreateCommand_ShouldResolveFromDi() {
		CreateRelatedPageAddonCommand command = Container.GetRequiredService<CreateRelatedPageAddonCommand>();

		command.Should().NotBeNull(
			because: "CreateRelatedPageAddonCommand and its whole dependency graph must be registered in BindingsModule");
	}

	[Test]
	[Description("get-related-page-addon command resolves from the real BindingsModule DI graph.")]
	public void GetCommand_ShouldResolveFromDi() {
		GetRelatedPageAddonCommand command = Container.GetRequiredService<GetRelatedPageAddonCommand>();

		command.Should().NotBeNull(
			because: "GetRelatedPageAddonCommand and its whole dependency graph must be registered in BindingsModule");
	}

	[Test]
	[Description("The MCP tools wrapping the related-page commands also resolve from the real BindingsModule DI graph.")]
	public void McpTools_ShouldResolveFromDi() {
		Container.GetRequiredService<CreateRelatedPageAddonTool>().Should().NotBeNull(
			because: "the create-related-page-addon MCP tool must be registered with a resolvable dependency graph");
		Container.GetRequiredService<GetRelatedPageAddonTool>().Should().NotBeNull(
			because: "the get-related-page-addon MCP tool must be registered with a resolvable dependency graph");
	}
}
