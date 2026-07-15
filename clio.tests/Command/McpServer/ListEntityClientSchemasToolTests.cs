using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListEntityClientSchemasToolTests {

	[Test]
	[Category("Unit")]
	public void Resolve_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.EntityName.Should().Be("Contract");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void Resolve_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeListEntityClientSchemasCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[TestCase("PageWithTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithAreaFreedomTemplate", "freedom")]
	[TestCase("FormPageTemplate", "freedom")]
	[TestCase("ListPageV3Template", "freedom")]
	[TestCase("BaseModulePageV2", "classic")]
	[TestCase("BasePageV2", "classic")]
	[TestCase(null, "unknown")]
	[TestCase("", "unknown")]
	public void ClassifyKind_Should_Split_Classic_And_Freedom(string template, string expected) {
		ListEntityClientSchemasCommand.ClassifyKind(template).Should().Be(expected);
	}

	private sealed class FakeListEntityClientSchemasCommand : ListEntityClientSchemasCommand {
		public ListEntityClientSchemasOptions CapturedOptions { get; private set; }

		public FakeListEntityClientSchemasCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryResolve(ListEntityClientSchemasOptions options, out ListEntityClientSchemasResponse response) {
			CapturedOptions = options;
			response = new ListEntityClientSchemasResponse { Success = true, Entity = options.EntityName };
			return true;
		}
	}
}
