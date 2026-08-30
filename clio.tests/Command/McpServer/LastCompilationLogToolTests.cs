using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class LastCompilationLogToolTests {

	[Test]
	[Description("Declares last-compilation-log as a read-only, non-destructive, non-resident MCP tool.")]
	public void GetLastCompilationLog_ShouldExposeLongTailReadOnlyContract(){
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(LastCompilationLogTool)
			.GetMethod(nameof(LastCompilationLogTool.GetLastCompilationLog))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		bool resident = McpCoreToolProfile.IsResident(LastCompilationLogTool.ToolName);

		// Assert
		attribute.Name.Should().Be(LastCompilationLogTool.ToolName,
			because: "the served tool name must remain stable for clio-run callers");
		attribute.ReadOnly.Should().BeTrue(because: "reading the persisted compilation result never mutates Creatio");
		attribute.Destructive.Should().BeFalse(because: "the tool performs only a GET request");
		attribute.Idempotent.Should().BeTrue(because: "repeating a read does not itself change target state");
		resident.Should().BeFalse(because: "last-compilation-log belongs to the long-tail clio-run surface");
	}

	[Test]
	[Description("Maps Creatio errors and warnings into a structured result while preserving compilation outcome.")]
	public void GetLastCompilationLog_ShouldReturnStructuredDiagnostics_WhenEndpointReturnsResult(){
		// Arrange
		const string payload = """
			{"errors":[{"line":4,"column":2,"errorNumber":"CS1002","errorText":"; expected","warning":false,"fileName":"Broken.cs"},{"line":8,"column":5,"errorNumber":"CS0168","errorText":"Variable is never used","warning":true,"fileName":"Warning.cs"}],"buildResult":1,"success":false}
			""";
		(IApplicationClient client, IToolCommandResolver resolver, LastCompilationLogTool tool) = CreateTool(payload);

		// Act
		LastCompilationLogResponse result = tool.GetLastCompilationLog(new LastCompilationLogArgs("dev"));

		// Assert
		result.Success.Should().BeTrue(because: "the endpoint response was retrieved and parsed successfully");
		result.CompilationSucceeded.Should().BeFalse(
			because: "a failed compilation is valid result data, not a tool retrieval failure");
		result.BuildResult.Should().Be(1, because: "the MCP response must preserve Creatio's build-result value");
		result.Diagnostics.Select(diagnostic => diagnostic.Severity).Should().Equal(["error", "warning"],
			because: "agents need to distinguish blocking errors from warnings");
		result.Diagnostics[0].Code.Should().Be("CS1002", because: "compiler codes are actionable diagnostic data");
		resolver.Received(1).Resolve<LastCompilationLogCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		client.Received(1).ExecuteGetRequest(Arg.Is<string>(url =>
			url.EndsWith("/api/ConfigurationStatus/GetLastCompilationResult", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Returns a structured, redacted failure when the requested environment cannot be resolved.")]
	public void GetLastCompilationLog_ShouldReturnFailure_WhenEnvironmentResolutionFails(){
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<LastCompilationLogCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'missing' was not found"));
		LastCompilationLogCommand defaultCommand = CreateCommand(Substitute.For<IApplicationClient>());
		LastCompilationLogTool tool = new(defaultCommand, Substitute.For<ILogger>(), resolver);

		// Act
		LastCompilationLogResponse result = tool.GetLastCompilationLog(new LastCompilationLogArgs("missing"));

		// Assert
		result.Success.Should().BeFalse(because: "resolution failures must not masquerade as compilation results");
		result.Error.Should().Contain("not found",
			because: "the caller needs an actionable explanation for the unregistered environment");
		result.Diagnostics.Should().BeEmpty(because: "no compiler diagnostics exist when retrieval never started");
	}

	[Test]
	[Description("Returns a structured failure when Creatio responds with an error envelope instead of compilation fields.")]
	public void GetLastCompilationLog_ShouldReturnFailure_WhenEndpointPayloadHasUnexpectedShape(){
		// Arrange
		(_, _, LastCompilationLogTool tool) = CreateTool("""{"message":"Functionality is disabled"}""");

		// Act
		LastCompilationLogResponse result = tool.GetLastCompilationLog(new LastCompilationLogArgs("dev"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an endpoint error envelope is a retrieval failure, not a failed compilation result");
		result.Error.Should().Contain("unexpected compilation-result payload",
			because: "the caller should receive an actionable payload-shape diagnosis");
		result.Diagnostics.Should().BeEmpty(because: "an endpoint error envelope contains no compiler diagnostics");
	}

	[Test]
	[Description("Allows environment-name to be omitted so HTTP credential passthrough can provide the target identity.")]
	public void GetLastCompilationLog_ShouldResolveEnvironmentlessOptions_WhenEnvironmentNameIsOmitted(){
		// Arrange
		(_, IToolCommandResolver resolver, LastCompilationLogTool tool) = CreateTool(
			"""{"errors":[],"buildResult":0,"success":true}""");

		// Act
		LastCompilationLogResponse result = tool.GetLastCompilationLog(new LastCompilationLogArgs());

		// Assert
		result.Success.Should().BeTrue(
			because: "the resolver may obtain the target from an authorized credential-passthrough context");
		resolver.Received(1).Resolve<LastCompilationLogCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == null));
	}

	[Test]
	[Description("Rejects a misspelled camelCase environment key instead of silently resolving the default environment.")]
	public void GetLastCompilationLog_ShouldRejectLegacyEnvironmentAlias(){
		// Arrange
		(_, IToolCommandResolver resolver, LastCompilationLogTool tool) = CreateTool(
			"""{"errors":[],"buildResult":0,"success":true}""");
		LastCompilationLogArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("prod")
			}
		};

		// Act
		LastCompilationLogResponse result = tool.GetLastCompilationLog(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "an ignored environment alias could otherwise read a different default target");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the caller needs the exact supported kebab-case replacement");
		resolver.DidNotReceive().Resolve<LastCompilationLogCommand>(Arg.Any<EnvironmentOptions>());
	}

	private static (IApplicationClient Client, IToolCommandResolver Resolver, LastCompilationLogTool Tool)
		CreateTool(string payload) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>()).Returns(payload);
		LastCompilationLogCommand command = CreateCommand(client);
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<LastCompilationLogCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return (client, resolver, new LastCompilationLogTool(command, Substitute.For<ILogger>(), resolver));
	}

	private static LastCompilationLogCommand CreateCommand(IApplicationClient client) {
		return new LastCompilationLogCommand(client, new EnvironmentSettings(), new CompilationLogParser());
	}
}
