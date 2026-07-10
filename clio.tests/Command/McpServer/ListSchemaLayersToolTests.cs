using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListSchemaLayersToolTests {

	[Test]
	[Category("Unit")]
	public void ListLayers_Should_Resolve_Command_And_Default_ManagerName() {
		ConsoleLogger.Instance.ClearMessages();
		FakeListSchemaLayersCommand defaultCommand = new();
		FakeListSchemaLayersCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaLayersCommand>(Arg.Any<ListSchemaLayersOptions>())
			.Returns(resolvedCommand);
		ListSchemaLayersTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		ListSchemaLayersResponse response = tool.ListLayers(new ListSchemaLayersArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContractPageV2");
		resolvedCommand.CapturedOptions.ManagerName.Should().Be("ClientUnitSchemaManager");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void ListLayers_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeListSchemaLayersCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaLayersCommand>(Arg.Any<ListSchemaLayersOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		ListSchemaLayersTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		ListSchemaLayersResponse response = tool.ListLayers(new ListSchemaLayersArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	// Lesson #2: editable ⇔ non-product maintainer AND InstallType==0.
	[TestCase("Customer", 0, true)]
	[TestCase("Customer", 1, false)]   // installed customer package → read-only
	[TestCase("Partner", 0, true)]
	[TestCase("Creatio", 0, false)]    // product maintainer
	[TestCase("Terrasoft", 0, false)]  // product maintainer
	[TestCase(null, 0, false)]
	[TestCase("", 0, false)]
	public void IsClientEditable_Should_Classify_By_Maintainer_And_InstallType(
		string maintainer, int installType, bool expected) {
		ListSchemaLayersCommand.IsClientEditable(maintainer, installType).Should().Be(expected);
	}

	private sealed class FakeListSchemaLayersCommand : ListSchemaLayersCommand {
		public ListSchemaLayersOptions CapturedOptions { get; private set; }

		public FakeListSchemaLayersCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryListLayers(ListSchemaLayersOptions options, out ListSchemaLayersResponse response) {
			CapturedOptions = options;
			response = new ListSchemaLayersResponse { Success = true, SchemaName = options.SchemaName, Count = 0 };
			return true;
		}
	}
}
