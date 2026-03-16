using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Core.Entities;

namespace Clio.Tests.Command;

[TestFixture]
internal class RemoteEntitySchemaColumnManagerTests
{
	private static readonly Guid PackageUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid IdColumnUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
	private static readonly Guid NameColumnUId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
	private static readonly Guid CodeColumnUId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

	private IApplicationPackageListProvider _packageListProvider;
	private IRemoteEntitySchemaDesignerClient _designerClient;
	private ILogger _logger;
	private RemoteEntitySchemaColumnManager _manager;
	private EntityDesignSchemaDto _loadedSchema;
	private EntityDesignSchemaDto _savedSchema;

	[SetUp]
	public void Setup() {
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_designerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_logger = Substitute.For<ILogger>();
		_packageListProvider.GetPackages().Returns(new[] {
			new PackageInfo(new PackageDescriptor {
				Name = "UsrPkg",
				UId = PackageUId
			}, string.Empty, Enumerable.Empty<string>())
		});
		_designerClient.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(callInfo => {
				_savedSchema = callInfo.ArgAt<EntityDesignSchemaDto>(0);
				return new Clio.Command.EntitySchemaDesigner.SaveDesignItemDesignerResponse {
					Success = true,
					SchemaUId = _savedSchema.UId
				};
			});
		_designerClient.GetAvailableReferenceSchemas(Arg.Any<GetAvailableSchemasRequestDto>(), Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(new AvailableEntitySchemasResponse {
				Success = true,
				Items = [
					new ManagerItemDto {
						UId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
						Name = "Contact",
						Caption = "Contact"
					}
				]
			});
		_manager = new RemoteEntitySchemaColumnManager(_packageListProvider, _designerClient, _logger);
	}

	[Test]
	[Description("Adds a new own column and assigns it as primary display column when the schema has no display column yet.")]
	public void ModifyColumn_AddsOwnColumn_AndSetsPrimaryDisplayColumn() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Name",
			Type = "Text",
			Title = "Vehicle name",
			Description = "Display caption"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Should().NotBeNull(because: "successful mutations should save the adjusted schema");
		_savedSchema.Columns.Should().ContainSingle(column => column.Name == "Name",
			because: "the new own column must be appended to the schema");
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "Name");
		addedColumn.DataValueType.Should().Be(1, because: "Text should map to the supported designer value");
		addedColumn.Caption.Single().Value.Should().Be("Vehicle name", because: "title should be stored as caption");
		addedColumn.Description.Single().Value.Should().Be("Display caption",
			because: "description should be stored as a localizable designer field");
		_savedSchema.PrimaryDisplayColumn.Name.Should().Be("Name",
			because: "the first text column should become primary display when none exists");
	}

	[Test]
	[Description("Modifies an own lookup column, preserves unspecified fields, and updates the reference schema when requested.")]
	public void ModifyColumn_UpdatesOwnColumn_AndPreservesUnspecifiedFields() {
		// Arrange
		EntitySchemaColumnDto ownerColumn = CreateLookupColumn("Owner", NameColumnUId, "Account");
		ownerColumn.Indexed = true;
		ownerColumn.IsValueCloneable = true;
		ownerColumn.RequirementType = (int)EntitySchemaColumnRequirementType.ApplicationLevel;
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), ownerColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Owner",
			NewName = "PrimaryOwner",
			Title = "Primary owner",
			Description = "Main owner reference",
			ReferenceSchemaName = "Contact"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.UId == NameColumnUId);
		savedColumn.Name.Should().Be("PrimaryOwner", because: "rename should update the column code");
		savedColumn.Caption.Single().Value.Should().Be("Primary owner", because: "title should be updated");
		savedColumn.Description.Single().Value.Should().Be("Main owner reference",
			because: "description should be updated");
		savedColumn.ReferenceSchema.Name.Should().Be("Contact",
			because: "lookup reference changes must be reflected in the saved payload");
		savedColumn.Indexed.Should().BeTrue(because: "unspecified flags must be preserved");
		savedColumn.IsValueCloneable.Should().BeTrue(because: "unspecified flags must be preserved");
		savedColumn.RequirementType.Should().Be((int)EntitySchemaColumnRequirementType.ApplicationLevel,
			because: "unspecified required settings must be preserved");
	}

	[Test]
	[Description("Removes an own column, reassigns display column fallback, and clears other schema-level references.")]
	public void ModifyColumn_RemovesOwnColumn_AndReassignsReferences() {
		// Arrange
		EntitySchemaColumnDto idColumn = CreateGuidColumn("Id", IdColumnUId);
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		EntitySchemaColumnDto codeColumn = CreateTextColumn("Code", CodeColumnUId);
		_loadedSchema = CreateSchema(columns: [idColumn, nameColumn, codeColumn], primaryDisplayColumn: nameColumn);
		_loadedSchema.PrimaryImageColumn = nameColumn;
		_loadedSchema.PrimaryColorColumn = nameColumn;
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "remove",
			ColumnName = "Name"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Columns.Should().NotContain(column => column.Name == "Name",
			because: "the removed own column should not be persisted");
		_savedSchema.PrimaryDisplayColumn.Name.Should().Be("Code",
			because: "the next available text column should replace the display column");
		_savedSchema.PrimaryImageColumn.Should().BeNull(because: "non-required schema references should be cleared");
		_savedSchema.PrimaryColorColumn.Should().BeNull(because: "non-required schema references should be cleared");
	}

	[Test]
	[Description("Rejects modifications of inherited columns because v1 supports mutations for own columns only.")]
	public void ModifyColumn_Throws_WhenColumnIsInherited() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [CreateTextColumn("Name", NameColumnUId)]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Name",
			Title = "Vehicle name"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*inherited and read-only*",
				because: "inherited columns are explicitly out of scope for v1 mutations");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Rejects duplicate column names when adding a new own column.")]
	public void ModifyColumn_Throws_WhenAddingDuplicateColumnName() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), CreateTextColumn("Name", NameColumnUId)]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Name",
			Type = "Text"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*already exists*",
				because: "column names must remain unique across the design schema");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Rejects lookup creation when the required reference schema was not provided.")]
	public void ModifyColumn_Throws_WhenAddingLookupWithoutReferenceSchema() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Owner",
			Type = "Lookup"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*require --reference-schema*",
				because: "lookup columns cannot be created without a target schema");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Rejects primary column removal when no remaining guid column can replace it.")]
	public void ModifyColumn_Throws_WhenRemovingPrimaryColumnWithoutGuidFallback() {
		// Arrange
		EntitySchemaColumnDto idColumn = CreateGuidColumn("Id", IdColumnUId);
		_loadedSchema = CreateSchema(columns: [idColumn, CreateTextColumn("Name", NameColumnUId)], primaryColumn: idColumn);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "remove",
			ColumnName = "Id"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*no valid fallback exists*",
				because: "schemas must keep a valid primary guid column");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Returns structured schema properties so CLI and MCP can share the same projection.")]
	public void GetSchemaProperties_ReturnsStructuredSchemaProperties() {
		// Arrange
		EntitySchemaColumnDto idColumn = CreateGuidColumn("Id", IdColumnUId);
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [idColumn, nameColumn],
			inheritedColumns: [CreateLookupColumn("Owner", CodeColumnUId, "Contact")],
			primaryColumn: idColumn,
			primaryDisplayColumn: nameColumn);
		_loadedSchema.ParentSchema = new EntityDesignSchemaDto {
			Name = "BaseEntity"
		};
		_loadedSchema.ExtendParent = true;
		_loadedSchema.IsTrackChangesInDB = true;
		_loadedSchema.Indexes = [new object(), new object()];
		SetupLoadedSchema();

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		});

		// Assert
		result.Name.Should().Be("UsrVehicle", because: "the schema name should be preserved in the structured result");
		result.ParentSchemaName.Should().Be("BaseEntity", because: "parent schema should be projected");
		result.OwnColumnCount.Should().Be(2, because: "own column count should be included in the structured result");
		result.InheritedColumnCount.Should().Be(1,
			because: "inherited column count should be included in the structured result");
		result.TrackChangesInDb.Should().BeTrue(
			because: "schema flags should remain available to both MCP and CLI formatters");
	}

	[Test]
	[Description("Returns structured column properties so CLI and MCP can share the same projection.")]
	public void GetColumnProperties_ReturnsStructuredColumnProperties() {
		// Arrange
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		nameColumn.Indexed = true;
		nameColumn.MultiLineText = true;
		nameColumn.LocalizableText = true;
		nameColumn.AccentInsensitive = true;
		nameColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.Const,
			Value = "Vehicle"
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), nameColumn]);
		SetupLoadedSchema();

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Name"
		});

		// Assert
		result.ColumnName.Should().Be("Name", because: "the requested column name should be preserved");
		result.Source.Should().Be("own", because: "the result should indicate whether the column is own or inherited");
		result.Type.Should().Be("Text", because: "the friendly data type should be projected");
		result.DefaultValue.Should().Be("Vehicle", because: "default values should remain available in the structured result");
		result.MultilineText.Should().BeTrue(because: "text-specific flags should be projected");
	}

	[Test]
	[Description("Prints own column properties in a human-readable form.")]
	public void PrintColumnProperties_PrintsOwnColumnProperties() {
		// Arrange
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		nameColumn.Indexed = true;
		nameColumn.MultiLineText = true;
		nameColumn.LocalizableText = true;
		nameColumn.AccentInsensitive = true;
		nameColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.Const,
			Value = "Vehicle"
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), nameColumn]);
		SetupLoadedSchema();

		// Act
		_manager.PrintColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Name"
		});

		// Assert
		_logger.Received().WriteInfo("Entity schema column properties");
		_logger.Received().WriteInfo("Source: own");
		_logger.Received().WriteInfo("Type: Text");
		_logger.Received().WriteInfo("Indexed: true");
		_logger.Received().WriteInfo("Default value: Vehicle");
	}

	[Test]
	[Description("Prints inherited column properties when the column does not exist among own columns.")]
	public void PrintColumnProperties_PrintsInheritedColumnProperties() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [CreateLookupColumn("Owner", NameColumnUId, "Contact")]);
		SetupLoadedSchema();

		// Act
		_manager.PrintColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Owner"
		});

		// Assert
		_logger.Received().WriteInfo("Source: inherited");
		_logger.Received().WriteInfo("Reference schema: Contact");
		_logger.Received().WriteInfo("Type: Lookup");
	}

	[Test]
	[Description("Prints a schema summary with counts, parent schema, and main flags.")]
	public void PrintSchemaProperties_PrintsSchemaSummary() {
		// Arrange
		EntitySchemaColumnDto idColumn = CreateGuidColumn("Id", IdColumnUId);
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [idColumn, nameColumn],
			inheritedColumns: [CreateLookupColumn("Owner", CodeColumnUId, "Contact")],
			primaryColumn: idColumn,
			primaryDisplayColumn: nameColumn);
		_loadedSchema.ParentSchema = new EntityDesignSchemaDto {
			Name = "BaseEntity"
		};
		_loadedSchema.ExtendParent = true;
		_loadedSchema.IsTrackChangesInDB = true;
		_loadedSchema.IsVirtual = true;
		_loadedSchema.Indexes = [new object(), new object()];
		SetupLoadedSchema();

		// Act
		_manager.PrintSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		});

		// Assert
		_logger.Received().WriteInfo("Entity schema properties");
		_logger.Received().WriteInfo("Parent schema: BaseEntity");
		_logger.Received().WriteInfo("Extend parent: true");
		_logger.Received().WriteInfo("Own columns: 2");
		_logger.Received().WriteInfo("Inherited columns: 1");
		_logger.Received().WriteInfo("Indexes: 2");
		_logger.Received().WriteInfo("Track changes in DB: true");
		_logger.Received().WriteInfo("Virtual: true");
	}

	private void SetupLoadedSchema() {
		_designerClient.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true,
				Schema = _loadedSchema
			});
	}

	private static EntityDesignSchemaDto CreateSchema(IEnumerable<EntitySchemaColumnDto> columns,
		IEnumerable<EntitySchemaColumnDto> inheritedColumns = null, EntitySchemaColumnDto primaryColumn = null,
		EntitySchemaColumnDto primaryDisplayColumn = null) {
		List<EntitySchemaColumnDto> ownColumns = columns.ToList();
		EntitySchemaColumnDto resolvedPrimaryColumn = primaryColumn ?? ownColumns.FirstOrDefault(column => column.Name == "Id");
		EntitySchemaColumnDto resolvedPrimaryDisplayColumn = primaryDisplayColumn ?? ownColumns.FirstOrDefault(column => column.Name == "Name");
		return new EntityDesignSchemaDto {
			UId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
			Name = "UsrVehicle",
			Package = new Clio.Command.EntitySchemaDesigner.WorkspacePackageDto {
				UId = PackageUId,
				Name = "UsrPkg"
			},
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = "Vehicle"
			}],
			Columns = ownColumns,
			InheritedColumns = inheritedColumns?.ToList() ?? [],
			Indexes = [],
			PrimaryColumn = resolvedPrimaryColumn,
			PrimaryDisplayColumn = resolvedPrimaryDisplayColumn
		};
	}

	private static EntitySchemaColumnDto CreateGuidColumn(string name, Guid uId) {
		return new EntitySchemaColumnDto {
			UId = uId,
			Name = name,
			DataValueType = 0,
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = name
			}]
		};
	}

	private static EntitySchemaColumnDto CreateTextColumn(string name, Guid uId) {
		return new EntitySchemaColumnDto {
			UId = uId,
			Name = name,
			DataValueType = 1,
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = name
			}]
		};
	}

	private static EntitySchemaColumnDto CreateLookupColumn(string name, Guid uId, string referenceSchemaName) {
		return new EntitySchemaColumnDto {
			UId = uId,
			Name = name,
			DataValueType = 10,
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = name
			}],
			ReferenceSchema = new EntityDesignSchemaDto {
				UId = Guid.NewGuid(),
				Name = referenceSchemaName,
				Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
					CultureName = "en-US",
					Value = referenceSchemaName
				}]
			}
		};
	}
}
