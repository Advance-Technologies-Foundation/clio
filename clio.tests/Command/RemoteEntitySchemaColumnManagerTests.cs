using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
[Category("Unit")]
[Property("Module", "Command")]
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
	private Clio.Common.EntitySchema.IRuntimeEntitySchemaReader _runtimeEntitySchemaReader;
	private ILookupDefaultDisplayValueResolver _lookupDefaultDisplayValueResolver;
	private IEntitySchemaCaptionCultureResolver _captionCultureResolver;
	private IEntitySchemaDependencyResolver _dependencyResolver;
	private ILogger _logger;
	private RemoteEntitySchemaColumnManager _manager;
	private EntityDesignSchemaDto _loadedSchema;
	private EntityDesignSchemaDto _savedSchema;

	[SetUp]
	public void Setup() {
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_defaultValueSourceResolver = Substitute.For<IEntitySchemaDefaultValueSourceResolver>();
		_designerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_runtimeEntitySchemaReader = Substitute.For<Clio.Common.EntitySchema.IRuntimeEntitySchemaReader>();
		_lookupDefaultDisplayValueResolver = Substitute.For<ILookupDefaultDisplayValueResolver>();
		// Default: enrichment is a no-op (both fields null) so existing readbacks stay GUID-only;
		// the dedicated lookup-Const enrichment test re-stubs this to assert display-value mapping.
		_lookupDefaultDisplayValueResolver
			.Resolve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new LookupDefaultResolution(null, null));
		_logger = Substitute.For<ILogger>();
		_captionCultureResolver = Substitute.For<IEntitySchemaCaptionCultureResolver>();
		// Default: effective culture is the en-US fallback (parity with the previous default behavior on
		// CI). Tests that exercise the resolved-profile path re-stub this to return uk-UA.
		_captionCultureResolver
			.ResolveEffectiveCulture(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>())
			.Returns("en-US");
		_dependencyResolver = Substitute.For<IEntitySchemaDependencyResolver>();
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
				Arg.Any<RemoteCommandOptions>(), Arg.Any<string?>())
			.Returns(callInfo =>
				new EntitySchemaDefaultValueSourceResolver(_designerClient).Resolve(
					callInfo.ArgAt<EntitySchemaDefaultValueConfig>(0),
					callInfo.ArgAt<int>(1),
					callInfo.ArgAt<string>(2),
					callInfo.ArgAt<RemoteCommandOptions>(3),
					callInfo.ArgAt<string?>(4)));
		_manager = new RemoteEntitySchemaColumnManager(
			_packageListProvider,
			new EntitySchemaColumnResolvers(
				_defaultValueSourceResolver,
				_lookupDefaultDisplayValueResolver,
				_captionCultureResolver),
			_designerClient,
			_runtimeEntitySchemaReader,
			_dependencyResolver,
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
	[Description("Adds a Color column as data value type 18 and does not treat it as the primary display column (Color is not text-like).")]
	public void ModifyColumn_AddsColorColumn_WhenTypeIsColor() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Highlight",
			Type = "Color",
			Title = "Highlight color"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Should().NotBeNull(because: "adding a Color column should save the adjusted schema");
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "Highlight");
		addedColumn.DataValueType.Should().Be(18,
			because: "the Color token must map to the platform Color data value type 18");
		_savedSchema.PrimaryDisplayColumn.Should().BeNull(
			because: "a Color column is not text-like, so it must not be auto-assigned as the primary display column");
	}

	[Test]
	[Description("Rejects text-only options on a Color column because Color is not a text-like type.")]
	public void ModifyColumn_Throws_WhenColorColumnUsesTextOnlyOption() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "Highlight",
			Type = "Color",
			Title = "Highlight color",
			Masked = true
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*Masked*",
				because: "text-only options like masked must not be accepted on a Color column");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Publishes the configuration and requests the OData entities rebuild after saving a column, in that order, so the column is reachable over OData.")]
	public void ModifyColumn_PublishesAndRequestsODataRebuild_AfterSaving() {
		// Arrange — capture the order the designer client is called in.
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		List<string> calls = [];
		_designerClient.When(c => c.SaveSchemaDbStructure(Arg.Any<Guid>(), Arg.Any<Clio.Command.RemoteCommandOptions>()))
			.Do(_ => calls.Add("save-db"));
		_designerClient.When(c => c.PublishConfigurationChanges(Arg.Any<Clio.Command.RemoteCommandOptions>()))
			.Do(_ => calls.Add("publish"));
		_designerClient.When(c => c.RunODataBuild(Arg.Any<Clio.Command.RemoteCommandOptions>()))
			.Do(_ => calls.Add("rebuild"));
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle", Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		calls.Should().Equal(["save-db", "publish", "rebuild"],
			because: "a saved column is invisible over OData until the configuration is published and the OData entities assembly is rebuilt; the rebuild must follow the publish");
	}

	[Test]
	[Description("Publishes and rebuilds OData exactly once for a multi-operation (add + modify) batch, not once per operation.")]
	public void ModifyColumns_PublishesAndRebuildsOnce_ForMultiOperationBatch() {
		// Arrange
		_loadedSchema = CreateSchema(
			columns: [CreateGuidColumn("Id", IdColumnUId), CreateTextColumn("Status", CodeColumnUId)],
			primaryDisplayColumn: null);
		SetupLoadedSchema();
		int publishCount = 0, rebuildCount = 0;
		_designerClient.When(c => c.PublishConfigurationChanges(Arg.Any<Clio.Command.RemoteCommandOptions>()))
			.Do(_ => publishCount++);
		_designerClient.When(c => c.RunODataBuild(Arg.Any<Clio.Command.RemoteCommandOptions>()))
			.Do(_ => rebuildCount++);
		ModifyEntitySchemaColumnOptions[] batch = [
			new() { Package = "UsrPkg", SchemaName = "UsrVehicle", Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name" },
			new() { Package = "UsrPkg", SchemaName = "UsrVehicle", Action = "modify", ColumnName = "Status", Title = "Vehicle status" }
		];

		// Act
		_manager.ModifyColumns(batch);

		// Assert
		publishCount.Should().Be(1,
			because: "a multi-column batch must publish the configuration exactly once, not once per operation");
		rebuildCount.Should().Be(1,
			because: "the OData entities rebuild must be requested once per batch, after publishing");
	}

	[Test]
	[Description("Succeeds with a warning when the OData rebuild request fails, because a rebuild-request fault must not fail an already-saved column change.")]
	public void ModifyColumn_SucceedsWithWarning_WhenODataRebuildRequestFails() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		_designerClient.RunODataBuild(Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(_ => throw new HttpRequestException("connection reset"));
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle", Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().NotThrow(
			because: "a rebuild-request failure must not fail a column change that was already saved");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains(EntitySchemaPublishHelper.ODataBuildRequestFailedWarningFragment, StringComparison.Ordinal)));
		// because: the rebuild-request failure must be surfaced as a warning so it is visible, not silent
	}

	[Test]
	[Description("Throws an actionable error when publishing the configuration fails after saving a column.")]
	public void ModifyColumn_Throws_WhenPublishFails() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		_designerClient.PublishConfigurationChanges(Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(_ => throw new InvalidOperationException("Compilation failed."));
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle", Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*publishing the configuration failed*Compilation failed.*",
				because: "a publish failure leaves the column invisible and must surface to the caller");
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
	[Description("Anchors a written column caption to the resolved profile culture (uk-UA) when the resolver succeeds (AC-02, WRITE path).")]
	public void SetLocalizableValue_ShouldUseEffectiveCulture_WhenProfileResolved() {
		// Arrange — the profile culture resolves to uk-UA for this run.
		_captionCultureResolver
			.ResolveEffectiveCulture(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>())
			.Returns("uk-UA");
		EntitySchemaColumnDto statusColumn = CreateTextColumn("UsrVehicleStatus", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), statusColumn]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrVehicleStatus",
			Title = "Статус"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrVehicleStatus");
		savedColumn.Caption.Should().Contain(item => item.CultureName == "uk-UA" && item.Value == "Статус",
			because: "a resolved profile culture must anchor the written column caption (AC-02)");
	}

	[Test]
	[Description("Reads/displays column captions using the host locale, not the resolved profile culture (Mi-3, READ path).")]
	public void GetColumnProperties_ShouldUseHostLocale_WhenReadingColumns() {
		// Arrange — column has both en-US and uk-UA captions; the host locale is uk-UA.
		using CultureScope cultureScope = new("uk-UA");
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		nameColumn.Caption = [
			new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "en-US", Value = "Name EN" },
			new Clio.Command.EntitySchemaDesigner.LocalizableStringDto { CultureName = "uk-UA", Value = "Імʼя" }
		];
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), nameColumn]);
		SetupLoadedSchema();

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Name"
		});

		// Assert
		result.Title.Should().Be("Імʼя",
			because: "READ/display paths format output for the operator's console using the host locale (Mi-3), not the profile culture");
	}

	[Test]
	[Description("GetColumnProperties throws the enriched error when the schema is unavailable — the read path surfaces the diagnostic without any side effects (ENG-91314).")]
	public void GetColumnProperties_ShouldThrowEnrichedError_WhenSchemaIsUnavailable() {
		// Arrange
		SetupUnavailableSchema();

		// Act
		Action act = () => _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Name"
		});

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "an unavailable schema on a read path must surface the enriched diagnostic, not silently succeed");
	}

	[Test]
	[Description("GetSchemaProperties throws the enriched error when the schema is unavailable — the read path surfaces the diagnostic without any side effects (ENG-91314).")]
	public void GetSchemaProperties_ShouldThrowEnrichedError_WhenSchemaIsUnavailable() {
		// Arrange
		SetupUnavailableSchema();

		// Act
		Action act = () => _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		});

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "an unavailable schema on a read path must surface the enriched diagnostic, not silently succeed");
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
	[Description("Overrides an inherited column's caption in place on InheritedColumns, leaving uId/name/type unchanged and not moving it to own columns.")]
	public void ModifyColumn_ShouldOverrideInheritedCaptionInPlace_WhenModifyIsCaptionOnly() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [CreateTextColumn("Symptoms", NameColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrTickets",
			Action = "modify",
			ColumnName = "Symptoms",
			Title = "Description"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Should().NotBeNull(because: "a caption override of an inherited column should save the schema");
		_savedSchema.Columns.Should().NotContain(column => column.Name == "Symptoms",
			because: "the inherited column must not be redefined as an own column");
		EntitySchemaColumnDto inheritedColumn = _savedSchema.InheritedColumns.Single(column => column.Name == "Symptoms");
		inheritedColumn.UId.Should().Be(NameColumnUId, because: "the inherited column's uId must stay unchanged");
		inheritedColumn.DataValueType.Should().Be(1, because: "the inherited column's type must stay unchanged");
		EntitySchemaDesignerSupport.GetLocalizableValue(inheritedColumn.Caption, "en-US").Should().Be("Description",
			because: "the caption override must be applied in place on the inherited column");
	}

	[Test]
	[Description("Rejects a non-caption change to an inherited column and does not save the schema.")]
	public void ModifyColumn_ShouldThrow_WhenInheritedColumnMutationIsNotCaptionOnly() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [CreateTextColumn("Symptoms", NameColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrTickets",
			Action = "modify",
			ColumnName = "Symptoms",
			Required = true
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*inherited; only its caption and description can be overridden*",
				because: "name, type, and flags of an inherited column stay read-only");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Rejects removing an inherited column with a clear error and does not save the schema.")]
	public void ModifyColumn_ShouldThrow_WhenRemovingInheritedColumn() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [CreateTextColumn("Symptoms", NameColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrTickets",
			Action = "remove",
			ColumnName = "Symptoms"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*inherited and cannot be removed*",
				because: "an inherited column cannot be removed from a child schema");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Turns a silent no-op into a clear error when an inherited caption override is not reflected on readback.")]
	public void ModifyColumn_ShouldThrow_WhenInheritedCaptionOverrideNotPersisted() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [CreateTextColumn("Symptoms", NameColumnUId)], primaryDisplayColumn: null);
		// Simulate a server that ignores the inherited caption override: the reloaded inherited column keeps
		// its original caption, which the readback verification must catch.
		_designerClient.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(callInfo => {
				_savedSchema = callInfo.ArgAt<EntityDesignSchemaDto>(0);
				EntitySchemaColumnDto inherited = _savedSchema.InheritedColumns.Single(column => column.Name == "Symptoms");
				inherited.Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
					CultureName = "en-US",
					Value = "Symptoms"
				}];
				return new Clio.Command.EntitySchemaDesigner.SaveDesignItemDesignerResponse {
					Success = true,
					SchemaUId = _savedSchema.UId
				};
			});
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrTickets",
			Action = "modify",
			ColumnName = "Symptoms",
			Title = "Description"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*was not persisted*",
				because: "a caption override that the server did not persist must surface as a clear failure");
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

	private static readonly Guid AccountSchemaUId = Guid.Parse("99999999-9999-9999-9999-999999999999");
	private static readonly Guid MergedNameColumnUId = Guid.Parse("12121212-1212-1212-1212-121212121212");
	private static readonly Guid MergedContractColumnUId = Guid.Parse("34343434-3434-3434-3434-343434343434");
	private static readonly Guid MergedCreatedOnColumnUId = Guid.Parse("56565656-5656-5656-5656-565656565656");

	private static Clio.Common.EntitySchema.RuntimeEntitySchemaResult CreateMergedRuntimeSchema() =>
		new(
			UId: AccountSchemaUId,
			Name: "Account",
			Caption: "Account",
			Description: "Account schema",
			ParentUId: Guid.Parse("1bab9dcf-17d5-49f8-9536-8e0064f1dce0"),
			ExtendParent: true,
			IsDBView: false,
			IsTrackChangesInDB: true,
			IsVirtual: false,
			ShowInAdvancedMode: true,
			AdministratedByOperations: true,
			AdministratedByColumns: false,
			AdministratedByRecords: true,
			PrimaryColumnUId: MergedNameColumnUId,
			PrimaryDisplayColumnName: "Name",
			PrimaryDisplayColumnUId: MergedNameColumnUId,
			Columns: [
				new Clio.Common.EntitySchema.RuntimeEntitySchemaColumnResult(
					MergedNameColumnUId, "Name", "Name", null, 1, true, false, null, IsIndexed: true),
				new Clio.Common.EntitySchema.RuntimeEntitySchemaColumnResult(
					MergedContractColumnUId, "UsrColumn40", "Contract", null, 10, false, false, "Contract", IsIndexed: false),
				new Clio.Common.EntitySchema.RuntimeEntitySchemaColumnResult(
					MergedCreatedOnColumnUId, "CreatedOn", "Created on", null, 7, false, true, null, IsIndexed: false)
			]);

	[Test]
	[Description("Returns the merged effective column set, including custom columns from other packages, when no package is supplied.")]
	public void GetSchemaProperties_ReturnsMergedColumnsAcrossPackages_WhenPackageIsOmitted() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		result.Name.Should().Be("Account",
			because: "the merged read should preserve the runtime schema name");
		result.PackageName.Should().Be(RemoteEntitySchemaColumnManager.MergedSchemaPackageName,
			because: "the merged read is not scoped to one package and must signal that to the caller");
		result.Columns.Should().Contain(column => column.Name == "UsrColumn40" && column.Title == "Contract",
			because: "custom columns added in other packages must surface in the merged read so the agent does not conclude the field is missing");
		result.Columns!.First(column => column.Name == "UsrColumn40").Type.Should().Be("Lookup",
			because: "runtime data-value-type ids must be projected through the shared friendly type mapping");
		result.Columns.First(column => column.Name == "UsrColumn40").ReferenceSchemaName.Should().Be("Contract",
			because: "lookup reference schema names from the runtime payload must be preserved");
		result.Columns.Should().Contain(column => column.Name == "Name",
			because: "standard columns must remain present alongside customizations in a single response");
		result.OwnColumnCount.Should().Be(2,
			because: "non-inherited runtime columns should be counted as own columns");
		result.InheritedColumnCount.Should().Be(1,
			because: "inherited runtime columns should be counted separately");
		result.PrimaryColumnName.Should().Be("Name",
			because: "the primary column should be resolved from the runtime primary column identifier");
		_designerClient.DidNotReceiveWithAnyArgs().GetSchemaDesignItem(default, default);
	}

	[Test]
	[Description("Maps the per-column indexed flag and the own/inherited source from the runtime payload in the merged view.")]
	public void GetSchemaProperties_MapsColumnIndexedAndSource_WhenPackageIsOmitted() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		result.Columns!.First(column => column.Name == "Name").Indexed.Should().BeTrue(
			because: "the indexed flag must be projected from the runtime payload rather than hardcoded to false");
		result.Columns.First(column => column.Name == "UsrColumn40").Indexed.Should().BeFalse(
			because: "a non-indexed runtime column must report indexed=false");
		result.Columns.First(column => column.Name == "CreatedOn").Source.Should().Be("inherited",
			because: "runtime columns flagged IsInherited must be projected as the inherited source");
		result.Columns.First(column => column.Name == "Name").Source.Should().Be("own",
			because: "runtime columns not flagged IsInherited must be projected as the own source");
	}

	[Test]
	[Description("Maps the schema-level metadata exposed by the runtime endpoint in the merged view.")]
	public void GetSchemaProperties_MapsRuntimeSchemaLevelMetadata_WhenPackageIsOmitted() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		result.Title.Should().Be("Account",
			because: "the schema caption from the runtime payload must populate the title in the merged view");
		result.Description.Should().Be("Account schema",
			because: "the schema description from the runtime payload must be surfaced in the merged view");
		result.ExtendParent.Should().BeTrue(
			because: "extend-parent is exposed by the runtime endpoint and must be mapped");
		result.TrackChangesInDb.Should().BeTrue(
			because: "track-changes-in-db is exposed by the runtime endpoint and must be mapped");
		result.Virtual.Should().BeFalse(
			because: "the runtime payload reports a non-virtual schema");
		result.ShowInAdvancedMode.Should().BeTrue(
			because: "show-in-advanced-mode is exposed by the runtime endpoint and must be mapped");
		result.AdministratedByOperations.Should().BeTrue(
			because: "administration-by-operations is exposed by the runtime endpoint and must be mapped");
		result.AdministratedByRecords.Should().BeTrue(
			because: "administration-by-records is exposed by the runtime endpoint and must be mapped");
	}

	[Test]
	[Description("Reports null for the schema-level fields the by-name runtime endpoint does not expose, so a consumer can distinguish unavailable from a genuine value in the merged view.")]
	public void GetSchemaProperties_ReportsNullForUnavailableFields_WhenPackageIsOmitted() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		result.ParentSchemaName.Should().BeNull(
			because: "the by-name runtime endpoint exposes only the parent UId, so the parent name stays unresolved in the merged view");
		result.IndexesCount.Should().BeNull(
			because: "the by-name runtime endpoint does not return the index collection, so the count is null (unavailable) rather than a misleading zero");
		result.SspAvailable.Should().BeNull(
			because: "ssp-available is not exposed by the by-name runtime endpoint, so it must be null to avoid a false negative against the single-package read");
		result.UseRecordDeactivation.Should().BeNull(
			because: "use-record-deactivation is not exposed by the by-name runtime endpoint, so it must be null rather than a plausible false");
		result.UseDenyRecordRights.Should().BeNull(
			because: "use-deny-record-rights is not exposed by the by-name runtime endpoint, so it must be null rather than a plausible false");
		result.UseLiveEditing.Should().BeNull(
			because: "use-live-editing is not exposed by the by-name runtime endpoint, so it must be null rather than a plausible false");
	}

	[Test]
	[Description("Leaves the primary column name null in the merged view when the runtime primary column UId matches no returned column.")]
	public void GetSchemaProperties_LeavesPrimaryColumnNameNull_WhenPrimaryColumnUIdMatchesNoColumn() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(new Clio.Common.EntitySchema.RuntimeEntitySchemaResult(
			UId: AccountSchemaUId,
			Name: "Account",
			PrimaryColumnUId: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
			PrimaryDisplayColumnName: null,
			PrimaryDisplayColumnUId: null,
			Columns: [
				new Clio.Common.EntitySchema.RuntimeEntitySchemaColumnResult(
					MergedNameColumnUId, "Name", "Name", null, 1, true, false, null, IsIndexed: true)
			]));

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		result.PrimaryColumnName.Should().BeNull(
			because: "an unresolved primary column UId must surface as a null name rather than throw on the FirstOrDefault lookup");
		result.Columns.Should().ContainSingle(column => column.Name == "Name",
			because: "the rest of the merged projection must still be returned when the primary column cannot be resolved");
	}

	[Test]
	[Description("Returns zero own and inherited counts and an empty column list when the runtime schema has no columns.")]
	public void GetSchemaProperties_ReturnsEmptyCounts_WhenRuntimeSchemaHasNoColumns() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(new Clio.Common.EntitySchema.RuntimeEntitySchemaResult(
			UId: AccountSchemaUId,
			Name: "Account",
			PrimaryColumnUId: Guid.Empty,
			PrimaryDisplayColumnName: null,
			PrimaryDisplayColumnUId: null,
			Columns: []));

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		result.Columns.Should().BeEmpty(
			because: "an empty runtime column set must project to an empty column list, not throw");
		result.OwnColumnCount.Should().Be(0,
			because: "the own/inherited counting boundary must hold at zero columns");
		result.InheritedColumnCount.Should().Be(0,
			because: "the own/inherited counting boundary must hold at zero columns");
	}

	[TestCase(null, TestName = "GetSchemaProperties_RoutesToMergedRead_WhenPackageIsNull")]
	[TestCase("", TestName = "GetSchemaProperties_RoutesToMergedRead_WhenPackageIsEmpty")]
	[TestCase("   ", TestName = "GetSchemaProperties_RoutesToMergedRead_WhenPackageIsWhitespace")]
	[Description("Routes to the merged runtime read whenever the package is null, empty, or whitespace.")]
	public void GetSchemaProperties_RoutesToMergedRead_WhenPackageIsBlank(string package) {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		_manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = package,
			SchemaName = "Account"
		});

		// Assert
		_runtimeEntitySchemaReader.Received(1).GetByName("Account");
		_designerClient.DidNotReceiveWithAnyArgs().GetSchemaDesignItem(default, default);
	}

	[Test]
	[Description("Trims the schema name before delegating the merged read to the runtime reader.")]
	public void GetSchemaProperties_TrimsSchemaName_WhenPackageIsOmitted() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		_manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "  Account  "
		});

		// Assert
		_runtimeEntitySchemaReader.Received(1).GetByName("Account");
	}

	[Test]
	[Description("Translates a runtime reader failure into the domain exception so the merged read surfaces a uniform exception type.")]
	public void GetSchemaProperties_ThrowsEntitySchemaDesignerException_WhenRuntimeReaderFails() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account")
			.Returns(_ => throw new InvalidOperationException("Runtime schema 'Account' was not returned by Creatio."));

		// Act
		Action act = () => _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "low-level runtime reader failures must be translated to the domain exception used by the single-package path")
			.WithMessage("*was not returned by Creatio*",
				because: "the original failure message must be preserved for diagnostics");
	}

	private static IEnumerable<TestCaseData> RuntimeReaderTransportFailures() {
		yield return new TestCaseData(new System.Net.Http.HttpRequestException("network blip")).SetName(
			"GetSchemaProperties_TranslatesHttpRequestException_WhenPackageIsOmitted");
		yield return new TestCaseData(new System.Text.Json.JsonException("unexpected HTML error page")).SetName(
			"GetSchemaProperties_TranslatesJsonException_WhenPackageIsOmitted");
		yield return new TestCaseData(new System.Threading.Tasks.TaskCanceledException("request timed out")).SetName(
			"GetSchemaProperties_TranslatesTaskCanceledException_WhenPackageIsOmitted");
	}

	[TestCaseSource(nameof(RuntimeReaderTransportFailures))]
	[Description("Translates realistic transport/parse failures from the runtime reader into the domain exception, because the MCP merged-read path does not have the BaseTool catch-all to normalize raw faults.")]
	public void GetSchemaProperties_TranslatesTransportAndParseFailures_WhenPackageIsOmitted(Exception readerFailure) {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(_ => throw readerFailure);

		// Act
		Action act = () => _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "transport and parse faults must reach the MCP surface as a normalized domain exception, not an unstructured fault")
			.WithInnerException(readerFailure.GetType(),
				because: "the original failure must be preserved as the inner exception for diagnostics");
	}

	[Test]
	[Description("Reads the single package layer slice through the designer client when a package is supplied.")]
	public void GetSchemaProperties_ReadsSinglePackageLayer_WhenPackageIsSupplied() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateTextColumn("Name", NameColumnUId)]);
		SetupLoadedSchema();

		// Act
		EntitySchemaPropertiesInfo result = _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		});

		// Assert
		result.PackageName.Should().Be("UsrPkg",
			because: "a package-scoped read must report the requested package, not the merged-all-packages label");
		_designerClient.ReceivedWithAnyArgs(1).TryGetSchemaDesignItem(default, default);
		_runtimeEntitySchemaReader.DidNotReceiveWithAnyArgs().GetByName(default);
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
	[Description("Enriches a lookup Const default readback with the referenced record display value resolved by the resolver.")]
	public void GetColumnProperties_ShouldEnrichLookupConstDefault_WhenColumnIsLookupConst() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		EntitySchemaColumnDto colorColumn = CreateLookupColumn("UsrColor", NameColumnUId, "UsrEng91318Color");
		colorColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.Const,
			Value = recordId.ToString("D")
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), colorColumn]);
		SetupLoadedSchema();
		_lookupDefaultDisplayValueResolver
			.Resolve("UsrEng91318Color", recordId, Arg.Any<RemoteCommandOptions>())
			.Returns(new LookupDefaultResolution("Green", null));

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "UsrColor"
		});

		// Assert
		result.DefaultValueConfig!.DisplayValue.Should().Be("Green",
			because: "a lookup Const default must be enriched with the referenced record display value so an agent can verify it");
		result.DefaultValueConfig.RecordResolution.Should().BeNull(
			because: "no marker is emitted when the display value resolves successfully");
		_lookupDefaultDisplayValueResolver.Received(1).Resolve("UsrEng91318Color", recordId, Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Does not invoke the lookup-default resolver for a non-lookup Const default, leaving the readback GUID/value-only.")]
	public void GetColumnProperties_ShouldNotEnrich_WhenConstDefaultIsNotLookup() {
		// Arrange
		EntitySchemaColumnDto nameColumn = CreateTextColumn("Name", NameColumnUId);
		nameColumn.DataValueType = 27;
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
		result.DefaultValueConfig!.DisplayValue.Should().BeNull(
			because: "enrichment applies only to lookup columns, not to a text Const default");
		_lookupDefaultDisplayValueResolver.DidNotReceive()
			.Resolve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Propagates the record-resolution marker to readback when a lookup Const default's display value cannot be resolved.")]
	public void GetColumnProperties_ShouldPropagateRecordResolutionMarker_WhenDisplayValueUnavailable() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		EntitySchemaColumnDto colorColumn = CreateLookupColumn("UsrColor", NameColumnUId, "UsrEng91318Color");
		colorColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.Const,
			Value = recordId.ToString("D")
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), colorColumn]);
		SetupLoadedSchema();
		_lookupDefaultDisplayValueResolver
			.Resolve("UsrEng91318Color", recordId, Arg.Any<RemoteCommandOptions>())
			.Returns(new LookupDefaultResolution(null, "no-access"));

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "UsrColor"
		});

		// Assert
		result.DefaultValueConfig!.RecordResolution.Should().Be("no-access",
			because: "an unresolved display value must surface its honest marker end to end through the readback");
		result.DefaultValueConfig.DisplayValue.Should().BeNull(
			because: "no display value is available when a marker is returned");
	}

	[Test]
	[Description("Does not enrich a lookup Const default whose stored value is not a GUID.")]
	public void GetColumnProperties_ShouldNotEnrich_WhenLookupConstValueIsNotGuid() {
		// Arrange
		EntitySchemaColumnDto colorColumn = CreateLookupColumn("UsrColor", NameColumnUId, "UsrEng91318Color");
		colorColumn.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = EntitySchemaColumnDefSource.Const,
			Value = "not-a-guid"
		};
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), colorColumn]);
		SetupLoadedSchema();

		// Act
		EntitySchemaColumnPropertiesInfo result = _manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "UsrColor"
		});

		// Assert
		result.DefaultValueConfig!.DisplayValue.Should().BeNull(
			because: "a non-GUID Const value cannot identify a referenced record, so enrichment is skipped");
		_lookupDefaultDisplayValueResolver.DidNotReceive()
			.Resolve(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
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
		savedColumn.DefValue.Should().NotBeNull(
			because: "clearing must send an explicit None default-value DTO; a null DefValue is silently preserved (left as the stale Const) by the EntitySchemaDesigner server");
		savedColumn.DefValue!.ValueSourceType.Should().Be(EntitySchemaColumnDefSource.None,
			because: "an explicit None ValueSourceType is the marker the server honors to drop the previously persisted default");
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

	[Test]
	[Description("Adds an ImageLookup ('Image link') column that auto-references the SysImage schema and is indexed, so crt.ImageInput can read and write it.")]
	public void ModifyColumn_AddsImageLookupColumn_ReferencesSysImageAndIndexes() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrPhoto",
			Type = "ImageLookup",
			Title = "Photo"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrPhoto");
		addedColumn.DataValueType.Should().Be(16,
			because: "ImageLookup and its ImageLink alias must map to the platform 'Image link' data value type 16");
		addedColumn.ReferenceSchema.Should().NotBeNull(
			because: "ImageLookup columns are reference columns and must carry a reference schema");
		addedColumn.ReferenceSchema.Name.Should().Be("SysImage",
			because: "ImageLookup columns reference the platform SysImage image-storage schema");
		addedColumn.ReferenceSchema.UId.Should().Be(Guid.Parse("93986bfe-2dbd-46bc-9bf9-d03dfefbf3b8"),
			because: "the server persists ReferenceSchema.UId, so clio must supply the SysImage schema UId");
		addedColumn.Indexed.Should().BeTrue(
			because: "ImageLookup columns are indexed, mirroring the platform entity designer");
		_savedSchema.PrimaryDisplayColumn.Should().BeNull(
			because: "an image-link column must not be promoted to the primary display column");
	}

	[Test]
	[Description("Forces Indexed=true for an ImageLookup column even when the caller explicitly passes Indexed=false, because the platform requires image-link columns to be indexed.")]
	public void ModifyColumn_AddsImageLookupColumn_ForcesIndexed_EvenWhenCallerPassesIndexedFalse() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrPhoto",
			Type = "ImageLookup",
			Title = "Photo",
			Indexed = false
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto addedColumn = _savedSchema.Columns.Single(column => column.Name == "UsrPhoto");
		addedColumn.Indexed.Should().BeTrue(
			because: "the ImageLookup indexed invariant must override an explicit Indexed=false from the caller");
	}

	[Test]
	[Description("Rejects a caller-supplied reference schema for ImageLookup because the reference is always the implicit SysImage schema.")]
	public void ModifyColumn_Throws_WhenImageLookupSuppliesReferenceSchema() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)]);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrPhoto",
			Type = "ImageLookup",
			ReferenceSchemaName = "Contact"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*reference the SysImage schema automatically*",
				because: "ImageLookup columns must not accept a caller-supplied reference schema");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Switches an existing column to ImageLookup and attaches the implicit SysImage reference so the column works with crt.ImageInput.")]
	public void ModifyColumn_UpdatesOwnColumn_ToImageLookup_ReferencesSysImage() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), CreateTextColumn("Payload", NameColumnUId)],
			primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Payload",
			Type = "ImageLookup"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		EntitySchemaColumnDto savedColumn = _savedSchema.Columns.Single(column => column.Name == "Payload");
		savedColumn.DataValueType.Should().Be(16,
			because: "modify flows should switch the column to the ImageLookup runtime type");
		savedColumn.ReferenceSchema.Name.Should().Be("SysImage",
			because: "switching to ImageLookup must attach the implicit SysImage reference");
		savedColumn.Indexed.Should().BeTrue(
			because: "ImageLookup columns are indexed after the type switch");
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
	[Description("Rejects adding a lookup column whose Const default points at a record missing from the referenced schema, before the schema is saved (DRAFT-AC-06).")]
	public void ModifyColumn_Throws_WhenAddLookupConstDefaultRecordMissing() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)]);
		SetupLoadedSchema();
		Guid missingRecordId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
		_designerClient
			.CheckRecordExists("Contact", missingRecordId, Arg.Any<RemoteCommandOptions>())
			.Returns(LookupRecordExistence.NotFound);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "add",
			ColumnName = "UsrOwner",
			Type = "Lookup",
			Title = "Owner",
			ReferenceSchemaName = "Contact",
			DefaultValueSource = "Const",
			DefaultValue = missingRecordId.ToString("D")
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*was not found in referenced schema*",
				because: "an added lookup column must validate its Const default against the referenced schema before saving, which requires the reference schema to be resolved before ApplyDefaultValue runs (DRAFT-AC-06)");
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

	[Test]
	[Description("Renders the synthetic merged package label and the unresolved parent placeholder when the package is omitted.")]
	public void PrintSchemaProperties_RendersMergedLabel_WhenPackageIsOmitted() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName("Account").Returns(CreateMergedRuntimeSchema());

		// Act
		_manager.PrintSchemaProperties(new GetEntitySchemaPropertiesOptions {
			SchemaName = "Account"
		});

		// Assert
		List<string> loggedMessages = _logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteInfo))
			.Select(call => (string)call.GetArguments()[0]!)
			.ToList();
		loggedMessages.Should().Contain($"Package: {RemoteEntitySchemaColumnManager.MergedSchemaPackageName}",
			because: "the merged read must signal that it is not scoped to a single package via the synthetic package label");
		loggedMessages.Should().Contain("Parent schema: <none>",
			because: "the parent schema name is not exposed by the by-name runtime endpoint, so the merged read renders the unresolved placeholder");
	}

	[Test]
	[Description("Auto-resolves missing package dependencies and retries LoadSchema when the schema is initially unavailable on a write path (ENG-91314).")]
	public void ModifyColumn_ShouldAutoResolveDependenciesAndRetry_WhenSchemaIsInitiallyUnavailable() {
		// Arrange — first TryGetSchemaDesignItem returns null schema; after auto-resolve, second call succeeds.
		int tryGetCallCount = 0;
		_designerClient.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(_ => {
				tryGetCallCount++;
				if (tryGetCallCount == 1) {
					return new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> { Success = false, Schema = null };
				}
				return new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> { Success = true, Schema = _loadedSchema };
			});
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		_dependencyResolver.TryAutoResolve("UsrVehicle", "UsrPkg").Returns(true);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle",
			Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		_manager.ModifyColumn(options);

		// Assert
		_savedSchema.Should().NotBeNull(
			because: "auto-resolve succeeded and the retry completed the mutation");
		_dependencyResolver.Received(1).TryAutoResolve("UsrVehicle", "UsrPkg");
	}

	[Test]
	[Description("Falls through to GetSchemaDesignItem (the enriched error path) when auto-resolve finds no candidate and the schema stays unavailable (ENG-91314).")]
	public void ModifyColumn_ShouldThrowEnrichedError_WhenAutoResolveReturnsFalse() {
		// Arrange
		SetupUnavailableSchema();
		_dependencyResolver.TryAutoResolve("UsrVehicle", "UsrPkg").Returns(false);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle",
			Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "when auto-resolve fails, the enriched error from GetSchemaDesignItem must propagate")
			.WithMessage("*not available in package*",
				because: "the re-issue must reach GetSchemaDesignItem so the enriched missing-dependency diagnostic surfaces, not the generic 'returned no schema' fallback");
		_designerClient.ReceivedWithAnyArgs(1).GetSchemaDesignItem(default, default);
	}

	[Test]
	[Description("Falls through to GetSchemaDesignItem when auto-resolve adds a dependency but the schema remains inaccessible on retry (ENG-91314).")]
	public void ModifyColumn_ShouldThrowEnrichedError_WhenRetryAfterAutoResolveStillFails() {
		// Arrange — every call returns null schema.
		SetupUnavailableSchema();
		_dependencyResolver.TryAutoResolve("UsrVehicle", "UsrPkg").Returns(true);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle",
			Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		Action act = () => _manager.ModifyColumn(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "even after adding the dependency, a still-inaccessible schema must surface the enriched diagnostic")
			.WithMessage("*not available in package*",
				because: "a retry that still yields a null schema must fall through to GetSchemaDesignItem, not emit the generic 'returned no schema' message");
		_designerClient.ReceivedWithAnyArgs(1).GetSchemaDesignItem(default, default);
	}

	[Test]
	[Description("Falls through to the enriched GetSchemaDesignItem re-issue when the server replies Success=true but carries a null schema, so the missing-dependency diagnostic is not lost to the generic fallback (ENG-91314).")]
	public void GetSchemaProperties_ShouldThrowEnrichedError_WhenResponseIsSuccessfulButSchemaIsNull() {
		// Arrange — TryGetSchemaDesignItem returns a successful envelope whose Schema is null (a non-HTML "unavailable"
		// signal); the gate must key off schema availability, not response nullity, and re-issue the enriched call.
		_designerClient.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(_ => new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true, Schema = null
			});
		_designerClient.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns<Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto>>(_ =>
				throw new EntitySchemaDesignerException("Schema 'UsrVehicle' is not available in package 'UsrPkg'."));

		// Act
		Action act = () => _manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		});

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "a Success=true response with a null schema is still 'schema unavailable' and must surface the enriched diagnostic")
			.WithMessage("*not available in package*",
				because: "gating the re-issue on schema availability (not response==null) keeps the missing-dependency guidance that is the whole point of the feature");
		_designerClient.ReceivedWithAnyArgs(1).GetSchemaDesignItem(default, default);
	}

	[Test]
	[Description("Read-only GetColumnProperties does not trigger dependency auto-resolution because read paths must never mutate package state (ENG-91314).")]
	public void GetColumnProperties_ShouldNotTriggerDependencyResolution_WhenSchemaIsAvailable() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), CreateTextColumn("Name", NameColumnUId)]);
		SetupLoadedSchema();

		// Act
		_manager.GetColumnProperties(new GetEntitySchemaColumnPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Name"
		});

		// Assert
		_dependencyResolver.DidNotReceive().TryAutoResolve(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Read-only GetSchemaProperties does not trigger dependency auto-resolution because read paths must never mutate package state (ENG-91314).")]
	public void GetSchemaProperties_ShouldNotTriggerDependencyResolution_WhenSchemaIsAvailable() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateTextColumn("Name", NameColumnUId)]);
		SetupLoadedSchema();

		// Act
		_manager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		});

		// Assert
		_dependencyResolver.DidNotReceive().TryAutoResolve(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Sets the primary-display column to an own column resolved by name and matched by its uId.")]
	public void SetSchemaProperties_ShouldSetPrimaryDisplayColumn_WhenColumnIsOwn() {
		// Arrange
		EntitySchemaColumnDto captionColumn = CreateTextColumn("Caption", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), captionColumn],
			primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new SetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "Caption"
		};

		// Act
		_manager.SetSchemaProperties(options);

		// Assert
		_savedSchema.Should().NotBeNull(because: "setting a schema property should save the schema");
		_savedSchema.PrimaryDisplayColumn.Should().NotBeNull(
			because: "the resolved own column should be assigned as the primary display column");
		_savedSchema.PrimaryDisplayColumn!.Name.Should().Be("Caption",
			because: "the primary-display column should be the column named in the request");
		_savedSchema.PrimaryDisplayColumn.UId.Should().Be(NameColumnUId,
			because: "the modern designer contract matches the primary-display column by its uId");
	}

	[Test]
	[Description("Resolves the primary-display target against inherited columns when it is not an own column.")]
	public void SetSchemaProperties_ShouldSetPrimaryDisplayColumn_WhenColumnIsInherited() {
		// Arrange
		EntitySchemaColumnDto subjectColumn = CreateTextColumn("Subject", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)],
			inheritedColumns: [subjectColumn], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new SetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "Subject"
		};

		// Act
		_manager.SetSchemaProperties(options);

		// Assert
		_savedSchema.PrimaryDisplayColumn!.Name.Should().Be("Subject",
			because: "an inherited column is a valid primary-display target and is resolved after own columns");
		_savedSchema.PrimaryDisplayColumn.UId.Should().Be(NameColumnUId,
			because: "the inherited column must be matched by its uId so the server links the right column");
	}

	[Test]
	[Description("Throws and does not save when the named primary-display column does not exist on the schema.")]
	public void SetSchemaProperties_ShouldThrow_WhenColumnNotFound() {
		// Arrange
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId)], primaryDisplayColumn: null);
		SetupLoadedSchema();
		var options = new SetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "Ghost"
		};

		// Act
		Action act = () => _manager.SetSchemaProperties(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*was not found in schema*",
				because: "naming a column absent from the schema must be rejected");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Throws before any load or save when no settable schema property is supplied.")]
	public void SetSchemaProperties_ShouldThrow_WhenNoPropertyIsSupplied() {
		// Arrange
		var options = new SetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = null
		};

		// Act
		Action act = () => _manager.SetSchemaProperties(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*No schema property to set*",
				because: "the setter must reject a no-op request that would set nothing");
		_designerClient.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(),
			Arg.Any<Clio.Command.RemoteCommandOptions>());
	}

	[Test]
	[Description("Turns a silent server no-op into a clear error when the primary-display column is not persisted on readback.")]
	public void SetSchemaProperties_ShouldThrow_WhenReadbackDoesNotReflectPrimaryDisplayColumn() {
		// Arrange
		EntitySchemaColumnDto captionColumn = CreateTextColumn("Caption", NameColumnUId);
		_loadedSchema = CreateSchema(columns: [CreateGuidColumn("Id", IdColumnUId), captionColumn],
			primaryDisplayColumn: null);
		// Simulate a target version that ignores the primary-display set: the saved+reloaded schema comes
		// back with no primary-display column, which the readback verification must catch.
		_designerClient.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(callInfo => {
				_savedSchema = callInfo.ArgAt<EntityDesignSchemaDto>(0);
				_savedSchema.PrimaryDisplayColumn = null;
				return new Clio.Command.EntitySchemaDesigner.SaveDesignItemDesignerResponse {
					Success = true,
					SchemaUId = _savedSchema.UId
				};
			});
		SetupLoadedSchema();
		var options = new SetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "Caption"
		};

		// Act
		Action act = () => _manager.SetSchemaProperties(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*was not persisted*",
				because: "a silent no-op on an unsupported target must surface as a clear failure, not a false success");
	}

	private void SetupLoadedSchema() {
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> MakeResponse() =>
			new() { Success = true, Schema = _savedSchema ?? _loadedSchema };
		_designerClient.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(_ => MakeResponse());
		_designerClient.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(_ => MakeResponse());
	}

	// Simulates a schema that is not accessible in the target package: TryGetSchemaDesignItem returns a
	// response with a null Schema (the same signal LoadSchema treats as "unavailable"), and the direct
	// GetSchemaDesignItem re-issue throws the enriched diagnostic — mirroring the production HTML-page path.
	private void SetupUnavailableSchema() {
		_designerClient.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns(_ => new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = false, Schema = null
			});
		_designerClient.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<Clio.Command.RemoteCommandOptions>())
			.Returns<Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto>>(_ =>
				throw new EntitySchemaDesignerException("Schema 'UsrVehicle' is not available in package 'UsrPkg'."));
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
