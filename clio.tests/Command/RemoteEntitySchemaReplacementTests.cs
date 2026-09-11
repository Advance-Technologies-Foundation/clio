using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using DesignerResponse = Clio.Command.EntitySchemaDesigner.DesignerResponse<Clio.Command.EntitySchemaDesigner.EntityDesignSchemaDto>;
using SaveDesignItemDesignerResponse = Clio.Command.EntitySchemaDesigner.SaveDesignItemDesignerResponse;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal sealed class RemoteEntitySchemaReplacementTests : BaseClioModuleTests {
	private IRemoteEntitySchemaDesignerClient _designer = null!;
	private FindEntitySchemaCommand _finder = null!;
	private IApplicationPackageListProvider _packages = null!;
	private IRemoteEntitySchemaCreator _creator = null!;
	private static readonly Guid PackageUId = Guid.Parse("11111111-1111-1111-1111-111111111111");

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_designer = Substitute.For<IRemoteEntitySchemaDesignerClient>();
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
	}

	public override void Setup() {
		base.Setup();
		_creator = Container.GetRequiredService<IRemoteEntitySchemaCreator>();
		_packages.GetPackages().Returns([new PackageInfo(new PackageDescriptor {
			Name = "UsrPkg", UId = PackageUId
		}, string.Empty, [])]);
	}

	[Test]
	[Description("Creates a replacement through the native designer despite a same-name schema in another package.")]
	public void Create_ShouldSaveReplacement_WhenBaseExistsInAnotherPackage() {
		// Arrange
		_finder.FindSchemas(Arg.Any<FindEntitySchemaOptions>()).Returns([
			new EntitySchemaSearchResult("Account", "Base", "Terrasoft", "BaseEntity")]);
		Guid parentUId = Guid.NewGuid();
		EntityDesignSchemaDto schema = new() { UId = Guid.NewGuid(), Name = "Account", ExtendParent = true,
			Columns = [], InheritedColumns = [], Indexes = [] };
		_designer.CreateNewSchema(Arg.Any<CreateEntitySchemaRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new DesignerResponse { Schema = schema });
		_designer.GetAvailableParentSchemas(Arg.Any<GetAvailableSchemasRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new AvailableEntitySchemasResponse { Items = [new ManagerItemDto { UId = parentUId, Name = "Account" }] });
		_designer.AssignParentSchema(Arg.Any<AssignParentSchemaRequestDto<EntityDesignSchemaDto>>(), Arg.Any<RemoteCommandOptions>())
			.Returns(call => {
				schema.ParentSchema = new EntityDesignSchemaDto { UId = parentUId, Name = "Account" };
				return new DesignerResponse { Schema = schema };
			});
		_designer.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new SaveDesignItemDesignerResponse { SchemaUId = schema.UId });
		_designer.TryGetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new DesignerResponse { Schema = schema });
		List<EntityDesignSchemaDto> saved = [];
		_designer.When(d => d.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>()))
			.Do(call => saved.Add(call.Arg<EntityDesignSchemaDto>()));
		// Act
		_creator.Create(new CreateEntitySchemaOptions { Package = "UsrPkg", SchemaName = "Account",
			Title = "Account", ExtendParent = true, ParentSchemaName = "Account" });
		// Assert
		saved.Should().ContainSingle(because: "a base schema in another package must permit one replacement save");
		saved[0].ExtendParent.Should().BeTrue(because: "the native replacement flag must survive metadata application");
		saved[0].Package.UId.Should().Be(PackageUId, because: "the replacement belongs to the requested package");
		saved[0].ParentSchema.UId.Should().Be(parentUId, because: "the native parent identity must be preserved");
	}

	[Test]
	[Description("Rejects a duplicate replacement in the requested package before any designer request.")]
	public void Create_ShouldRejectDuplicate_WhenReplacementAlreadyExistsInTargetPackage() {
		// Arrange
		_finder.FindSchemas(Arg.Any<FindEntitySchemaOptions>()).Returns([
			new EntitySchemaSearchResult("Account", "usrpkg", "Customer", "Account")]);
		Action create = () => _creator.Create(new CreateEntitySchemaOptions { Package = "UsrPkg",
			SchemaName = "Account", Title = "Account", ExtendParent = true, ParentSchemaName = "Account" });
		// Act / Assert
		create.Should().Throw<InvalidOperationException>(because: "create-only calls must not overwrite an existing replacement")
			.WithMessage("*already exists*", because: "the caller must receive an actionable duplicate diagnostic");
		_designer.ReceivedCalls().Should().BeEmpty(because: "duplicate rejection must precede all designer work");
	}
}
