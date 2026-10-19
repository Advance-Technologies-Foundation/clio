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
		SetParentFolderReference(BaseFolderUId, "BaseFolder");
	}

	private void SetParentFolderReference(Guid referenceUId, string referenceName) {
		_designer.GetSchemaDesignItem(Arg.Is<GetSchemaDesignItemRequestDto>(request =>
					request.Name == "BaseItemInFolder" && request.PackageUId == PackageUId && request.UseFullHierarchy),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new DesignerResponse { Schema = new EntityDesignSchemaDto {
				UId = BaseItemInFolderUId, Name = "BaseItemInFolder", IsVirtual = true,
				Columns = [new EntitySchemaColumnDto {
					UId = FolderColumnUId, Name = "Folder", DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto { UId = referenceUId, Name = referenceName }
				}],
				InheritedColumns = []
			} });
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
			.WithMessage("*only the reference schema of an inherited Lookup column can be overridden*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*Parent schema 'BaseItemInFolder'*has no column 'UsrFolder'*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*Parent schema 'BaseItemInFolder'*is not virtual*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*replaces its parent*",
				because: "the message must name the rule that refused the request");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses Contact as the Folder target because it does not inherit from BaseFolder, ending the walk at the root whose parent the runtime service cannot resolve; ContactFolder passes.")]
	public void Create_ShouldRefuseOverride_WhenTargetDoesNotInheritParentReference() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"Contact\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "the server saves an unrelated target and breaks the folder pair, so clio must refuse it")
			.WithMessage("*'Contact' does not inherit from 'BaseFolder'*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage($"*Override column 'Folder' cannot set '{propertyName}'*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*Override column 'Folder' must specify reference-schema-name*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*Inherited column 'Folder' is overridden more than once*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*could not be read*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*does not inherit from 'BaseFolder'*hierarchy of 'Contact' was read up to*",
				because: "the message must show where the hierarchy read stopped");
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
			.WithMessage("*could not be read*",
				because: "the message must name the rule that refused the request")
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
	[Description("Accepts a target two parent hops below the reference the column has in the parent schema.")]
	public void Create_ShouldRedirectInheritedFolder_WhenTargetInheritsParentReferenceThroughTwoHops() {
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
		act.Should().Throw<EntitySchemaDesignerException>(because: "a cycle never reaches the parent's reference")
			.WithMessage("*'UsrLoopFolder' does not inherit from 'BaseFolder'*",
				because: "the message must name the rule that refused the request");
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
			.WithMessage("*'UsrDeepFolder' does not inherit from 'BaseFolder'*",
				because: "the message must name the rule that refused the request");
		_designer.Received(InheritedLookupOverride.MaxHierarchyDepth + 1)
			.GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
		_designer.DidNotReceive().SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Validates the target against the reference the parent schema declares for the column, so a sibling of that reference is refused.")]
	public void Create_ShouldRefuseOverride_WhenTargetDoesNotInheritReferenceDeclaredByParent() {
		// Arrange
		SetParentFolderReference(ContactFolderUId, "ContactFolder");
		Guid siblingUId = Guid.NewGuid();
		AddReferenceSchema(siblingUId, "UsrAccountFolder", parentUId: BaseFolderUId);
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"UsrAccountFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "the parent narrows the column to ContactFolder")
			.WithMessage("*'UsrAccountFolder' does not inherit from 'ContactFolder'*parent schema 'BaseItemInFolder'*",
				because: "the message must name the reference the parent declares");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses the override when the parent schema's column has no reference schema.")]
	public void Create_ShouldRefuseOverride_WhenParentColumnHasNoReference() {
		// Arrange
		SetParentFolderReference(Guid.Empty, "BaseFolder");
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "there is no reference to validate the target against")
			.WithMessage("*Column 'Folder' of parent schema 'BaseItemInFolder' has no reference schema*",
				because: "the message must name the parent column");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses an override on a schema created without parent-schema-name with a message that asks for it.")]
	public void Create_ShouldRefuseOverride_WhenParentSchemaNameIsMissing() {
		// Arrange
		CreateEntitySchemaOptions options = CreateOptions(FolderOverride);
		options.ParentSchemaName = null;

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "a schema without a parent inherits no column")
			.WithMessage("*Override column 'Folder' requires parent-schema-name*",
				because: "the message must name the missing option instead of a '<none>' parent");
		_saved.Should().BeEmpty(because: "the refusal must precede SaveSchema");
	}

	[Test]
	[Description("Refuses an override on a design schema whose parent is not populated.")]
	public void Apply_ShouldRefuseOverride_WhenParentSchemaIsNull() {
		// Arrange
		InheritedLookupOverride sut = new(_designer, _retryDelay);
		EntityDesignSchemaDto schema = new() {
			Name = "UsrVehicleInFolder", Columns = [], InheritedColumns = CreateInheritedColumns()
		};

		// Act
		Action act = () => sut.Apply(schema, "Folder", "ContactFolder",
			new ManagerItemDto { UId = ContactFolderUId, Name = "ContactFolder" }, new RemoteCommandOptions());

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "an override needs a parent schema")
			.WithMessage("*'UsrVehicleInFolder' has no parent schema*requires parent-schema-name*",
				because: "the message must ask for the parent schema");
	}

	[Test]
	[Description("Reports a reloaded schema that holds the column as an own column as a failed override.")]
	public void VerifyPersisted_ShouldThrow_WhenColumnWasSavedAsOwnColumn() {
		// Arrange
		InheritedLookupOverride sut = new(_designer, _retryDelay);
		EntityDesignSchemaDto reloaded = new() {
			Name = "UsrVehicleInFolder",
			Columns = [new EntitySchemaColumnDto { Name = "Folder", DataValueType = 10 }],
			InheritedColumns = CreateInheritedColumns()
		};

		// Act
		Action act = () => sut.VerifyPersisted(reloaded, "Folder", "ContactFolder");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "an own column is not an override")
			.WithMessage("*'Folder' was saved as an own column*",
				because: "the message must say how the column was saved");
	}

	[Test]
	[Description("Reports a reloaded schema without the inherited column as a failed override.")]
	public void VerifyPersisted_ShouldThrow_WhenInheritedColumnIsMissing() {
		// Arrange
		InheritedLookupOverride sut = new(_designer, _retryDelay);
		EntityDesignSchemaDto reloaded = new() { Name = "UsrVehicleInFolder", Columns = [], InheritedColumns = [] };

		// Act
		Action act = () => sut.VerifyPersisted(reloaded, "Folder", "ContactFolder");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(because: "a missing column cannot confirm the override")
			.WithMessage("*Inherited column 'Folder' could not be reloaded*",
				because: "the message must say the column is missing after the save");
	}

	[Test]
	[Description("Reads the target again after a thrown InvalidOperationException and redirects once a read succeeds.")]
	public void Create_ShouldRedirectInheritedFolder_WhenTargetReadThrowsThenSucceeds() {
		// Arrange
		int targetReads = 0;
		_designer.GetRuntimeEntitySchema(ContactFolderUId, Arg.Any<RemoteCommandOptions>())
			.Returns(_ => ++targetReads < 2
				? throw new InvalidOperationException("Can't find EntitySchema")
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
			because: "a failed first read of a just-published target is retried");
		_retryDelay.Received(1).Wait(InheritedLookupOverride.TargetReadRetryInterval);
	}

	[TestCase(InheritedLookupOverride.MaxHierarchyDepth, true)]
	[TestCase(InheritedLookupOverride.MaxHierarchyDepth + 1, false)]
	[Description("Accepts a target exactly MaxHierarchyDepth hops below the parent's reference and refuses one hop more.")]
	public void Create_ShouldBoundHierarchyWalk_AtMaxHierarchyDepth(int hops, bool accepted) {
		// Arrange
		Guid[] chain = Enumerable.Range(0, hops).Select(_ => Guid.NewGuid()).ToArray();
		AddReferenceSchema(chain[0], "UsrEdgeFolder", parentUId: hops > 1 ? chain[1] : BaseFolderUId);
		for (int index = 1; index < chain.Length; index++) {
			SetRuntimeParent(chain[index], index + 1 < chain.Length ? chain[index + 1] : BaseFolderUId);
		}
		CreateEntitySchemaOptions options = CreateOptions(
			"{\"name\":\"Folder\",\"override\":true,\"reference-schema-name\":\"UsrEdgeFolder\"}");

		// Act
		Action act = () => _creator.Create(options);

		// Assert
		if (accepted) {
			act.Should().NotThrow(because: "a reference reached within the depth limit is accepted");
			SavedFolder().ReferenceSchema.UId.Should().Be(chain[0], because: "the target is applied");
		} else {
			act.Should().Throw<EntitySchemaDesignerException>(because: "the walk stops at the depth limit")
				.WithMessage("*'UsrEdgeFolder' does not inherit from 'BaseFolder'*",
					because: "a reference beyond the limit is not reached");
		}
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
			.WithMessage("*was not persisted*expected 'ContactFolder'*'BaseFolder'*",
				because: "the message must say what could not be confirmed after the save");
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
			.WithMessage("*inherited reference override could not be verified*",
				because: "the message must say what could not be confirmed after the save");
	}
}
