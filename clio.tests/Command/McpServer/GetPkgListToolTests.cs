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
	[Description("Advertises the stable MCP tool name for list-packages so tests and callers reference the same production constant.")]
	public void GetPkgList_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetPkgListTool)
			.GetMethod(nameof(GetPkgListTool.GetPkgList))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		string toolName = attribute.Name;

		toolName.Should().Be(GetPkgListTool.GetPkgListToolName,
			because: "unit tests must reference the production MCP tool-name constant instead of duplicating the string literal");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a fresh list-packages command for the requested environment and returns structured package data filtered by the requested search pattern.")]
	public void GetPkgList_Should_Resolve_Command_And_Return_Filtered_Structured_Result() {
		IApplicationPackageListProvider packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		packageListProvider.GetPackages().Returns(new[] {
			CreatePackageInfo("AlphaPkg", "1.2.3", "Maintainer A"),
			CreatePackageInfo("BetaPkg", "2.0.0", "Maintainer B")
		});
		IJsonResponseFormater jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		ILogger logger = Substitute.For<ILogger>();
		GetPkgListCommand defaultCommand = new(
			new EnvironmentSettings(),
			packageListProvider,
			jsonResponseFormater,
			logger);
		GetPkgListCommand resolvedCommand = new(
			new EnvironmentSettings(),
			packageListProvider,
			jsonResponseFormater,
			logger);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetPkgListCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		GetPkgListTool tool = new(defaultCommand, logger, commandResolver);

		PackageListResponse result = tool.GetPkgList(new GetPkgListArgs("sandbox", "beta"));

		commandResolver.Received(1).Resolve<GetPkgListCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "sandbox"));
		result.Packages.Should().ContainSingle(because: "the filter should narrow the structured MCP payload to matching packages only");
		result.Count.Should().Be(1, because: "count should describe the returned filtered page");
		result.Total.Should().Be(1, because: "total should describe all packages matching the filter before paging");
		result.Truncated.Should().BeFalse(because: "the single matching package fits in the default page");
		PackageListItemResult package = result.Packages.Single();
		package.Name.Should().Be("BetaPkg",
			because: "the MCP tool should preserve the package name returned by the command");
		package.Version.Should().Be("2.0.0",
			because: "the structured MCP result should expose the package version for assertions and agents");
		package.Maintainer.Should().Be("Maintainer B",
			because: "the structured MCP result should expose the package maintainer for assertions and agents");
	}

	[TestCase(null)]
	[TestCase(0)]
	[Category("Unit")]
	[Description("Applies the default 50-package limit and reports observable paging metadata when limit is omitted or zero.")]
	public void GetPkgList_ShouldApplyDefaultLimit_WhenLimitIsOmittedOrZero(int? limit) {
		// Arrange
		IApplicationPackageListProvider packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		packageListProvider.GetPackages().Returns(Enumerable.Range(0, 60)
			.Select(index => CreatePackageInfo($"Package{index:D2}", "1.0.0", "Maintainer")));
		GetPkgListTool tool = CreateTool(packageListProvider);

		// Act
		PackageListResponse result = tool.GetPkgList(new GetPkgListArgs("sandbox", Limit: limit));

		// Assert
		result.Packages.Should().HaveCount(GetPkgListTool.DefaultLimit,
			because: "an omitted limit must keep a large environment response within the default payload bound");
		result.Count.Should().Be(GetPkgListTool.DefaultLimit,
			because: "count must match the number of packages returned in this page");
		result.Total.Should().Be(60, because: "total must preserve the full match count before paging");
		result.Offset.Should().Be(0, because: "an omitted offset starts at the first matching package");
		result.Limit.Should().Be(GetPkgListTool.DefaultLimit,
			because: "the response must make the effective default limit observable");
		result.Truncated.Should().BeTrue(because: "ten matching packages remain after the default page");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the requested package window and reports the full filtered total when limit and offset are supplied.")]
	public void GetPkgList_ShouldReturnRequestedPage_WhenLimitAndOffsetAreSupplied() {
		// Arrange
		IApplicationPackageListProvider packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		packageListProvider.GetPackages().Returns(Enumerable.Range(0, 6)
			.Select(index => CreatePackageInfo($"Package{index:D2}", "1.0.0", "Maintainer")));
		GetPkgListTool tool = CreateTool(packageListProvider);

		// Act
		PackageListResponse result = tool.GetPkgList(new GetPkgListArgs("sandbox", Limit: 2, Offset: 2));

		// Assert
		result.Packages.Select(package => package.Name).Should().Equal(["Package02", "Package03"],
			because: "offset and limit must select a stable page from the name-ordered package set");
		result.Count.Should().Be(2, because: "count must describe this page only");
		result.Total.Should().Be(6, because: "total must remain independent of the requested page");
		result.Offset.Should().Be(2, because: "the response must echo the applied page offset");
		result.Limit.Should().Be(2, because: "the response must echo the applied page size");
		result.Truncated.Should().BeTrue(because: "two more packages remain after this page");
	}

	[TestCase(-1, 0, "limit")]
	[TestCase(1, -1, "offset")]
	[Category("Unit")]
	[Description("Rejects negative paging arguments before resolving or querying the target environment.")]
	public void GetPkgList_ShouldRejectNegativePaging_WhenLimitOrOffsetIsNegative(
		int limit, int offset, string expectedArgument) {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILogger logger = Substitute.For<ILogger>();
		GetPkgListCommand defaultCommand = new(
			new EnvironmentSettings(),
			Substitute.For<IApplicationPackageListProvider>(),
			Substitute.For<IJsonResponseFormater>(),
			logger);
		GetPkgListTool tool = new(defaultCommand, logger, commandResolver);

		// Act
		Action act = () => tool.GetPkgList(new GetPkgListArgs("sandbox", Limit: limit, Offset: offset));

		// Assert
		act.Should().Throw<ArgumentOutOfRangeException>()
			.WithMessage($"*{expectedArgument} must be zero or greater*",
				because: "invalid paging must fail with a caller-actionable argument name");
		commandResolver.DidNotReceive().Resolve<GetPkgListCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for list-packages references the exact production tool name and keeps the optional filter visible to agents.")]
	public void GetPkgListPrompt_Should_Mention_Tool_Name_And_Filter() {
		string prompt = WorkspacePackagePrompt.GetPkgList("sandbox", "PkgA");

		prompt.Should().Contain(GetPkgListTool.GetPkgListToolName,
			because: "the prompt should reference the production MCP tool name");
		prompt.Should().Contain("filter",
			because: "agents should be reminded that the MCP tool supports narrowing the package list");
		prompt.Should().Contain("offset + count",
			because: "the prompt should explain how to advance through all result pages");
		prompt.Should().Contain("truncated",
			because: "the prompt should use the response completeness signal instead of guessing whether more packages exist");
	}

	private static GetPkgListTool CreateTool(IApplicationPackageListProvider packageListProvider) {
		IJsonResponseFormater jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		ILogger logger = Substitute.For<ILogger>();
		GetPkgListCommand command = new(
			new EnvironmentSettings(),
			packageListProvider,
			jsonResponseFormater,
			logger);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetPkgListCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return new GetPkgListTool(command, logger, commandResolver);
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
