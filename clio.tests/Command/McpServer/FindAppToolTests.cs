using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class FindAppToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for find-app so callers and tests share the same production identifier.")]
	public void FindApp_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(FindAppTool)
			.GetMethod(nameof(FindAppTool.FindApp))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(FindAppTool.FindAppToolName,
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a success envelope carrying the applications (with sections) resolved by the environment-aware command.")]
	public void FindApp_Should_Return_Success_Envelope_With_Applications() {
		// Arrange
		IReadOnlyList<AppSearchResult> expected = [
			new AppSearchResult("11111111-1111-1111-1111-111111111111", "CrtCaseManagementApp", "Case Management", "1.0.0", null,
				[new AppSectionSearchResult("Cases", "Cases", "Case", null)])
		];
		FindAppCommand resolvedCommand = Substitute.For<FindAppCommand>(
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		resolvedCommand.FindApplications(Arg.Any<FindAppOptions>()).Returns(expected);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<FindAppCommand>(Arg.Any<FindAppOptions>()).Returns(resolvedCommand);
		FindAppTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		FindAppResponse response = tool.FindApp(new FindAppArgs("dev", "case", null));

		// Assert
		response.Success.Should().BeTrue(
			because: "a successful search should be wrapped in a success envelope");
		response.Error.Should().BeNull(
			because: "successful calls should not carry an error payload");
		response.Applications.Should().BeEquivalentTo(expected,
			because: "the tool should surface the structured applications resolved by the command");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.Environment == "dev" && options.SearchPattern == "case"));
	}

	[Test]
	[Category("Unit")]
	[Description("Wraps an environment-resolution failure in a structured error envelope that carries the actionable reg-web-app fix.")]
	public void FindApp_Should_Return_Error_Envelope_With_Actionable_Hint_When_Environment_Missing() {
		// Arrange
		string actionableMessage = EnvironmentNotFoundError.Build("missing-env", (IEnumerable<string>?)null);
		FindAppCommand resolvedCommand = Substitute.For<FindAppCommand>(
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<FindAppCommand>(Arg.Any<FindAppOptions>())
			.Returns(_ => throw new InvalidOperationException(actionableMessage));
		FindAppTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		FindAppResponse response = tool.FindApp(new FindAppArgs("missing-env"));

		// Assert
		response.Success.Should().BeFalse(
			because: "a resolution failure must be expressed as a structured error envelope, not an exception");
		response.Applications.Should().BeNull(
			because: "error envelopes should not carry a result collection");
		response.Error.Should().Contain("reg-web-app",
			because: "the env-not-found error must include a copy-pasteable reg-web-app fix");
		response.Error.Should().Contain("missing-env",
			because: "the error should name the environment that could not be resolved");
	}
}
