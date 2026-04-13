using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class GetPkgListToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for get-pkg-list so tests and callers reference the same production constant.")]
	public void GetPkgList_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetPkgListTool)
			.GetMethod(nameof(GetPkgListTool.GetPkgList))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(GetPkgListTool.GetPkgListToolName,
			because: "unit tests must reference the production MCP tool-name constant instead of duplicating the string literal");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a fresh get-pkg-list command for the requested environment and returns structured package data filtered by the requested search pattern.")]
	public void GetPkgList_Should_Resolve_Command_And_Return_Filtered_Structured_Result() {
		// Arrange
		IApplicationPackageListProvider packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		packageListProvider.GetPackages().Returns(new[] {
			CreatePackageInfo("AlphaPkg", "1.2.3", "Maintainer A"),
			CreatePackageInfo("BetaPkg", "2.0.0", "Maintainer B")
		});
		IJsonResponseFormater jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		IClioGateway clioGateway = Substitute.For<IClioGateway>();
		clioGateway.IsCompatibleWith("2.0.0.0").Returns(true);
		ILogger logger = Substitute.For<ILogger>();
		GetPkgListCommand defaultCommand = new(
			new EnvironmentSettings(),
			packageListProvider,
			jsonResponseFormater,
			logger,
			clioGateway);
		GetPkgListCommand resolvedCommand = new(
			new EnvironmentSettings(),
			packageListProvider,
			jsonResponseFormater,
			logger,
			clioGateway);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetPkgListCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		GetPkgListTool tool = new(defaultCommand, logger, commandResolver);

		// Act
		IReadOnlyList<PackageListItemResult> result = tool.GetPkgList(new GetPkgListArgs("sandbox", "beta"));

		// Assert
		commandResolver.Received(1).Resolve<GetPkgListCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "sandbox"));
		result.Should().ContainSingle(because: "the filter should narrow the structured MCP payload to matching packages only");
		PackageListItemResult package = result.Single();
		package.Name.Should().Be("BetaPkg",
			because: "the MCP tool should preserve the package name returned by the command");
		package.Version.Should().Be("2.0.0",
			because: "the structured MCP result should expose the package version for assertions and agents");
		package.Maintainer.Should().Be("Maintainer B",
			because: "the structured MCP result should expose the package maintainer for assertions and agents");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for get-pkg-list references the exact production tool name and keeps the optional filter visible to agents.")]
	public void GetPkgListPrompt_Should_Mention_Tool_Name_And_Filter() {
		// Arrange

		// Act
		string prompt = WorkspacePackagePrompt.GetPkgList("sandbox", "PkgA");

		// Assert
		prompt.Should().Contain(GetPkgListTool.GetPkgListToolName,
			because: "the prompt should reference the production MCP tool name");
		prompt.Should().Contain("filter",
			because: "agents should be reminded that the MCP tool supports narrowing the package list");
	}

	private static PackageInfo CreatePackageInfo(string name, string version, string maintainer) {
		PackageDescriptor descriptor = new() {
			Name = name,
			PackageVersion = version,
			Maintainer = maintainer,
			UId = Guid.NewGuid()
		};
		return new PackageInfo(descriptor, string.Empty, Array.Empty<string>());
	}
}
