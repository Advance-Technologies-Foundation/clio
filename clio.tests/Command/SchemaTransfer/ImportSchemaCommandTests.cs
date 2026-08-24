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
	private const string ForeignSchemaUId = "0d3f7a6e-1b2c-4d5e-8f90-1a2b3c4d5e6f";

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

	[Test]
	[Category("Unit")]
	[Description("Refuses a REPLACE when the target package owns the name under a different schema uId")]
	public void Execute_Should_Refuse_When_Target_Package_Layer_Is_A_Different_Schema() {
		// Arrange
		// The boxed-layer case: the target package owns this name, but as a different SysSchema row. The
		// platform importer preserves the bundle's uId, so this is not a replacement — it is a second row with
		// the same (name, manager, package) triple, which IU_Name_Manager_Package rejects.
		List<SchemaLayerDto> layers = BuildLayers(TargetPackage);
		layers[0].SchemaUId = ForeignSchemaUId;
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(layers);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(1,
			because: "matching the package alone does not make the layer the same schema as the bundle");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("A dry run reports the same refusal, so the plan never claims a REPLACE that would fail")]
	public void Execute_Should_Refuse_Dry_Run_When_Target_Package_Layer_Is_A_Different_Schema() {
		// Arrange
		List<SchemaLayerDto> layers = BuildLayers(TargetPackage);
		layers[0].SchemaUId = ForeignSchemaUId;
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(layers);
		ImportSchemaOptions options = BuildOptions();
		options.DryRun = true;

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "reporting 'Plan: REPLACE' for an import the platform would reject is what makes a dry run "
				+ "untrustworthy");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a REPLACE when the target package layer belongs to a different schema manager")]
	public void Execute_Should_Refuse_When_Target_Package_Layer_Has_Another_Manager() {
		// Arrange
		List<SchemaLayerDto> layers = BuildLayers(TargetPackage);
		layers[0].ManagerName = "ClientUnitSchemaManager";
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(layers);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(1, because: "a same-named schema under another manager is not the bundle's schema");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Still replaces when the layer reports no uId, because an unknown identity is not a mismatch")]
	public void Execute_Should_Import_When_Target_Package_Layer_Reports_No_UId() {
		// Arrange
		// An older gate, or a layer the gate could not resolve a uId for, must not turn an ordinary update into
		// a refusal — only a uId that is present and different is evidence of another schema.
		List<SchemaLayerDto> layers = BuildLayers(TargetPackage);
		layers[0].SchemaUId = null;
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(layers);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(0, because: "an absent uId is no evidence that the layer is a different schema");
		_schemaTransferClient.Received(1).Import(Payload, TargetPackage);
	}

	[Test]
	[Category("Unit")]
	[Description("Replaces the matching layer when the target package owns the name twice, under two managers")]
	public void Execute_Should_Import_When_One_Of_Several_Target_Package_Layers_Matches() {
		// Arrange
		// The uniqueness constraint is (name, manager, package), so one package can own this name twice. When the
		// bundle carries no manager the gate does not narrow by manager either, so both layers come back and the
		// plan has to be resolved against the one that actually matches, not against whichever is first.
		SchemaBundleDescriptor bundleWithoutManager = new() {
			SchemaName = SchemaName,
			SchemaUId = "8375dacb-4ea5-4103-b07a-d365f8d276f3"
		};
		_schemaBundleStore.Read(Arg.Any<string>())
			.Returns(new SchemaBundle(bundleWithoutManager, Payload));
		List<SchemaLayerDto> layers = BuildLayers(TargetPackage);
		layers.Insert(0, new SchemaLayerDto {
			SchemaName = SchemaName,
			SchemaUId = ForeignSchemaUId,
			ManagerName = "SourceCodeSchemaManager",
			PackageName = TargetPackage
		});
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(layers);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(0,
			because: "one of the layers the package owns is the bundle's own schema, so this is a replacement");
		_schemaTransferClient.Received(1).Import(Payload, TargetPackage);
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses and names every layer when none of the target package's layers is the bundle's schema")]
	public void Execute_Should_Refuse_When_No_Target_Package_Layer_Matches() {
		// Arrange
		List<SchemaLayerDto> layers = BuildLayers(TargetPackage);
		layers[0].SchemaUId = ForeignSchemaUId;
		layers.Add(new SchemaLayerDto {
			SchemaName = SchemaName,
			SchemaUId = "1c9f2b3d-4e5a-6b7c-8d9e-0f1a2b3c4d5e",
			ManagerName = "SourceCodeSchemaManager",
			PackageName = TargetPackage
		});
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(layers);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(1, because: "none of the layers the package owns is the schema in the bundle");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a REPLACE when the bundle carries no uId, so an unverifiable identity is not a match")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void Execute_Should_Refuse_When_The_Bundle_Carries_No_UId(string bundleUId) {
		// Arrange
		// A payload with no `UId` member reads back as a blank SchemaUId, so this is a real bundle shape. The
		// package owns the name, which is what classifies the import as a REPLACE — and a replacement can only
		// be claimed once the identity is confirmed, which a blank uId cannot do.
		GivenBundleWithUId(bundleUId);
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(BuildLayers(TargetPackage));

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(1,
			because: "with no uId the bundle cannot be shown to be the layer it would overwrite, and a blind "
				+ "overwrite is exactly what the identity guard exists to prevent");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses the same case under --dry-run, so the plan never reports an unconfirmed REPLACE")]
	public void Execute_Should_Refuse_A_Dry_Run_When_The_Bundle_Carries_No_UId() {
		// Arrange
		GivenBundleWithUId(null);
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns(BuildLayers(TargetPackage));
		ImportSchemaOptions options = BuildOptions();
		options.DryRun = true;

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a dry run whose plan says REPLACE is the thing the operator trusts before the real run");
		_schemaTransferClient.DidNotReceive().Import(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Still creates from a bundle with no uId when the target package owns nothing of that name")]
	public void Execute_Should_Import_A_Bundle_Without_UId_When_There_Is_Nothing_To_Replace() {
		// Arrange
		// The guard belongs to the REPLACE decision only: with nothing to overwrite there is no identity to
		// confirm, so a uId-less bundle must still be importable.
		GivenBundleWithUId(null);
		_schemaTransferClient.FindLayers(SchemaName, Arg.Any<string>()).Returns([]);

		// Act
		int result = _sut.Execute(BuildOptions());

		// Assert
		result.Should().Be(0, because: "a CREATE overwrites nothing, so there is no identity to verify");
		_schemaTransferClient.Received(1).Import(Payload, TargetPackage);
	}

	private void GivenBundleWithUId(string bundleUId) =>
		_schemaBundleStore.Read(Arg.Any<string>()).Returns(new SchemaBundle(
			new SchemaBundleDescriptor {
				SchemaName = SchemaName,
				SchemaUId = bundleUId,
				ManagerName = "AddonSchemaManager"
			},
			Payload));

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
