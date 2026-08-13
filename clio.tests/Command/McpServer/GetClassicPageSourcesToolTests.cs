using System;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class GetClassicPageSourcesToolTests {

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	[Test]
	[Category("Unit")]
	[Description("GetPageSources maps args to options and executes the command resolved for the requested environment.")]
	public void GetPageSources_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		FakeGetClassicPageSourcesCommand defaultCommand = new();
		FakeGetClassicPageSourcesCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicPageSourcesCommand>(Arg.Any<GetClassicPageSourcesOptions>())
			.Returns(resolvedCommand);
		GetClassicPageSourcesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicPageSourcesResponse response = tool.GetPageSources(
			new GetClassicPageSourcesArgs("ContactPageV2", "Contact") {
				OutputFile = "/tmp/sources.json", EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command succeeded");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command must receive the mapped options");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContactPageV2", because: "schema-name maps through");
		resolvedCommand.CapturedOptions.Entity.Should().Be("Contact", because: "entity maps through");
		resolvedCommand.CapturedOptions.OutputFile.Should().Be("/tmp/sources.json", because: "output-file maps through");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name maps to the options environment");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run; only the env-resolved one does");
	}

	[Test]
	[Category("Unit")]
	[Description("GetPageSources returns a failed response (not an exception) when command resolution fails.")]
	public void GetPageSources_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		FakeGetClassicPageSourcesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicPageSourcesCommand>(Arg.Any<GetClassicPageSourcesOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		GetClassicPageSourcesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicPageSourcesResponse response = tool.GetPageSources(
			new GetClassicPageSourcesArgs("ContactPageV2") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must surface as a failed response");
		response.Error.Should().Contain("boom", because: "the underlying error message must be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("GetPageSources redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void GetPageSources_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		FakeGetClassicPageSourcesCommand defaultCommand = new();
		FakeGetClassicPageSourcesCommand resolvedCommand = new() {
			ResponseToReturn = new GetClassicPageSourcesResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicPageSourcesCommand>(Arg.Any<GetClassicPageSourcesOptions>())
			.Returns(resolvedCommand);
		GetClassicPageSourcesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicPageSourcesResponse response = tool.GetPageSources(
			new GetClassicPageSourcesArgs("ContactPageV2") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("GetPageSources redacts a sensitive URI/host carried in the response warnings, not just in the error, so the section-metadata warning cannot leak backend detail into the MCP transcript.")]
	public void GetPageSources_Should_Redact_Sensitive_Text_In_Warnings() {
		// Arrange — a successful collection whose warning interpolates the raw DataService failure text
		FakeGetClassicPageSourcesCommand defaultCommand = new();
		FakeGetClassicPageSourcesCommand resolvedCommand = new() {
			ResponseToReturn = new GetClassicPageSourcesResponse {
				Success = true,
				Warnings = [
					"Section metadata lookup failed (POST https://secret-host.example.com/0/DataService failed); " +
					"fell back to name conventions."
				]
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicPageSourcesCommand>(Arg.Any<GetClassicPageSourcesOptions>())
			.Returns(resolvedCommand);
		GetClassicPageSourcesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicPageSourcesResponse response = tool.GetPageSources(
			new GetClassicPageSourcesArgs("ContactPageV2") { EnvironmentName = "dev" });

		// Assert
		response.Warnings.Should().ContainSingle(because: "the single warning is carried through to the caller")
			.Which.Should().NotContain("secret-host.example.com",
				because: "warnings are a second error channel and must be redacted like Error is");
		response.Warnings[0].Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the same stable placeholder used on the error path");
		response.Warnings[0].Should().Contain("fell back to name conventions",
			because: "redaction must scrub the host, not destroy the actionable part of the warning");
	}

	private sealed class FakeGetClassicPageSourcesCommand : GetClassicPageSourcesCommand {
		public GetClassicPageSourcesOptions CapturedOptions { get; private set; }
		public GetClassicPageSourcesResponse ResponseToReturn { get; init; }

		public FakeGetClassicPageSourcesCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IRemoteEntitySchemaColumnManager>(),
				Substitute.For<IPageDesignerHierarchyClient>(),
				Substitute.For<IClassicSectionSchemaResolver>(),
				Substitute.For<IClassicDetailEditPageResolver>(),
				Substitute.For<System.IO.Abstractions.IFileSystem>(),
				ConsoleLogger.Instance) {
		}

		public override bool TryAssemblePageSources(
			GetClassicPageSourcesOptions options, out GetClassicPageSourcesResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn
				?? new GetClassicPageSourcesResponse { Success = true, SchemaName = options.SchemaName };
			return response.Success;
		}
	}
}
