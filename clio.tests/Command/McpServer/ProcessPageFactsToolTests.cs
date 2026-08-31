using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Behaviour that lives ONLY in <see cref="ProcessPageFactsTool"/> — the argument gate in front of the command and
/// the redaction around environment resolution. The command itself is covered by
/// <c>ProcessPageFactsCommandTests</c>; the registry entries this tool appears in are completeness oracles, not
/// behaviour tests, so without this fixture the three cases below had no coverage at all.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class ProcessPageFactsToolTests {

	private static ProcessPageFactsTool CreateTool(IToolCommandResolver commandResolver = null) {
		ProcessPageFactsCommand command =
			new(Substitute.For<IProcessPageReader>(), ConsoleLogger.Instance);
		return new ProcessPageFactsTool(command, ConsoleLogger.Instance,
			commandResolver ?? Substitute.For<IToolCommandResolver>());
	}

	private static Dictionary<string, JsonElement> Extension(string key, string value) => new() {
		[key] = JsonSerializer.SerializeToElement(value)
	};

	[Test]
	[Description("A legacy argument spelling is REFUSED with the canonical name, instead of being silently ignored as an unknown extension property — the tool would otherwise report 'schema-name is required' for a caller who did pass the page, under an older name.")]
	public void GetProcessPageFacts_ShouldRejectALegacyArgumentSpelling() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		ProcessPageFactsTool tool = CreateTool();
		ProcessPageFactsArgs args = new() {
			// A legacy spelling AND an unrecognised one: the builder renders the two halves separately, and the
			// accepted vocabulary is offered with the unknown half.
			ExtensionData = new Dictionary<string, JsonElement> {
				["schemaName"] = JsonSerializer.SerializeToElement("UsrRequest_FormPage"),
				["locale"] = JsonSerializer.SerializeToElement("de-DE")
			}
		};

		// Act
		ProcessPageFactsResponse response = tool.GetProcessPageFacts(args);

		// Assert
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schemaName").And.Contain("schema-name",
			because: "the refusal has to name both what was sent and what to send instead");
		response.Error.Should().Contain("locale").And.Contain("Valid: schema-name, culture, environment-name",
			because: "an unrecognised key is answered with the whole accepted vocabulary");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("A blank schema-name is refused before any environment work happens: the page name is the one argument this tool cannot default.")]
	public void GetProcessPageFacts_ShouldRequireASchemaName() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ProcessPageFactsTool tool = CreateTool(commandResolver);

		// Act
		ProcessPageFactsResponse response = tool.GetProcessPageFacts(new ProcessPageFactsArgs { SchemaName = "  " });

		// Assert
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schema-name is required");
		commandResolver.DidNotReceive().Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("An environment-resolution failure is REDACTED before it becomes a tool result: resolution runs on the caller's connection details, and an MCP tool result is transcript the agent keeps. Pinned on the URI, which is what SensitiveErrorTextRedactor actually removes — a secret embedded in free prose is outside that redactor's scope, so this asserts the guarantee that exists rather than one that does not.")]
	public void GetProcessPageFacts_ShouldRedactAnEnvironmentResolutionFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>())
			.Returns(_ => throw new InvalidOperationException(
				"Cannot connect to https://stand.creatio.com as Supervisor with password Supervisor2"));
		ProcessPageFactsTool tool = CreateTool(commandResolver);
		ProcessPageFactsArgs args = new() {
			SchemaName = "UsrRequest_FormPage",
			Uri = "https://stand.creatio.com",
			Login = "Supervisor",
			Password = "Supervisor2"
		};

		// Act
		ProcessPageFactsResponse response = tool.GetProcessPageFacts(args);

		// Assert
		response.Success.Should().BeFalse();
		response.SchemaName.Should().Be("UsrRequest_FormPage",
			because: "a failure envelope still says which page was asked about");
		response.Error.Should().NotContain("https://stand.creatio.com",
			because: "the target host must not survive verbatim into the agent's transcript");
		response.Error.Should().Contain("redacted",
			because: "dropping the Redact call would put the raw resolution error into the tool result");
		ConsoleLogger.Instance.ClearMessages();
	}

}
