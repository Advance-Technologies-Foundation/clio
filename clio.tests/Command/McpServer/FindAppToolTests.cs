using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

	[Test]
	[Category("Unit")]
	[Description("Recovers a guessed 'name' overflow key into search-pattern so the substring filter reaches the command instead of being silently dropped.")]
	public void FindApp_Should_Recover_Name_Alias_Into_SearchPattern_When_Bound_Pattern_Missing() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([]);
		FindAppArgs args = new("dev") {
			ExtensionData = Overflow(("name", "case"))
		};

		// Act
		FindAppResponse response = tool.FindApp(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a recognized 'name' alias should be recovered rather than rejected");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.SearchPattern == "case" && options.Environment == "dev" && options.Code == null));
	}

	[Test]
	[Category("Unit")]
	[Description("Recovers a guessed 'query' overflow key into search-pattern so the natural query spelling reaches the command.")]
	public void FindApp_Should_Recover_Query_Alias_Into_SearchPattern_When_Bound_Pattern_Missing() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([]);
		FindAppArgs args = new("dev") {
			ExtensionData = Overflow(("query", "billing"))
		};

		// Act
		FindAppResponse response = tool.FindApp(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a recognized 'query' alias should be recovered rather than rejected");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.SearchPattern == "billing" && options.Code == null));
	}

	[Test]
	[Category("Unit")]
	[Description("Recovers a guessed 'app-code' overflow key into the exact code argument so an agent's natural code spelling reaches the command.")]
	public void FindApp_Should_Recover_AppCode_Alias_Into_Code_When_Bound_Code_Missing() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([]);
		FindAppArgs args = new("dev") {
			ExtensionData = Overflow(("app-code", "CrtCaseManagementApp"))
		};

		// Act
		FindAppResponse response = tool.FindApp(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a recognized 'app-code' alias should be recovered rather than rejected");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.Code == "CrtCaseManagementApp" && options.SearchPattern == null));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error naming a genuinely-unknown overflow key and the correct search-pattern argument instead of silently returning an unfiltered list.")]
	public void FindApp_Should_Return_Structured_Error_When_Overflow_Key_Is_Unknown() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([
			new AppSearchResult("id", "AnyApp", "Any App", "1.0.0", null, [])
		]);
		FindAppArgs args = new("dev") {
			ExtensionData = Overflow(("bogus", "value"))
		};

		// Act
		FindAppResponse response = tool.FindApp(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "an unrecognized filter key must fail loudly rather than be silently dropped");
		response.Applications.Should().BeNull(
			because: "an unknown key must not fall through to an unfiltered application list");
		response.Error.Should().Contain("bogus",
			because: "the error must name the rejected key so the agent can correct it");
		response.Error.Should().Contain("search-pattern",
			because: "the error must point the agent at the correct substring-filter argument");
		commandResolver.DidNotReceive().Resolve<FindAppCommand>(Arg.Any<FindAppOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps a valid bound search-pattern working unchanged when no overflow keys are present (regression for the recovery path).")]
	public void FindApp_Should_Use_Bound_SearchPattern_When_No_Overflow_Present() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([]);

		// Act
		FindAppResponse response = tool.FindApp(new FindAppArgs("dev", "case", null));

		// Assert
		response.Success.Should().BeTrue(
			because: "a legitimate bound search-pattern must continue to succeed");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.SearchPattern == "case" && options.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Invokes the command with null pattern and code when both filters are omitted, returning all apps by design rather than erroring.")]
	public void FindApp_Should_Invoke_Command_With_Null_Filters_When_Both_Omitted() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([]);

		// Act
		FindAppResponse response = tool.FindApp(new FindAppArgs("dev"));

		// Assert
		response.Success.Should().BeTrue(
			because: "omitting both filters is a legitimate enumerate-all request, not an error");
		response.Error.Should().BeNull(
			because: "an absent filter must never produce an error envelope");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.SearchPattern == null && options.Code == null));
	}

	[Test]
	[Category("Unit")]
	[Description("Does not misclassify the bound environment-name argument as an unknown overflow key when no other keys are present.")]
	public void FindApp_Should_Not_Treat_EnvironmentName_As_Unknown_Filter_Key() {
		// Arrange
		(FindAppTool tool, IToolCommandResolver commandResolver) = CreateTool([]);

		// Act
		FindAppResponse response = tool.FindApp(new FindAppArgs("dev"));

		// Assert
		response.Success.Should().BeTrue(
			because: "environment-name binds to a real argument and must never be reported as an unknown key");
		response.Error.Should().BeNull(
			because: "a request carrying only the required environment-name is valid");
		commandResolver.Received(1).Resolve<FindAppCommand>(Arg.Is<FindAppOptions>(
			options => options.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts the target host/URI out of a raw IApplicationClient exception message before it reaches the typed FindAppResponse.Error envelope, since that POCO error path bypasses both the throw-path filter and the IsError dispatch audit.")]
	public void FindApp_Should_Redact_Host_And_Uri_In_Error_Envelope_When_Command_Throws_With_Connection_Failure() {
		// Arrange
		const string sensitiveHost = "http://secret-host:88/0/odata";
		string rawMessage = $"Failed to connect to {sensitiveHost}";
		FindAppCommand resolvedCommand = Substitute.For<FindAppCommand>(
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		resolvedCommand.FindApplications(Arg.Any<FindAppOptions>())
			.Returns(_ => throw new InvalidOperationException(rawMessage));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<FindAppCommand>(Arg.Any<FindAppOptions>()).Returns(resolvedCommand);
		FindAppTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		FindAppResponse response = tool.FindApp(new FindAppArgs("dev"));

		// Assert
		response.Success.Should().BeFalse(
			because: "a connection failure must be expressed as a structured error envelope, not an exception");
		response.Error.Should().NotContain(sensitiveHost,
			because: "the raw target host/URI from the IApplicationClient failure must never reach the transcript");
		response.Error.Should().NotContain("secret-host",
			because: "no fragment of the redacted host should survive in the surfaced error");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the redactor replaces the URI with a stable placeholder rather than dropping the whole message");
	}

	private static (FindAppTool Tool, IToolCommandResolver Resolver) CreateTool(IReadOnlyList<AppSearchResult> results) {
		FindAppCommand resolvedCommand = Substitute.For<FindAppCommand>(
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		resolvedCommand.FindApplications(Arg.Any<FindAppOptions>()).Returns(results);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<FindAppCommand>(Arg.Any<FindAppOptions>()).Returns(resolvedCommand);
		FindAppTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);
		return (tool, commandResolver);
	}

	private static Dictionary<string, JsonElement> Overflow(params (string Key, string Value)[] entries) {
		Dictionary<string, JsonElement> overflow = new(StringComparer.Ordinal);
		foreach ((string key, string value) in entries) {
			overflow[key] = JsonSerializer.SerializeToElement(value);
		}
		return overflow;
	}
}
