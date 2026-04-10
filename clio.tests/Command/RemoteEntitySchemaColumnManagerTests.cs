using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Core.Entities;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
internal class RemoteEntitySchemaColumnManagerTests
{
	private static readonly Guid PackageUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid IdColumnUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
	private static readonly Guid NameColumnUId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
	private static readonly Guid CodeColumnUId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

	private IApplicationPackageListProvider _packageListProvider;
	private IEntitySchemaDefaultValueSourceResolver _defaultValueSourceResolver;
	private IRemoteEntitySchemaDesignerClient _designerClient;
	private ILogger _logger;
	private RemoteEntitySchemaColumnManager _manager;
	private EntityDesignSchemaDto _loadedSchema;
	private EntityDesignSchemaDto _savedSchema;

	[SetUp]
	public void Setup() {
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_defaultValueSourceResolver = Substitute.For<IEntitySchemaDefaultValueSourceResolver>();
		_designerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_logger = Substitute.For<ILogger>();
		_savedSchema = null;
		_loadedSchema = null;
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
		_designerClient.SaveSchemaDbStructure(Arg.Any<Guid>(), Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(new BaseResponse {
				Success = true
			});
		_designerClient.GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(callInfo => new RuntimeEntitySchemaResponse {
				Success = true,
				Schema = new RuntimeEntitySchemaDto {
					UId = callInfo.ArgAt<Guid>(0),
					Name = (_savedSchema ?? _loadedSchema)?.Name ?? "UsrVehicle"
				}
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
		_designerClient.GetSystemValues(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SystemValueLookupValueDto {
					Value = Guid.Parse("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d"),
					DisplayValue = "Current Time and Date"
				}
			});
		_defaultValueSourceResolver
			.Resolve(Arg.Any<EntitySchemaDefaultValueConfig>(), Arg.Any<int>(), Arg.Any<string>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(callInfo =>
				new EntitySchemaDefaultValueSourceResolver(_designerClient).Resolve(
					callInfo.ArgAt<EntitySchemaDefaultValueConfig>(0),
					callInfo.ArgAt<int>(1),
					callInfo.ArgAt<string>(2),
					callInfo.ArgAt<RemoteCommandOptions>(3)));
		_manager = new RemoteEntitySchemaColumnManager(
			_packageListProvider,
			_defaultValueSourceResolver,
			_designerClient,
			_logger);
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
		_designerClient.Received(1).SaveSchemaDbStructure(_savedSchema.UId, Arg.Any<Clio.Command.RemoteCommandOptions>());
		_designerClient.Received(1).GetRuntimeEntitySchema(_savedSchema.UId, Arg.Any<Clio.Command.RemoteCommandOptions>());
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
		EntitySchemaDesignerSupport.GetLocalizableValue(savedColumn.Caption).Should().Be("Primary owner",
			because: "title should be updated for the effective current culture without assuming a single localization entry");
		EntitySchemaDesignerSupport.GetLocalizableValue(savedColumn.Description).Should().Be("Main owner reference",
			because: "description should be updated");
		savedColumn.ReferenceSchema.Name.Should().Be("Contact",
			because: "lookup reference changes must be reflected in the saved payload");
		savedColumn.Indexed.Should().BeTrue(because: "unspecified flags must be preserved");
		savedColumn.IsValueCloneable.Should().BeTrue(because: "unspecified flags must be preserved");
		savedColumn.RequirementType.Should().Be((int)EntitySchemaColumnRequirementType.ApplicationLevel,
			because: "unspecified required settings must be preserved");
		_designerClient.Received(1).SaveSchemaDbStructure(_savedSchema.UId, Arg.Any<Clio.Command.RemoteCommandOptions>());
		_designerClient.Received(1).GetRuntimeEntitySchema(_savedSchema.UId, Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Falls back to the column name when add requests an empty title so caption is never persisted as empty.")]
	public void ModifyColumn_AddsOwnColumn_UsesColumnName_WhenTitleIsEmpty() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrVehicleStatus",
			Type = "Lookup",
			Title = string.Empty,
			ReferenceSchemaName = "Contact"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		EntitySchemaDesignerSupport.GetLocalizableValue(addedColumn.Caption).Should().Be("UsrVehicleStatus",
			because: "empty add titles should fall back to the column code instead of storing an empty caption");
	}

	[Test]
	[Description("Saves only the provided en-US title localization without synthesizing extra culture entries when only en-US is given.")]
	public void ModifyColumn_AddsOwnColumn_SavesOnlyProvidedLocalizations_WhenOnlyEnUsTitleLocalizationIsProvided() {
		// Arrange
		using CultureScope cultureScope = new("uk-UA");
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrVehicleStatus",
			Type = "Text",
			TitleLocalizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["en-US"] = "Status"
			}
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		addedColumn.Caption.Should().Contain(item => item.CultureName == "en-US" && item.Value == "Status",
			because: "the provided en-US title must be saved");
		addedColumn.Caption.Should().NotContain(item => item.CultureName == "uk-UA",
			because: "no uk-UA entry should be synthesized when only en-US was provided");
	}

	[Test]
	[Description("Falls back to the column name when add requests a whitespace title so caption is never persisted as empty.")]
	public void ModifyColumn_AddsOwnColumn_UsesColumnName_WhenTitleIsWhitespace() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrVehicleStatus",
			Type = "Lookup",
			Title = "   ",
			ReferenceSchemaName = "Contact"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		EntitySchemaDesignerSupport.GetLocalizableValue(addedColumn.Caption).Should().Be("UsrVehicleStatus",
			because: "whitespace add titles should fall back to the column code instead of storing an empty caption");
	}

	[Test]
	[Description("Keeps the existing caption when modify receives an empty title so blank payloads do not clear captions.")]
	public void ModifyColumn_PreservesCaption_WhenTitleIsEmpty() {
		// Arrange
		EntitySchemaColumnDto statusColumn = CreateTextColumn("UsrVehicleStatus", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), statusColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrVehicleStatus",
			Title = string.Empty
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		EntitySchemaDesignerSupport.GetLocalizableValue(savedColumn.Caption).Should().Be("UsrVehicleStatus",
			because: "empty modify titles should be ignored and should not clear existing captions");
	}

	[Test]
	[Description("Trims caption updates when modify receives a title with surrounding spaces.")]
	public void ModifyColumn_UpdatesCaptionWithTrimmedValue_WhenTitleContainsWhitespacePadding() {
		// Arrange
		EntitySchemaColumnDto statusColumn = CreateTextColumn("UsrVehicleStatus", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), statusColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrVehicleStatus",
			Title = "  Vehicle Status  "
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		EntitySchemaDesignerSupport.GetLocalizableValue(savedColumn.Caption).Should().Be("Vehicle Status",
			because: "modify title updates should trim accidental whitespace from caller payloads");
	}

	[Test]
	[Description("Saves only the provided en-US title localization without synthesizing extra culture entries when only en-US is given for modify.")]
	public void ModifyColumn_SavesOnlyProvidedLocalizations_WhenOnlyEnUsTitleLocalizationIsProvided() {
		// Arrange
		using CultureScope cultureScope = new("uk-UA");
		EntitySchemaColumnDto statusColumn = CreateTextColumn("UsrVehicleStatus", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), statusColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrVehicleStatus",
			TitleLocalizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["en-US"] = "Vehicle Status"
			}
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		savedColumn.Caption.Should().Contain(item => item.CultureName == "en-US" && item.Value == "Vehicle Status",
			because: "the provided en-US title localization must be saved");
		savedColumn.Caption.Should().NotContain(item => item.CultureName == "uk-UA",
			because: "no uk-UA entry should be synthesized when only en-US was explicitly provided");
		savedColumn.Caption.Select(item => item.CultureName).Should().OnlyHaveUniqueItems(
			because: "caption must not contain duplicate culture entries");
	}

	[Test]
	[Description("Preserves all entity-level caption culture entries when a column mutation is applied, so that multi-language entity titles survive the LoadSchema-SaveSchema round-trip.")]
	public void ModifyColumn_PreservesAllEntityCultureCaptions_WhenSchemaHasMultiCultureEntityCaption() {
		// Arrange
		using CultureScope cultureScope = new("uk-UA");
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		_loadedSchema.Caption = [
			new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "en-US", Value = "MyTest" },
			new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "uk-UA", Value = "Об'єкт" }
		];
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrStartDate",
			Type = "DateTime",
			TitleLocalizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["en-US"] = "Start date"
			}
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Caption.Should().Contain(c => c.CultureName == "en-US" && c.Value == "MyTest",
			because: "the en-US entity caption must be preserved after a column mutation");
		_savedSchema.Caption.Should().Contain(c => c.CultureName == "uk-UA" && c.Value == "Об'єкт",
			because: "the uk-UA entity caption must also be preserved so that a round-trip through LoadSchema/SaveSchema does not wipe other-culture titles");
	}

	[Test]
	[Description("Preserves all entity-level caption culture entries when a column mutation is applied, so that multi-language entity titles survive the LoadSchema-SaveSchema round-trip.")]
	public void ModifyColumn_PreservesAllEntityCultureCaptions_WhenSchemaHasMultiCultureEntityCaption() {
		// Arrange
		using CultureScope cultureScope = new("uk-UA");
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		_loadedSchema.Caption = [
			new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "en-US", Value = "MyTest" },
			new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "uk-UA", Value = "Об'єкт" }
		];
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrStartDate",
			Type = "DateTime",
			TitleLocalizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["en-US"] = "Start date"
			}
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Caption.Should().Contain(c => c.CultureName == "en-US" && c.Value == "MyTest",
			because: "the en-US entity caption must be preserved after a column mutation");
		_savedSchema.Caption.Should().Contain(c => c.CultureName == "uk-UA" && c.Value == "Об'єкт",
			because: "the uk-UA entity caption must also be preserved so that a round-trip through LoadSchema/SaveSchema does not wipe other-culture titles");
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
		nameColumn.Description = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
			CultureName = "en-US",
			Value = "Vehicle name"
		}];
		nameColumn.Indexed = true;
		nameColumn.RequirementType = (int)EntitySchemaColumnRequirementType.ApplicationLevel;
		EntitySchemaColumnDto ownerColumn = CreateLookupColumn("Owner", CodeColumnUId, "Contact");
		ownerColumn.Description = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
			CultureName = "en-US",
			Value = "Owner lookup"
		}];
		_loadedSchema = CreateSchema(columns: [idColumn, nameColumn],
			inheritedColumns: [ownerColumn],
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
		result.Columns.Should().HaveCount(3,
			because: "the schema read model should expose both own and inherited columns for structured verification");
			result.Columns!.Select(column => column.Name).Should().Equal(["Id", "Name", "Owner"],
				because: "schema columns should keep designer order with own columns before inherited columns");
		result.Columns[1].Source.Should().Be("own",
			because: "columns loaded from schema.Columns should be marked as own");
		result.Columns[1].Title.Should().Be("Name",
			because: "localized captions should be projected into the compact schema column model");
		result.Columns[1].Description.Should().Be("Vehicle name",
			because: "localized descriptions should be projected into the compact schema column model");
		result.Columns[1].Type.Should().Be("Text",
			because: "friendly type aliases should be reused for schema column projections");
		result.Columns[1].Required.Should().BeTrue(
			because: "required flags should be projected through the shared schema read model");
		result.Columns[1].Indexed.Should().BeTrue(
			because: "indexed flags should be projected through the shared schema read model");
		result.Columns[2].Source.Should().Be("inherited",
			because: "columns loaded from schema.InheritedColumns should be marked as inherited");
		result.Columns[2].ReferenceSchemaName.Should().Be("Contact",
			because: "lookup columns should expose their reference schema in the schema read model");
	}

	[Test]
	[Description("Returns structured column properties so CLI and MCP can share the same projection.")]
	public void GetColumnProperties_ReturnsStructuredColumnProperties() {
		// Arrange
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		nameColumn.DataValueType = 27;
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
		result.Type.Should().Be("ShortText", because: "frontend-compatible aliases should be projected for supported designer text subtypes");
		result.DefaultValueSource.Should().Be("Const", because: "the structured result should preserve the explicit default source");
		result.DefaultValue.Should().Be("Vehicle", because: "default values should remain available in the structured result");
		result.DefaultValueConfig.Should().NotBeNull(
			because: "column readback should also expose the structured default value config");
		result.DefaultValueConfig!.Source.Should().Be("Const",
			because: "the structured default value config should preserve the explicit default source");
		result.DefaultValueConfig.Value.Should().Be("Vehicle",
			because: "the structured default value config should preserve the constant payload");
		result.MultilineText.Should().BeTrue(because: "text-specific flags should be projected");
	}

	[Test]
	[Description("Returns additive resolved-value-source metadata for system-value defaults in structured readback.")]
	public void GetColumnProperties_ReturnsResolvedValueSource_ForSystemValueDefault() {
		// Arrange
		EntitySchemaColumnDto startDateColumn = CreateTextColumn("UsrStartDate", NameColumnUId);
		startDateColumn.DataValueType = 7;
		startDateColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.SystemValue,
			ValueSource = "d7c295d3-3146-4ee1-ac49-3a7bd0edc45d"
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), startDateColumn]);
		SetupLoadedSchema();

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "UsrStartDate"
		});

		// Assert
		result.DefaultValueConfig.Should().NotBeNull(
			because: "system-value defaults should produce structured default-value-config metadata");
		result.DefaultValueConfig!.ResolvedValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "readback should include the stable resolved identifier alongside value-source");
	}

	[Test]
	[Description("Clears a saved default value when modify requests default-value-source none without requiring other field changes.")]
	public void ModifyColumn_ClearsDefaultValue_WhenDefaultValueSourceIsNone() {
		// Arrange
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		nameColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.Const,
			Value = "Vehicle"
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), nameColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Name",
			DefaultValueSource = "None"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "Name");
		savedColumn.DefValue.Should().BeNull(
			because: "explicit None should clear the persisted default value instead of preserving stale data");
	}

	[Test]
	[Description("Applies structured default-value-config metadata so system-value defaults can be set without legacy shorthand fields.")]
	public void ModifyColumn_AppliesStructuredDefaultValueConfig() {
		// Arrange
		EntitySchemaColumnDto startDateColumn = CreateTextColumn("UsrStartDate", NameColumnUId);
		startDateColumn.DataValueType = 7;
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), startDateColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrStartDate",
			DefaultValueConfig = new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "CurrentDateTime"
			}
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrStartDate");
		savedColumn.DefValue.Should().NotBeNull(
			because: "structured default value configs should create designer default value metadata");
		savedColumn.DefValue!.ValueSourceType.Should().Be(EntitySchemaColumnDefSource.SystemValue,
			because: "the structured config source should map to the designer system-value source");
		savedColumn.DefValue.ValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "the structured config value-source should be normalized to the canonical GUID");
	}

	[Test]
	[Description("Normalizes Settings defaults from setting names to canonical setting codes when modifying columns.")]
	public void ModifyColumn_AppliesStructuredSettingsDefaultValueConfig_UsingCanonicalCode() {
		// Arrange
		EntitySchemaColumnDto titleColumn = CreateTextColumn("UsrTitle", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), titleColumn]);
		SetupLoadedSchema();
		_designerClient.GetSysSettingsByValueTypeName("Text", Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SysSettingsSelectQueryRowDto {
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Code = "UsrDefaultTitle",
					Name = "Default Title",
					ValueTypeName = "Text"
				}
			});
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrTitle",
			DefaultValueConfig = new EntitySchemaDefaultValueConfig {
				Source = "Settings",
				ValueSource = "Default Title"
			}
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrTitle");
		savedColumn.DefValue.Should().NotBeNull(
			because: "structured settings defaults should create designer default value metadata");
		savedColumn.DefValue!.ValueSourceType.Should().Be(EntitySchemaColumnDefSource.Settings,
			because: "the structured config source should map to the designer settings source");
		savedColumn.DefValue.ValueSource.Should().Be("UsrDefaultTitle",
			because: "settings defaults should be normalized to canonical setting codes");
	}

	[Test]
	[Description("Accepts frontend-style type aliases when adding columns and maps them to the closest supported designer types.")]
	public void ModifyColumn_AddsOwnColumn_FromFrontendTypeAlias() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Status",
			Type = "ShortText",
			Title = "Status"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "Status");
		addedColumn.DataValueType.Should().Be(27,
			because: "frontend ShortText aliases should map to the closest supported designer value");
		_savedSchema.PrimaryDisplayColumn.Name.Should().Be("Status",
			because: "frontend text aliases should still count as display-capable text columns");
	}

	[TestCase("Binary", 13)]
	[TestCase("Blob", 13)]
	[TestCase("Image", 14)]
	[TestCase("File", 25)]
	[Description("Adds Binary, Image, File, and Blob-alias columns and persists their runtime data value type ids without assigning a text display column.")]
	public void ModifyColumn_AddsOwnBinaryLikeColumn(string typeName, int expectedDataValueType) {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Payload",
			Type = typeName,
			Title = "Payload"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "Payload");
		addedColumn.DataValueType.Should().Be(expectedDataValueType,
			because: "supported binary-like column types should map to their expected runtime data value ids");
		_savedSchema.PrimaryDisplayColumn.Should().BeNull(
			because: "binary-like columns should not be promoted to primary display columns");
	}

	[TestCase("Binary", 13)]
	[TestCase("Blob", 13)]
	[TestCase("Image", 14)]
	[TestCase("File", 25)]
	[Description("Allows modify flows to switch an own column to Binary, Image, File, or Blob alias without treating it as lookup metadata.")]
	public void ModifyColumn_UpdatesOwnColumn_ToBinaryLikeType(string typeName, int expectedDataValueType) {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), CreateTextColumn("Payload", NameColumnUId)],
			primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Payload",
			Type = typeName
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "Payload");
		savedColumn.DataValueType.Should().Be(expectedDataValueType,
			because: "modify flows should preserve supported binary-like runtime type ids");
		savedColumn.ReferenceSchema.Should().BeNull(
			because: "binary-like columns should not carry lookup reference metadata after modify");
	}

	[TestCase("Binary")]
	[TestCase("Image")]
	[TestCase("File")]
	[Description("Rejects constant defaults for Binary, Image, and File column mutations because the mutation contract does not support binary default payloads.")]
	public void ModifyColumn_Throws_WhenBinaryLikeTypeUsesConstDefault(string typeName) {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Payload",
			Type = typeName,
			DefaultValueSource = "Const",
			DefaultValue = "AAECAw=="
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*does not support --default-value or --default-value-source Const*",
				because: "binary-like mutation flows should reject unsupported constant default payloads");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Allows masked flag for Password alias and maps the column to SecureText runtime type.")]
	public void ModifyColumn_AddsPasswordColumn_WithMaskedFlag() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrPassword",
			Type = "Password",
			Title = "Password",
			Masked = true
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrPassword");
		addedColumn.DataValueType.Should().Be(24,
			because: "Password alias should resolve to SecureText runtime type");
		addedColumn.Masked.Should().BeTrue(
			because: "masked flag should be accepted for SecureText password columns");
		addedColumn.ValueMasked.Should().BeTrue(
			because: "masked flag should also set schema-level value masking");
	}

	[Test]
	[Description("Keeps masked and value-masked false when adding SecureText without explicit masked flag.")]
	public void ModifyColumn_AddsSecureTextColumn_MaskedFalseByDefault() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrPassword",
			Type = "SecureText",
			Title = "Password"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrPassword");
		addedColumn.DataValueType.Should().Be(24,
			because: "SecureText should map to runtime data value type 24");
		addedColumn.Masked.Should().BeFalse(
			because: "SecureText add should not force masked=true when no explicit flag is provided");
		addedColumn.ValueMasked.Should().BeFalse(
			because: "SecureText add should keep value masking disabled unless explicitly requested");
	}

	[Test]
	[Description("Rejects masked flag for non-text and non-secure text effective types.")]
	public void ModifyColumn_Throws_WhenMaskedUsedOnUnsupportedType() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrCode",
			Type = "Integer",
			Masked = true
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*Masked option can be used only when the effective column type is Text or SecureText*",
				because: "masked should stay restricted to compatible field types");
	}

	[TestCase(13, "Binary")]
	[TestCase(14, "Image")]
	[TestCase(16, "ImageLookup")]
	[TestCase(25, "File")]
	[Description("Returns normalized friendly type names for binary-like and image lookup columns through structured readback.")]
	public void GetColumnProperties_ReturnsFriendlyTypeNames_ForBinaryLikeColumns(int dataValueType, string expectedTypeName) {
		// Arrange
		EntitySchemaColumnDto payloadColumn = CreateTextColumn("Payload", NameColumnUId);
		payloadColumn.DataValueType = dataValueType;
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), payloadColumn], primaryDisplayColumn: null);
		SetupLoadedSchema();

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Payload"
		});

		// Assert
		result.Type.Should().Be(expectedTypeName,
			because: "structured readback should expose normalized friendly type names instead of raw numeric ids");
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
		_logger.Received().WriteInfo("Default value source: Const");
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
			.Returns(_ => new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true,
				Schema = _savedSchema ?? _loadedSchema
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

	private sealed class CultureScope : IDisposable {
		private readonly CultureInfo _originalCurrentCulture;
		private readonly CultureInfo _originalCurrentUiCulture;

		public CultureScope(string cultureName) {
			_originalCurrentCulture = CultureInfo.CurrentCulture;
			_originalCurrentUiCulture = CultureInfo.CurrentUICulture;
			CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;
		}

		public void Dispose() {
			CultureInfo.CurrentCulture = _originalCurrentCulture;
			CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
		}
	}
}
