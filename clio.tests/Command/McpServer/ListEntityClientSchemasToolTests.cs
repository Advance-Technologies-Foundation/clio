using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class ListEntityClientSchemasToolTests {

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve resolves the command for the requested environment and maps entity-name into options.")]
	public void Resolve_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved fake command returns a successful response");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the environment-scoped command must execute");
		resolvedCommand.CapturedOptions.EntityName.Should().Be("Contract", because: "entity-name is the required lookup key");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name drives command resolution");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run for environment-scoped calls");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a redacted error response when environment-scoped command resolution fails.")]
	public void Resolve_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "resolver failures must be returned as typed tool failures");
		response.Error.Should().Contain("boom", because: "the caller needs the resolver failure reason");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a typed failure when the MCP request explicitly passes null args.")]
	public void Resolve_Should_Return_Error_When_Args_Are_Null() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(null);

		// Assert
		response.Success.Should().BeFalse(because: "args:null is invalid but should not escape as an NRE");
		response.Error.Should().Contain("args", because: "the failure should name the missing argument object");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void Resolve_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new() {
			ResponseToReturn = new ListEntityClientSchemasResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve redacts a sensitive URI/host carried in the response warnings, not just the error, so a warning cannot leak backend detail into the MCP transcript — parity with GetClassicPageSourcesTool.")]
	public void Resolve_Should_Redact_Sensitive_Text_In_Warnings() {
		// Arrange — a successful resolve whose warning carries a raw DataService failure host
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new() {
			ResponseToReturn = new ListEntityClientSchemasResponse {
				Success = true,
				Warnings = [
					"Section lookup failed (POST https://secret-host.example.com/0/DataService failed); results may be partial."
				]
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Warnings.Should().ContainSingle(because: "the single warning is carried through to the caller")
			.Which.Should().NotContain("secret-host.example.com",
				because: "warnings are a second error channel and must be redacted like Error is");
		response.Warnings[0].Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the same stable placeholder used on the error path");
		response.Warnings[0].Should().Contain("results may be partial",
			because: "redaction scrubs the host, not the actionable part of the warning");
	}

	[Test]
	[Category("Unit")]
	[Description("ClassifyKind recognizes known Freedom templates, known Classic templates, and leaves unknown templates undecided.")]
	[TestCase("PageWithTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithAreaFreedomTemplate", "freedom")]
	[TestCase("PageWithRightAreaAndTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithTopAreaAndTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithTabsAndProgressBarTemplate", "freedom")]
	[TestCase("BlankPageTemplate", "freedom")]
	[TestCase("BaseHomePage", "freedom")]
	[TestCase("BaseDashboardTemplate", "freedom")]
	[TestCase("BaseSidebarTemplate", "freedom")]
	[TestCase("BaseMiniPageTemplate", "freedom")]
	[TestCase("FormPageTemplate", "freedom")]
	[TestCase("ListPageV3Template", "freedom")]
	[TestCase("ListPageV2Template", "freedom")]
	[TestCase("BaseModulePageV2", "classic")]
	[TestCase("BasePageV2", "classic")]
	[TestCase("NotActuallyFreedomClassicTemplate", "unknown")]
	[TestCase("CustomTemplate", "unknown")]
	[TestCase(null, "unknown")]
	[TestCase("", "unknown")]
	public void ClassifyKind_Should_Split_Classic_And_Freedom(string template, string expected) {
		// Arrange / Act
		string actual = ListEntityClientSchemasCommand.ClassifyKind(template);

		// Assert
		actual.Should().Be(expected, because: "migration routing must not guess classic/freedom for unknown templates");
	}

	private sealed class FakeListEntityClientSchemasCommand : ListEntityClientSchemasCommand {
		public ListEntityClientSchemasOptions CapturedOptions { get; private set; }
		public ListEntityClientSchemasResponse ResponseToReturn { get; init; }

		public FakeListEntityClientSchemasCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance,
				Substitute.For<Clio.Common.EntitySchema.IRuntimeEntitySchemaReader>(),
				Substitute.For<Clio.Command.EntitySchemaDesigner.ILookupDefaultDisplayValueResolver>()) {
		}

		public override bool TryResolve(ListEntityClientSchemasOptions options, out ListEntityClientSchemasResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn ?? new ListEntityClientSchemasResponse { Success = true, Entity = options.EntityName };
			return response.Success;
		}
	}
}
