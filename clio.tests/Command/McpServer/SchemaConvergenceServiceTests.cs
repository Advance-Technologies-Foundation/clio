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

	[TestCase(true, false, SchemaConvergenceOutcome.Collision)]
	[TestCase(false, true, SchemaConvergenceOutcome.Collision)]
	[TestCase(true, true, SchemaConvergenceOutcome.AlreadySatisfied)]
	[TestCase(false, false, SchemaConvergenceOutcome.AlreadySatisfied)]
	[Category("Unit")]
	[Description("Checks the requested DB-view kind before allowing sync to reconcile an existing schema.")]
	public void Classify_ShouldCheckDbViewKind_WhenExplicitlyRequested(bool requested, bool existing,
		SchemaConvergenceOutcome expected) {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		FakeProperties(resolver, Properties() with { DbView = existing });
		IServiceCollection services = new ServiceCollection();
		services.AddSingleton(resolver);
		services.AddTransient<ISchemaConvergenceService, SchemaConvergenceService>();
		using ServiceProvider provider = services.BuildServiceProvider();
		ISchemaConvergenceService service = provider.GetRequiredService<ISchemaConvergenceService>();
		// Act
		SchemaConvergencePlan plan = service.Classify(LookupTarget() with { IsDBView = requested });
		// Assert
		plan.Outcome.Should().Be(expected, because: "sync must not silently ignore or change an existing storage kind");
	}

	[TestCase(null)]
	[TestCase("BaseEntity")]
	[Category("Unit")]
	[Description("Refuses to reconcile a replacement request onto an ordinary schema already in the target package.")]
	public void Classify_ShouldReturnCollision_WhenReplacementTargetIsAnOrdinarySchema(string? parentName) {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", parentName)]);
		GetEntitySchemaPropertiesCommand properties = FakeProperties(resolver, Properties());
		IServiceCollection services = new ServiceCollection();
		services.AddSingleton(resolver);
		services.AddTransient<ISchemaConvergenceService, SchemaConvergenceService>();
		using ServiceProvider provider = services.BuildServiceProvider();
		ISchemaConvergenceService service = provider.GetRequiredService<ISchemaConvergenceService>();
		// Act
		SchemaConvergencePlan plan = service.Classify(ReplacementEntityTarget(RequestedColumn("UsrExtra", "Text")));
		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Collision, because: "an ordinary target row cannot stand in for a replacement");
		plan.ColumnsToAdd.Should().BeEmpty(because: "the original schema must not receive replacement columns");
		properties.ReceivedCalls().Should().BeEmpty(because: "collision detection must precede reconciliation reads");
	}

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
	[Description("Classifies a same-name replacement as Reconcile on replay when it already exists in the target package.")]
	public void Classify_ShouldReconcile_WhenExtendParentReplacementExistsInTargetPackage() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		// Both the base row (lower package) and the already-created replacement (target package) exist.
		FakeFind(resolver, [
			new EntitySchemaSearchResult(SchemaName, "Base", "Customer", "BaseEntity"),
			new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", SchemaName)
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

	[Test]
	[Category("Unit")]
	[Description("Skips a null-named column when reading the current column set so a malformed read model does not throw on the dictionary key, and still surfaces the well-formed columns.")]
	public void ReadColumns_ShouldSkipNullNamedColumn_WhenReadModelContainsNullName() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeProperties(resolver, Properties(Column(null!, "Text"), Column("UsrScore", "Integer")));
		SchemaConvergenceService service = new(resolver);

		// Act
		IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> columns = service.ReadColumns("dev", SchemaName);

		// Assert
		columns.Should().HaveCount(1,
			because: "the null-named column must be skipped instead of throwing on the dictionary key");
		columns.Should().ContainKey("UsrScore",
			because: "the well-formed columns must still be surfaced after the null-named one is skipped");
	}

	[Test]
	[Category("Unit")]
	[Description("Reconciles a caller target package with trailing whitespace against the same server package instead of raising a spurious cross-package collision when a different-package row is also present.")]
	public void Classify_ShouldReconcile_WhenTargetPackageHasTrailingWhitespaceAndDifferentPackageRowExists() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		// A same-name row exists both in another package AND in the (real) target package; a caller package
		// with trailing whitespace must still resolve to the target-package row, not fall through to the other.
		FakeFind(resolver, [
			new EntitySchemaSearchResult(SchemaName, "OtherPackage", "Customer", "BaseLookup"),
			new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")
		]);
		FakeProperties(resolver, Properties(Column("UsrExisting", "Text")));
		SchemaConvergenceService service = new(resolver);
		SchemaConvergenceTarget target = new(
			"dev", TargetPackage + " ", SchemaName, "BaseLookup", IsLookup: true, ExtendParent: false,
			[RequestedColumn("UsrExtra", "Text")]);

		// Act
		SchemaConvergencePlan plan = service.Classify(target);

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Reconcile,
			because: "a trailing space on the requested package must be trimmed for comparison so the target-package row still wins over the different-package row");
		plan.CollisionPackageName.Should().BeNull(
			because: "trimming the caller package prevents a spurious cross-package collision");
		plan.ColumnsToAdd.Should().ContainSingle(column => column.ResolveName() == "UsrExtra",
			because: "only the missing column belongs to the reconcile add delta");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies an override item whose reference matches the stored reference of the inherited lookup as AlreadySatisfied.")]
	public void Classify_ShouldReturnAlreadySatisfied_WhenOverrideReferenceMatchesInheritedReference() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Custom", "BaseItemInFolder")]);
		FakeProperties(resolver, Properties(InheritedLookup("Folder", "ContactFolder")));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(FolderItemTarget(OverrideColumn("Folder", "contactfolder")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.AlreadySatisfied,
			because: "the inherited lookup already references the requested schema (compared case-insensitively)");
		plan.ColumnsToModify.Should().BeEmpty(
			because: "a matching reference needs no redirect");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "an override never adds a column");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies an override item whose reference differs from the stored reference of the inherited lookup as Reconcile with a modify that carries only the column name and reference-schema-name.")]
	public void Classify_ShouldReconcileWithReferenceModify_WhenOverrideReferenceDiffersFromInheritedReference() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Custom", "BaseItemInFolder")]);
		FakeProperties(resolver, Properties(InheritedLookup("Folder", "ContactFolder")));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(FolderItemTarget(OverrideColumn("Folder", " UsrContactSubFolder ")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Reconcile,
			because: "the stored inherited reference differs from the requested one");
		plan.ColumnsToAdd.Should().BeEmpty(
			because: "an override never adds a column");
		UpdateEntitySchemaOperationArgs modify = plan.ColumnsToModify.Should().ContainSingle(
			because: "the redirect is applied through one modify of the inherited column").Subject;
		modify.Action.Should().Be("modify", because: "the redirect goes through the column-manager modify path");
		modify.ColumnName.Should().Be("Folder", because: "the modify targets the inherited column");
		modify.ReferenceSchemaName.Should().Be("UsrContactSubFolder",
			because: "the modify carries the trimmed requested reference");
		modify.Type.Should().BeNull(because: "the type of an inherited column is read-only");
		modify.IsRequired.Should().BeNull(because: "the required flag of an inherited column is read-only");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses an override item whose column the existing schema does not have, instead of adding a column without a type.")]
	public void Classify_ShouldReturnCollision_WhenOverrideColumnIsAbsent() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Custom", "BaseItemInFolder")]);
		FakeProperties(resolver, Properties(InheritedLookup("Folder", "ContactFolder")));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(FolderItemTarget(OverrideColumn("UsrFolder", "ContactFolder")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Collision,
			because: "an override applies only to a column the parent declares");
		plan.Error.Should().Be($"Error: Parent schema 'BaseItemInFolder' of schema '{SchemaName}' has no column "
			+ "'UsrFolder' to override.", because: "the refusal matches the create-entity-schema message");
		plan.ColumnsToAdd.Should().BeEmpty(because: "an override never adds a column");
		plan.ColumnsToModify.Should().BeEmpty(because: "a refused plan carries no mutation");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses an override item whose column is an own column of the existing schema, instead of reporting it already satisfied.")]
	public void Classify_ShouldReturnCollision_WhenOverrideColumnIsOwnColumn() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Custom", "BaseItemInFolder")]);
		FakeProperties(resolver, Properties(Column("Folder", "Lookup") with { ReferenceSchemaName = "ContactFolder" }));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(FolderItemTarget(OverrideColumn("Folder", "ContactFolder")));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Collision,
			because: "an override cannot converge on an own column");
		plan.Error.Should().Be($"Error: Column 'Folder' is an own column of schema '{SchemaName}', not an inherited "
			+ "one; an override applies only to a column inherited from the parent schema.",
			because: "the refusal matches the create-entity-schema message");
		plan.ColumnsToModify.Should().BeEmpty(because: "a refused plan carries no mutation");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps comparing an ordinary lookup column by type only: a differing reference on a non-override column is AlreadySatisfied.")]
	public void Classify_ShouldIgnoreReference_WhenLookupColumnIsNotAnOverride() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Customer", "BaseLookup")]);
		FakeProperties(resolver, Properties(Column("UsrOwner", "Lookup") with { ReferenceSchemaName = "Contact" }));
		SchemaConvergenceService service = new(resolver);
		CreateEntitySchemaColumnArgs requested = new("UsrOwner", "Lookup", null, "Account");

		// Act
		SchemaConvergencePlan plan = service.Classify(LookupTarget(requested));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.AlreadySatisfied,
			because: "only an override item converges on the reference schema");
		plan.ColumnsToModify.Should().BeEmpty(
			because: "an ordinary lookup column is compared by type only");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses an override item without reference-schema-name on an existing schema with the create-entity-schema message, instead of planning a modify with no reference.")]
	public void Classify_ShouldReturnCollision_WhenOverrideColumnHasNoReference() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Custom", "BaseItemInFolder")]);
		FakeProperties(resolver, Properties(InheritedLookup("Folder", "ContactFolder")));
		SchemaConvergenceService service = new(resolver);

		// Act
		SchemaConvergencePlan plan = service.Classify(FolderItemTarget(OverrideColumn("Folder", null)));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.Collision,
			because: "an override without a target cannot converge");
		plan.Error.Should().Be("Error: Override column 'Folder' must specify reference-schema-name.",
			because: "the refusal matches the create-entity-schema message");
		plan.ColumnsToModify.Should().BeEmpty(because: "a refused plan carries no mutation");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an override item that differs from the stored column only by its title as AlreadySatisfied, because an override converges on the reference only.")]
	public void Classify_ShouldReturnAlreadySatisfied_WhenOverrideDiffersByTitleOnly() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		FakeFind(resolver, [new EntitySchemaSearchResult(SchemaName, TargetPackage, "Custom", "BaseItemInFolder")]);
		FakeProperties(resolver, Properties(InheritedLookup("Folder", "ContactFolder")));
		SchemaConvergenceService service = new(resolver);
		CreateEntitySchemaColumnArgs requested = OverrideColumn("Folder", "ContactFolder") with {
			TitleLocalizations = new Dictionary<string, string> { ["en-US"] = "Contact folder" }
		};

		// Act
		SchemaConvergencePlan plan = service.Classify(FolderItemTarget(requested));

		// Assert
		plan.Outcome.Should().Be(SchemaConvergenceOutcome.AlreadySatisfied,
			because: "a title sent with an override is applied on create and not compared on an existing schema");
		plan.ColumnsToModify.Should().BeEmpty(because: "the stored reference already matches");
	}

	private static SchemaConvergenceTarget FolderItemTarget(params CreateEntitySchemaColumnArgs[] requestedColumns) {
		return new SchemaConvergenceTarget(
			"dev", TargetPackage, SchemaName, "BaseItemInFolder", IsLookup: false, ExtendParent: false, requestedColumns);
	}

	private static CreateEntitySchemaColumnArgs OverrideColumn(string name, string? referenceSchemaName) {
		return new CreateEntitySchemaColumnArgs(name, null, null, referenceSchemaName) { Override = true };
	}

	private static EntitySchemaPropertyColumnInfo InheritedLookup(string name, string referenceSchemaName) {
		return Column(name, "Lookup") with { Source = "inherited", ReferenceSchemaName = referenceSchemaName };
	}

	private static SchemaConvergenceTarget LookupTarget(params CreateEntitySchemaColumnArgs[] requestedColumns) {
		return new SchemaConvergenceTarget(
			"dev", TargetPackage, SchemaName, "BaseLookup", IsLookup: true, ExtendParent: false, requestedColumns);
	}

	private static SchemaConvergenceTarget ReplacementEntityTarget(params CreateEntitySchemaColumnArgs[] requestedColumns) {
		// A create-entity replacement schema (extend-parent=true) that shadows a same-name base schema.
		return new SchemaConvergenceTarget(
			"dev", TargetPackage, SchemaName, SchemaName, IsLookup: false, ExtendParent: true, requestedColumns);
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
