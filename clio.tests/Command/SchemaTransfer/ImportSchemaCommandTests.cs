using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.SchemaTransfer;

[TestFixture]
[Property("Module", "Command")]
public class ImportSchemaCommandTests : BaseCommandTests<ImportSchemaOptions> {

	private const string SchemaName = "UsrProbeSchema";
	private const string TargetPackage = "UsrTargetPackage";
	private const string OtherPackage = "UsrOtherPackage";
	private const string BundlePath = "/tmp/bundles/UsrProbeSchema";
	private const string Payload = """{"Name":"UsrProbeSchema"}""";

	private ISchemaTransferClient _schemaTransferClient;
	private ISchemaBundleStore _schemaBundleStore;
	private ImportSchemaCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_schemaTransferClient = Substitute.For<ISchemaTransferClient>();
		_schemaBundleStore = Substitute.For<ISchemaBundleStore>();
		containerBuilder.AddSingleton(_schemaTransferClient);
		containerBuilder.AddSingleton(_schemaBundleStore);
	}

	[SetUp]
	public void SetUp() {
		_sut = Container.GetRequiredService<ImportSchemaCommand>();
		_schemaBundleStore.Read(Arg.Any<string>()).Returns(new SchemaBundle(
			new SchemaBundleDescriptor {
				SchemaName = SchemaName,
				SchemaUId = "8375dacb-4ea5-4103-b07a-d365f8d276f3",
				ManagerName = "AddonSchemaManager"
			},
			Payload));
	}

	[TearDown]
	public void TearDown() {
		_schemaTransferClient.ClearReceivedCalls();
		_schemaBundleStore.ClearReceivedCalls();
	}

	[Test]
	[Category("Unit")]
	[Description("Imports the bundle when the schema does not exist yet on the target")]
	public void Execute_Should_Import_When_Schema_Does_Not_Exist() {
		// Arrange
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns([]);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(0, because: "creating a schema that does not exist yet is unambiguous");
		_schemaTransferClient.Received(1).Import(Payload, TargetPackage);
	}

	[Test]
	[Category("Unit")]
	[Description("Imports the bundle when the schema already exists in the target package, replacing that layer")]
	public void Execute_Should_Import_When_Schema_Exists_In_Target_Package() {
		// Arrange
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>())
			.Returns(BuildLayers(TargetPackage));

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(0, because: "replacing the layer in its own package is the ordinary update path");
		_schemaTransferClient.Received(1).Import(Payload, TargetPackage);
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses, and writes nothing, when the schema name is owned by a different package")]
	public void Execute_Should_Refuse_When_Schema_Owned_By_Another_Package() {
		// Arrange
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>())
			.Returns(BuildLayers(OtherPackage));

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(1,
			because: "creating a second layer is indistinguishable here from the duplicate-key defect");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Imports into a new layer when the caller opted in with --allow-new-layer")]
	public void Execute_Should_Import_New_Layer_When_Allowed() {
		// Arrange
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>())
			.Returns(BuildLayers(OtherPackage));
		ImportSchemaOptions options = BuildOptions();
		options.AllowNewLayer = true;

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "the caller took the decision deliberately");
		_schemaTransferClient.Received(1).Import(Payload, TargetPackage);
	}

	[Test]
	[Category("Unit")]
	[Description("A dry run reports the plan and writes nothing to the environment")]
	public void Execute_Should_Not_Import_On_Dry_Run() {
		// Arrange
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns([]);
		ImportSchemaOptions options = BuildOptions();
		options.DryRun = true;

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a dry run is a successful inspection, not a failure");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("A dry run is still refused when the import it describes would be refused")]
	public void Execute_Should_Refuse_Dry_Run_When_Schema_Owned_By_Another_Package() {
		// Arrange
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>())
			.Returns(BuildLayers(OtherPackage));
		ImportSchemaOptions options = BuildOptions();
		options.DryRun = true;

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a dry run that reported success for an import that would fail would be worse than useless");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails when the bundle carries no schema name to resolve the plan for")]
	public void Execute_Should_Fail_When_Bundle_Names_No_Schema() {
		// Arrange
		_schemaBundleStore.Read(Arg.Any<string>())
			.Returns(new SchemaBundle(new SchemaBundleDescriptor(), Payload));

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(1, because: "without a schema name the create-versus-replace plan cannot be resolved");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	private static ImportSchemaOptions BuildOptions() =>
		new() {
			Path = BundlePath,
			PackageName = TargetPackage
		};

	private static List<SchemaLayerDto> BuildLayers(string packageName) =>
		[
			new() {
				SchemaName = SchemaName,
				SchemaUId = "8375dacb-4ea5-4103-b07a-d365f8d276f3",
				ManagerName = "AddonSchemaManager",
				PackageName = packageName
			}
		];
}
