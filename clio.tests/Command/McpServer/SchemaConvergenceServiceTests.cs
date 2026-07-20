using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SchemaConvergenceServiceTests {

	private const string TargetPackage = "UsrPkg";
	private const string SchemaName = "UsrTodoStatus";

	[Test]
	[Category("Unit")]
	[Description("Classifies an absent schema as Create with an empty column delta and no error.")]
	public void Classify_ShouldReturnCreateOutcome_WhenSchemaIsAbsent() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FindEntitySchemaCommand find = FakeFind(resolver, []);
		GetEntitySchemaPropertiesCommand properties = FakeProperties(resolver, Properties());
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrExtra", "Text"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Create,
			because: "an absent schema must be created");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "the create command applies the requested columns inline, so no add delta is planned");
		plan.ColumnsToModify.Should().BeEmpty(
			because: "an absent schema has no columns to modify");
		plan.Error.Should().BeNull(
			because: "a create outcome is not an error");
		properties.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a same-package schema missing a requested column as Reconcile carrying only the missing column in ColumnsToAdd.")]
	public void Classify_ShouldReturnReconcileWithColumnsToAdd_WhenSchemaExistsInTargetPackageWithSubset() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		FakeProperties(resolver, Properties(Column("UsrExisting", "Text")));
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrExtra", "Text"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Reconcile,
			because: "an existing same-package schema missing a requested column must be reconciled");
		plan.ColumnsToAdd.Should().ContainSingle(column => column.ResolveName() == "UsrExtra",
			because: "only the single missing column belongs to the add delta");
		plan.ColumnsToModify.Should().BeEmpty(
			because: "no requested column differs in shape");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a same-package schema that already has every requested column as AlreadySatisfied with no delta.")]
	public void Classify_ShouldReturnAlreadySatisfied_WhenSchemaExistsInTargetPackageWithAllColumns() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		FakeProperties(resolver, Properties(Column("UsrExtra", "Text")));
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrExtra", "Text"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.AlreadySatisfied,
			because: "a schema that already contains every requested column requires no mutation");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "nothing is missing");
		plan.ColumnsToModify.Should().BeEmpty(
			because: "nothing differs");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a same-named schema in a DIFFERENT package as a Collision carrying the owning package and a user-friendly error.")]
	public void Classify_ShouldReturnCollision_WhenSchemaExistsInDifferentPackage() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, "OtherPackage", "Customer", "BaseLookup")]);
		FakeProperties(resolver, Properties());
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrExtra", "Text"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Collision,
			because: "a same-named schema in a different package is a durable cross-package collision");
		plan.CollisionPackageName.Should().Be("OtherPackage",
			because: "the owning package must be machine-readable");
		plan.Error.Should().StartWith("Error:",
			because: "the collision message must be a user-friendly Error: {message} string");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a same-package schema whose parent is incompatible with the requested lookup as a Collision, not a reconcile.")]
	public void Classify_ShouldReturnCollision_WhenSameNameSchemaInTargetPackageHasIncompatibleParent() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseEntity")]);
		FakeProperties(resolver, Properties());
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrExtra", "Text"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Collision,
			because: "a same-name, wrong-kind schema in the target package must fail explicitly, not be reconciled into a lookup");
		plan.Error.Should().Contain("BaseEntity",
			because: "the error must name the incompatible existing parent so the caller understands the kind mismatch");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "no lookup columns may be planned against a wrong-kind schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a requested column present with a different type to ColumnsToModify and classifies the schema as Reconcile, not Collision.")]
	public void Classify_ShouldSurfaceColumnAsModify_WhenColumnTypeDiffersInTargetPackage() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		FakeProperties(resolver, Properties(Column("UsrExtra", "Text")));
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrExtra", "Integer"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Reconcile,
			because: "a per-column type difference is a reconcilable modify, not a whole-schema collision");
		plan.ColumnsToModify.Should().ContainSingle(operation => operation.ResolveColumnName() == "UsrExtra",
			because: "the differing column must be surfaced to the modify delta for the Story-2 write path");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "the column already exists, so it is not an add");
	}

	[Test]
	[Category("Unit")]
	[Description("Reads existence exactly once and never reads column detail on the create-only path.")]
	public void Classify_ShouldReadSchemaExactlyOnce_WhenSchemaIsAbsent() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FindEntitySchemaCommand find = FakeFind(resolver, []);
		GetEntitySchemaPropertiesCommand properties = FakeProperties(resolver, Properties());
		SchemaConvergenceService service = new(resolver);

		// Act
		service.Classify(LookupTarget(RequestedColumn("UsrExtra", "Text")));

		// Assert
		find.Received(1).FindSchemas(Arg.Any<FindEntitySchemaOptions>());
		properties.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reads existence and column detail exactly once each on the reconcile path (2 server-side reads, per the round-trip budget).")]
	public void Classify_ShouldReadSchemaTwice_WhenSchemaExistsInTargetPackage() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FindEntitySchemaCommand find = FakeFind(resolver,
			[new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		GetEntitySchemaPropertiesCommand properties = FakeProperties(resolver, Properties(Column("UsrExisting", "Text")));
		SchemaConvergenceService service = new(resolver);

		// Act
		service.Classify(LookupTarget(RequestedColumn("UsrExtra", "Text")));

		// Assert
		find.Received(1).FindSchemas(Arg.Any<FindEntitySchemaOptions>());
		properties.Received(1).GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a create-entity replacement (extend-parent=true) as Create when only a different-package base row exists, instead of a cross-package collision.")]
	public void Classify_ShouldReturnCreate_WhenExtendParentReplacementDoesNotExistInTargetPackage() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		// Only the base schema exists, in a lower package; the replacement does not exist in the target package yet.
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, "Base", "Customer", "BaseEntity")]);
		GetEntitySchemaPropertiesCommand properties = FakeProperties(resolver, Properties());
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(ReplacementEntityTarget(RequestedColumn("UsrExtra", "Text")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Create,
			because: "a replacement schema is a same-name schema to be created in the target package shadowing the base — not a collision");
		plan.Error.Should().BeNull(
			because: "creating a replacement is not an error");
		properties.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a create-entity replacement as Reconcile on replay when the replacement already exists in the target package, ignoring the different-package base row and the parent gate.")]
	public void Classify_ShouldReconcile_WhenExtendParentReplacementExistsInTargetPackage() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		// Both the base row (lower package) and the already-created replacement (target package) exist.
		FakeFind(resolver, [
			new EntitySchemaSearchResult(SchemaName, "Base", "Customer", "BaseEntity"),
			new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "Contact")
		]);
		FakeProperties(resolver, Properties(Column("UsrExisting", "Text")));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(ReplacementEntityTarget(RequestedColumn("UsrExtra", "Text")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Reconcile,
			because: "once the replacement exists in the target package a replay must reconcile the missing column, not collide");
		plan.ColumnsToAdd.Should().ContainSingle(column => column.ResolveName() == "UsrExtra",
			because: "only the missing column belongs to the replacement's add delta");
	}

	[Test]
	[Category("Unit")]
	[Description("Prefers the target-package row over a different-package row of the same name so a reconcilable schema is not misclassified as a cross-package collision.")]
	public void Classify_ShouldPreferTargetPackageRow_WhenFindSchemasReturnsMultipleRows() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		// Same name exists in another package AND in the target package; the target-package row must win.
		FakeFind(resolver, [
			new EntitySchemaSearchResult(SchemaName, "OtherPackage", "Customer", "BaseLookup"),
			new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")
		]);
		FakeProperties(resolver, Properties(Column("UsrExisting", "Text")));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(LookupTarget(RequestedColumn("UsrExtra", "Text")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Reconcile,
			because: "the caller's schema already exists in the target package, so it must be reconciled, not treated as a cross-package collision");
		plan.ColumnsToAdd.Should().ContainSingle(column => column.ResolveName() == "UsrExtra",
			because: "the missing column must be added to the target-package schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a same-package column whose requested type token (phoneNumber) matches the server's divergent raw-ordinal friendly read-back ('42') as AlreadySatisfied, not Reconcile (ordinal-normalized comparison guards replay idempotency).")]
	public void Classify_ShouldReturnAlreadySatisfied_WhenColumnTypeTokenMatchesFriendlyReadbackName() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		// phoneNumber (ordinal 42) is read back with the raw-ordinal friendly name "42".
		FakeProperties(resolver, Properties(Column("UsrPhone", "42")));
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = LookupTarget(RequestedColumn("UsrPhone", "phoneNumber"));

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.AlreadySatisfied,
			because: "phoneNumber and its '42' read-back denote the same DataValueType, so nothing must be reconciled");
		plan.ColumnsToModify.Should().BeEmpty(
			because: "a byte-different but type-equivalent read-back must not be surfaced as a spurious modify");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "the column already exists");
	}

	[Test]
	[Category("Unit")]
	[Description("Reads the current columns of a schema exactly once and returns them keyed by name for the update-entity per-column reconcile.")]
	public void ReadColumns_ShouldReturnColumnsKeyedByName_WhenSchemaHasColumns() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		GetEntitySchemaPropertiesCommand properties = FakeProperties(resolver,
			Properties(Column("UsrExisting", "Text"), Column("UsrScore", "Integer")));
		SchemaConvergenceService service = new(resolver);

		// Act
		IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> columns = service.ReadColumns("dev", SchemaName);

		// Assert
		columns.Should().HaveCount(2,
			because: "every column from the schema read must be surfaced");
		columns.Should().ContainKey("usrscore",
			because: "the column map must be case-insensitive so the reconcile matches regardless of casing");
		columns["UsrScore"].Type.Should().Be("Integer",
			because: "the column type must be preserved so the reconcile can detect a differing type");
		properties.Received(1).GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty map when the schema read reports no columns so the reconcile issues every requested add.")]
	public void ReadColumns_ShouldReturnEmptyMap_WhenSchemaHasNoColumns() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeProperties(resolver, Properties());
		SchemaConvergenceService service = new(resolver);

		// Act
		IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> columns = service.ReadColumns("dev", SchemaName);

		// Assert
		columns.Should().BeEmpty(
			because: "a schema with no columns must yield an empty reconcile baseline");
	}

	private static SchemaConvergenceTarget LookupTarget(params CreateEntitySchemaColumnArgs[] requestedColumns) {
		return new SchemaConvergenceTarget(
			"dev", TargetPackage, SchemaName, "BaseLookup", IsLookup: true, ExtendParent: false, requestedColumns);
	}

	private static SchemaConvergenceTarget ReplacementEntityTarget(params CreateEntitySchemaColumnArgs[] requestedColumns) {
		// A create-entity replacement schema (extend-parent=true) that shadows a same-name base schema.
		return new SchemaConvergenceTarget(
			"dev", TargetPackage, SchemaName, "Contact", IsLookup: false, ExtendParent: true, requestedColumns);
	}

	private static CreateEntitySchemaColumnArgs RequestedColumn(string name, string type) {
		return new CreateEntitySchemaColumnArgs(name, type);
	}

	private static FindEntitySchemaCommand FakeFind(
		IToolCommandResolver resolver, IReadOnlyList<EntitySchemaSearchResult> results) {
		FindEntitySchemaCommand find = Substitute.For<FindEntitySchemaCommand>(
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		find.FindSchemas(Arg.Any<FindEntitySchemaOptions>()).Returns(results);
		resolver.Resolve<FindEntitySchemaCommand>(Arg.Any<EnvironmentOptions>()).Returns(find);
		return find;
	}

	private static GetEntitySchemaPropertiesCommand FakeProperties(
		IToolCommandResolver resolver, EntitySchemaPropertiesInfo properties) {
		GetEntitySchemaPropertiesCommand command = Substitute.For<GetEntitySchemaPropertiesCommand>(
			Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>());
		command.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(properties);
		resolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return command;
	}

	private static EntitySchemaPropertiesInfo Properties(params EntitySchemaPropertyColumnInfo[] columns) {
		return new EntitySchemaPropertiesInfo(
			Name: SchemaName,
			Title: SchemaName,
			Description: null,
			PackageName: TargetPackage,
			ParentSchemaName: "BaseLookup",
			ExtendParent: false,
			PrimaryColumnName: "Id",
			PrimaryDisplayColumnName: "Name",
			OwnColumnCount: columns.Length,
			InheritedColumnCount: 0,
			IndexesCount: 0,
			TrackChangesInDb: false,
			DbView: false,
			SspAvailable: false,
			Virtual: false,
			UseRecordDeactivation: false,
			ShowInAdvancedMode: false,
			AdministratedByOperations: false,
			AdministratedByColumns: false,
			AdministratedByRecords: false,
			UseDenyRecordRights: false,
			UseLiveEditing: false,
			Columns: columns);
	}

	private static EntitySchemaPropertyColumnInfo Column(string name, string type) {
		return new EntitySchemaPropertyColumnInfo(
			Name: name,
			UId: Guid.NewGuid(),
			Source: "own",
			Title: name,
			Description: null,
			Type: type,
			Required: false,
			Indexed: false,
			ReferenceSchemaName: null);
	}
}
