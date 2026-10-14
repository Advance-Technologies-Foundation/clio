using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.Localization;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using DesignerResponse = Clio.Command.EntitySchemaDesigner.DesignerResponse<Clio.Command.EntitySchemaDesigner.EntityDesignSchemaDto>;
using LocalizableStringDto = Clio.Command.EntitySchemaDesigner.LocalizableStringDto;
using SaveDesignItemDesignerResponse = Clio.Command.EntitySchemaDesigner.SaveDesignItemDesignerResponse;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal sealed class RemoteEntitySchemaInheritedLookupOverrideTests : BaseClioModuleTests {
	private static readonly Guid PackageUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid BaseItemInFolderUId = Guid.Parse("4f63bafb-e9e7-4082-b92e-66b97c14017c");
	private static readonly Guid BaseFolderUId = Guid.Parse("d602bf96-d029-4b07-9755-63c8f5cb5ed5");
	private static readonly Guid ContactFolderUId = Guid.Parse("8b5c01a2-59e9-40dc-855b-6e3bebddc6ee");
	private static readonly Guid ContactUId = Guid.Parse("16be3651-8fe2-4159-8dd0-a803d4683dd3");
	private static readonly Guid BaseEntityUId = Guid.Parse("1bab9dcf-17d5-49f8-9536-8e0064f1dce0");
	private static readonly Guid UnresolvableRootUId = Guid.Parse("1b56b061-4e91-4974-9038-df8340e534f2");
	private static readonly Guid FolderColumnUId = Guid.Parse("9e46bb69-9788-423c-b7cd-4c624081082d");
	private const string FolderOverride =
		"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"ContactFolder\"}";

	private IRemoteEntitySchemaDesignerClient _designer = null!;
	private IRetryDelay _retryDelay = null!;
	private FindEntitySchemaCommand _finder = null!;
	private IApplicationPackageListProvider _packages = null!;
	private IRemoteEntitySchemaCreator _creator = null!;
	private EntityDesignSchemaDto _schema = null!;
	private List<EntityDesignSchemaDto> _saved = null!;
	private bool _parentIsVirtual;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_designer = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_retryDelay = Substitute.For<IRetryDelay>();
		services.AddTransient(_ => _retryDelay);
		_finder = Substitute.For<FindEntitySchemaCommand>(Substitute.For<IApplicationClient>(),
			Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());
		_packages = Substitute.For<IApplicationPackageListProvider>();
		IEntitySchemaCaptionCultureResolver captions = Substitute.For<IEntitySchemaCaptionCultureResolver>();
		captions.ResolveEffectiveCulture(Arg.Any<RemoteCommandOptions>(), Arg.Any<string>()).Returns("en-US");
		services.AddTransient(_ => _designer);
		services.AddTransient(_ => _finder);
		services.AddTransient(_ => _packages);
		services.AddTransient(_ => captions);
		services.AddTransient(_ => Substitute.For<IEntitySchemaPublisher>());
		ICreatioCultureCatalog cultures = Substitute.For<ICreatioCultureCatalog>();
		cultures.GetCultures().Returns([new CreatioCulture("en-US", true), new CreatioCulture("de-DE", true)]);
		services.AddTransient(_ => cultures);
	}

	public override void Setup() {
		base.Setup();
		_creator = Container.GetRequiredService<IRemoteEntitySchemaCreator>();
		_packages.GetPackages().Returns([new PackageInfo(new PackageDescriptor {
			Name = "UsrPkg", UId = PackageUId
		}, string.Empty, [])]);
		_finder.FindSchemas(Arg.Any<FindEntitySchemaOptions>()).Returns([]);
		_parentIsVirtual = true;
		_saved = [];
		_schema = new EntityDesignSchemaDto { UId = Guid.NewGuid(), Columns = [], InheritedColumns = [], Indexes = [] };
		_designer.CreateNewSchema(Arg.Any<CreateEntitySchemaRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(call => {
				_schema.ExtendParent = call.Arg<CreateEntitySchemaRequestDto>().ExtendParent;
				return new DesignerResponse { Schema = _schema };
			});
		_designer.CheckUniqueSchemaName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new BoolResponse { Success = true, Value = true });
		_designer.GetAvailableParentSchemas(Arg.Any<GetAvailableSchemasRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new AvailableEntitySchemasResponse { Items = [
				new ManagerItemDto { UId = BaseItemInFolderUId, Name = "BaseItemInFolder" },
				new ManagerItemDto { UId = Guid.NewGuid(), Name = "UsrVehicleInFolder" }
			] });
		_designer.AssignParentSchema(Arg.Any<AssignParentSchemaRequestDto<EntityDesignSchemaDto>>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(_ => {
				_schema.ParentSchema = new EntityDesignSchemaDto {
					UId = BaseItemInFolderUId, Name = "BaseItemInFolder", IsVirtual = _parentIsVirtual
				};
				_schema.InheritedColumns = CreateInheritedColumns();
				return new DesignerResponse { Schema = _schema };
			});
		_designer.GetAvailableReferenceSchemas(Arg.Any<GetAvailableSchemasRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(call => new AvailableEntitySchemasResponse {
				Items = call.Arg<GetAvailableSchemasRequestDto>().AllowVirtual
					? [.. NonVirtualReferenceSchemas(), new ManagerItemDto { UId = BaseFolderUId, Name = "BaseFolder" }]
					: NonVirtualReferenceSchemas()
			});
		_designer.GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>())
			.Returns(call => new RuntimeEntitySchemaResponse {
				Success = true,
				Schema = new RuntimeEntitySchemaDto {
					UId = call.Arg<Guid>(),
					Name = "UsrVehicleInFolder",
					ParentUId = ParentOf(call.Arg<Guid>())
				}
			});
		_designer.GetRuntimeEntitySchema(UnresolvableRootUId, Arg.Any<RemoteCommandOptions>())
			.Returns<RuntimeEntitySchemaResponse>(_ =>
				throw new InvalidOperationException($"Can't find EntitySchema by UId: {UnresolvableRootUId}"));
		_designer.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(call => {
				_saved.Add(call.Arg<EntityDesignSchemaDto>());
				return new SaveDesignItemDesignerResponse { SchemaUId = _schema.UId };
			});
		_designer.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(_ => new DesignerResponse { Schema = _schema });
	}

	private static ManagerItemDto[] NonVirtualReferenceSchemas() => [
		new ManagerItemDto { UId = ContactFolderUId, Name = "ContactFolder", Caption = "Contact folder" },
		new ManagerItemDto { UId = ContactUId, Name = "Contact", Caption = "Contact" }
	];

	private static Guid ParentOf(Guid schemaUId) =>
		schemaUId == ContactFolderUId ? BaseFolderUId
		: schemaUId == ContactUId ? BaseEntityUId
		: schemaUId == BaseEntityUId ? UnresolvableRootUId
		: Guid.Empty;

	private static List<EntitySchemaColumnDto> CreateInheritedColumns() => [
		new EntitySchemaColumnDto {
			UId = Guid.NewGuid(), Name = "Id", DataValueType = 0,
			Caption = [new LocalizableStringDto { CultureName = "en-US", Value = "Id" }]
		},
		new EntitySchemaColumnDto {
			UId = Guid.NewGuid(), Name = "Notes", DataValueType = 1,
			Caption = [new LocalizableStringDto { CultureName = "en-US", Value = "Notes" }]
		},
		new EntitySchemaColumnDto {
			UId = FolderColumnUId, Name = "Folder", DataValueType = 10,
			Caption = [new LocalizableStringDto { CultureName = "en-US", Value = "Folder" }],
			ReferenceSchema = new EntityDesignSchemaDto { UId = BaseFolderUId, Name = "BaseFolder" }
		}
	];

	private static CreateEntitySchemaOptions CreateOptions(params string[] columns) => new() {
		Package = "UsrPkg",
		SchemaName = "UsrVehicleInFolder",
		Title = "Vehicle in folder",
		ParentSchemaName = "BaseItemInFolder",
		Columns = columns
	};

	private EntitySchemaColumnDto SavedFolder() =>
		_saved.Single().InheritedColumns.Single(column => column.Name == "Folder");

	[Test]
	[Description("Creates a BaseItemInFolder child whose inherited Folder references ContactFolder in the same save, with no own column and the inherited caption unchanged.")]
	public void Create_ShouldRedirectInheritedFolder_WhenOverrideColumnIsSupplied() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		_creator.Create(options);

		// Assert
		_saved.Should().ContainSingle(because: "the override is applied in the single SaveSchema of the create");
		_saved[0].Columns.Should().BeEmpty(because: "an override must not add an own column");
		EntitySchemaColumnDto folder = SavedFolder();
		folder.UId.Should().Be(FolderColumnUId, because: "the inherited column keeps its identity");
		folder.ReferenceSchema.UId.Should().Be(ContactFolderUId, because: "the reference must point at ContactFolder");
		folder.ReferenceSchema.Name.Should().Be("ContactFolder", because: "the reference name must match the request");
		EntitySchemaDesignerSupport.GetLocalizableValue(folder.Caption, "en-US").Should().Be("Folder",
			because: "an override without a title must keep the inherited caption");
	}

	[Test]
	[Description("Applies title-localizations sent with an override as the inherited column caption.")]
	public void Create_ShouldOverrideInheritedCaption_WhenOverrideCarriesTitleLocalizations() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"ContactFolder\"," +
			"\"title-localizations\":{\"en-US\":\"Contact folder\",\"de-DE\":\"Kontaktordner\"}}");

		// Act
		_creator.Create(options);

		// Assert
		EntitySchemaColumnDto folder = SavedFolder();
		EntitySchemaDesignerSupport.GetLocalizableValue(folder.Caption, "en-US").Should().Be("Contact folder",
			because: "the requested en-US caption must be written on the inherited column");
		EntitySchemaDesignerSupport.GetLocalizableValue(folder.Caption, "de-DE").Should().Be("Kontaktordner",
			because: "every requested culture must be written on the inherited column");
	}

	[Test]
	[Description("Refuses a column named like an inherited column without override before SaveSchema, naming BaseItemInFolder and the override form.")]
	public void Create_ShouldRefuseInheritedName_WhenOverrideIsAbsent() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"type\":\"Lookup\",\"reference-schema-name\":\"ContactFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a duplicate of an inherited column cannot be saved")
			.WithMessage("*'Folder' is inherited*parent schema 'BaseItemInFolder'*\"override\":true*",
				because: "the error must name the parent and show the override form");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses an override of an inherited column that is not a lookup.")]
	public void Create_ShouldRefuseOverride_WhenInheritedColumnIsNotLookup() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Notes\",\"override\":true,\"reference-schema-name\":\"ContactFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "only an inherited lookup has a reference to override")
			.WithMessage("*only the reference schema of an inherited Lookup column can be overridden*");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses an override of a column the parent schema does not have.")]
	public void Create_ShouldRefuseOverride_WhenParentHasNoSuchColumn() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"UsrFolder\",\"override\":true,\"reference-schema-name\":\"ContactFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "an override needs an inherited column")
			.WithMessage("*Parent schema 'BaseItemInFolder'*has no column 'UsrFolder'*");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses an override when the parent schema is not virtual, because the platform drops the change.")]
	public void Create_ShouldRefuseOverride_WhenParentIsNotVirtual() {
		// Arrange
		_parentIsVirtual = false;
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "a non-virtual parent silently drops the override")
			.WithMessage("*Parent schema 'BaseItemInFolder'*is not virtual*");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses an override on a schema that replaces its parent, because the platform drops the change.")]
	public void Create_ShouldRefuseOverride_WhenSchemaReplacesParent() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);
		options.SchemaName = "UsrVehicleInFolder";
		options.ParentSchemaName = "UsrVehicleInFolder";
		options.ExtendParent = true;

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "a replacement schema silently drops the override")
			.WithMessage("*replaces its parent*");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses Contact as the Folder target because it does not inherit from BaseFolder, ending the walk at the root whose parent the runtime service cannot resolve; ContactFolder passes.")]
	public void Create_ShouldRefuseOverride_WhenTargetDoesNotInheritCurrentReference() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"Contact\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "the server saves an unrelated target and breaks the folder pair, so clio must refuse it")
			.WithMessage("*'Contact' does not inherit from 'BaseFolder'*");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[TestCase("BaseFolder", "*'BaseFolder' is virtual*")]
	[TestCase("UsrMissingFolder", "*'UsrMissingFolder' was not found*")]
	[Description("Reports a virtual target and a missing target with distinct errors.")]
	public void Create_ShouldRefuseOverride_WhenTargetIsVirtualOrMissing(string target, string expectedMessage) {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			$"{{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"{target}\"}}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "an override needs an existing non-virtual target")
			.WithMessage(expectedMessage, because: "each failing precondition has its own message");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[TestCase("\"required\":true", "required")]
	[TestCase("\"type\":\"Lookup\"", "type")]
	[TestCase("\"default-value-source\":\"None\"", "default-value-source")]
	[TestCase("\"masked\":false", "masked")]
	[TestCase("\"default-value\":\"Main\"", "default-value")]
	[TestCase("\"default-value-config\":{\"source\":\"None\"}", "default-value-config")]
	[Description("Refuses an override that carries a property other than reference-schema-name and title, naming the property.")]
	public void Create_ShouldRefuseOverride_WhenItCarriesAnotherProperty(string property, string propertyName) {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			$"{{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"ContactFolder\",{property}}}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an override changes only the reference and caption")
			.WithMessage($"*Override column 'Folder' cannot set '{propertyName}'*");
		_designer.DidNotReceive().CreateNewSchema(Arg.Any<CreateEntitySchemaRequestDto>(), Arg.Any<RemoteCommandOptions>());
		_saved.Should().BeEmpty(because: "the column spec is refused before the schema is created");
	}

	[Test]
	[Description("Refuses an override without reference-schema-name before the schema is created.")]
	public void Create_ShouldRefuseOverride_WhenReferenceSchemaNameIsMissing() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions("{\"name\":\"Folder\",\"override\":true}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an override needs a target schema")
			.WithMessage("*Override column 'Folder' must specify reference-schema-name*");
		_designer.DidNotReceive().CreateNewSchema(Arg.Any<CreateEntitySchemaRequestDto>(), Arg.Any<RemoteCommandOptions>());
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Refuses a create that overrides the same inherited column twice before SaveSchema.")]
	public void Create_ShouldRefuseOverride_WhenSameInheritedColumnIsOverriddenTwice() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride,
			"{\"name\":\"folder\",\"override\":true,\"reference-schema-name\":\"ContactFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "only one target per inherited column can be persisted")
			.WithMessage("*Inherited column 'Folder' is overridden more than once*");
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Refuses the override when the runtime service answers the target read unsuccessfully, before SaveSchema.")]
	public void Create_ShouldRefuseOverride_WhenTargetReadIsUnsuccessful() {
		// Arrange
		_designer.GetRuntimeEntitySchema(ContactFolderUId, Arg.Any<RemoteCommandOptions>())
			.Returns(new RuntimeEntitySchemaResponse { Success = false });
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "an unreadable target cannot be validated")
			.WithMessage("*could not be read*");
		_designer.Received(InheritedLookupOverride.TargetReadAttemptCount)
			.GetRuntimeEntitySchema(ContactFolderUId, Arg.Any<RemoteCommandOptions>());
		_retryDelay.Received(InheritedLookupOverride.TargetReadAttemptCount - 1)
			.Wait(InheritedLookupOverride.TargetReadRetryInterval);
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Reads the target again while it reads as missing after a publish, and redirects once a read succeeds.")]
	public void Create_ShouldRedirectInheritedFolder_WhenTargetReadSucceedsAfterUnsuccessfulReads() {
		// Arrange
		int targetReads = 0;
		_designer.GetRuntimeEntitySchema(ContactFolderUId, Arg.Any<RemoteCommandOptions>())
			.Returns(_ => ++targetReads < 3
				? new RuntimeEntitySchemaResponse { Success = false }
				: new RuntimeEntitySchemaResponse {
					Success = true,
					Schema = new RuntimeEntitySchemaDto {
						UId = ContactFolderUId, Name = "ContactFolder", ParentUId = BaseFolderUId
					}
				});
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		_creator.Create(options);

		// Assert
		SavedFolder().ReferenceSchema.UId.Should().Be(ContactFolderUId,
			because: "the target is validated once its runtime read succeeds");
		targetReads.Should().Be(3, because: "the target is read until the first successful answer");
		_retryDelay.Received(2).Wait(InheritedLookupOverride.TargetReadRetryInterval);
	}

	[Test]
	[Description("Ends the walk on an unsuccessful ancestor read without reading it again.")]
	public void Create_ShouldRefuseOverride_WithoutRetry_WhenAncestorReadIsUnsuccessful() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"Contact\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "Contact does not inherit from BaseFolder")
			.WithMessage("*does not inherit from 'BaseFolder'*");
		_designer.Received(1).GetRuntimeEntitySchema(UnresolvableRootUId, Arg.Any<RemoteCommandOptions>());
		_retryDelay.DidNotReceive().Wait(Arg.Any<TimeSpan>());
	}

	[Test]
	[Description("Refuses the override when the target read throws InvalidOperationException, before SaveSchema.")]
	public void Create_ShouldRefuseOverride_WhenTargetReadThrows() {
		// Arrange
		_designer.GetRuntimeEntitySchema(ContactFolderUId, Arg.Any<RemoteCommandOptions>())
			.Returns<RuntimeEntitySchemaResponse>(_ => throw new InvalidOperationException("empty response"));
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "an unreadable target cannot be validated")
			.WithMessage("*could not be read*")
			.WithInnerException<InvalidOperationException>();
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Propagates an InvalidOperationException subclass thrown while reading an ancestor of the target, before SaveSchema.")]
	public void Create_ShouldPropagate_WhenAncestorReadThrowsInvalidOperationExceptionSubclass() {
		// Arrange
		_designer.GetRuntimeEntitySchema(BaseEntityUId, Arg.Any<RemoteCommandOptions>())
			.Returns<RuntimeEntitySchemaResponse>(_ => throw new TransportFailureException());
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"Contact\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<TransportFailureException>(because: "a transport failure is not an end of the hierarchy");
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Accepts a target two parent hops below the current reference.")]
	public void Create_ShouldRedirectInheritedFolder_WhenTargetInheritsCurrentReferenceThroughTwoHops() {
		// Arrange
		Guid subFolderUId = Guid.NewGuid();
		AddReferenceSchema(subFolderUId, "UsrContactSubFolder", parentUId: ContactFolderUId);
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"UsrContactSubFolder\"}");

		// Act
		_creator.Create(options);

		// Assert
		SavedFolder().ReferenceSchema.UId.Should().Be(subFolderUId,
			because: "UsrContactSubFolder inherits BaseFolder through ContactFolder");
	}

	[Test]
	[Description("Ends the walk over a parent cycle and refuses the target, before SaveSchema.")]
	public void Create_ShouldRefuseOverride_WhenTargetHierarchyHasCycle() {
		// Arrange
		Guid firstUId = Guid.NewGuid();
		Guid secondUId = Guid.NewGuid();
		AddReferenceSchema(firstUId, "UsrLoopFolder", parentUId: secondUId);
		SetRuntimeParent(secondUId, firstUId);
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"UsrLoopFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "a cycle never reaches the current reference")
			.WithMessage("*'UsrLoopFolder' does not inherit from 'BaseFolder'*");
		_designer.Received(2).GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Ends the walk after MaxHierarchyDepth parent hops and refuses the target, before SaveSchema.")]
	public void Create_ShouldRefuseOverride_WhenTargetHierarchyIsDeeperThanLimit() {
		// Arrange
		Guid[] chain = Enumerable.Range(0, InheritedLookupOverride.MaxHierarchyDepth + 8)
			.Select(_ => Guid.NewGuid())
			.ToArray();
		AddReferenceSchema(chain[0], "UsrDeepFolder", parentUId: chain[1]);
		for (int index = 1; index < chain.Length - 1; index++) {
			SetRuntimeParent(chain[index], chain[index + 1]);
		}
		SetRuntimeParent(chain[^1], BaseFolderUId);
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"UsrDeepFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "the walk is bounded")
			.WithMessage("*'UsrDeepFolder' does not inherit from 'BaseFolder'*");
		_designer.Received(InheritedLookupOverride.MaxHierarchyDepth + 1)
			.GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	private void AddReferenceSchema(Guid schemaUId, string name, Guid parentUId) {
		_designer.GetAvailableReferenceSchemas(Arg.Any<GetAvailableSchemasRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new AvailableEntitySchemasResponse { Items = [
				.. NonVirtualReferenceSchemas(),
				new ManagerItemDto { UId = schemaUId, Name = name, Caption = name }
			] });
		SetRuntimeParent(schemaUId, parentUId);
	}

	private void SetRuntimeParent(Guid schemaUId, Guid parentUId) {
		_designer.GetRuntimeEntitySchema(schemaUId, Arg.Any<RemoteCommandOptions>())
			.Returns(new RuntimeEntitySchemaResponse {
				Success = true,
				Schema = new RuntimeEntitySchemaDto { UId = schemaUId, Name = schemaUId.ToString(), ParentUId = parentUId }
			});
	}

	private sealed class TransportFailureException() : InvalidOperationException("transport failure");

	[Test]
	[Description("Reports a create whose reloaded inherited reference differs from the request as a failure.")]
	public void Create_ShouldThrow_WhenReloadedInheritedReferenceDiffers() {
		// Arrange
		_designer.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(_ => new DesignerResponse { Schema = new EntityDesignSchemaDto {
				UId = _schema.UId, Name = "UsrVehicleInFolder", Columns = [],
				InheritedColumns = CreateInheritedColumns(), Indexes = []
			} });
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "a reference change the server dropped must not be reported as success")
			.WithMessage("*was not persisted*expected 'ContactFolder'*'BaseFolder'*");
	}

	[Test]
	[Description("Fails the create when the designer reload is unavailable, because the override cannot be verified.")]
	public void Create_ShouldThrow_WhenOverrideCannotBeVerified() {
		// Arrange
		_designer.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns((DesignerResponse?)null);
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an unverifiable override must not be reported as success")
			.WithMessage("*inherited reference override could not be verified*");
	}
}
