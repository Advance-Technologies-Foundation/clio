using System;
using System.Linq;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class EntityBusinessRuleSchemaProviderTests {
	[Test]
	[Category("Unit")]
	[Description("Loads an entity schema through the designer client using full hierarchy and the default culture.")]
	public void GetSchema_Should_Load_Full_Hierarchy_With_Default_Culture() {
		// Arrange
		IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		EntityDesignSchemaDto expectedSchema = new() {
			Name = "UsrOrder",
			UId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
		};
		entitySchemaDesignerClient.GetSchemaDesignItem(
				Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true,
				Schema = expectedSchema
			});
		EntityBusinessRuleSchemaProvider provider = new(entitySchemaDesignerClient);

		// Act
		EntityDesignSchemaDto result = provider.GetSchema(
			" UsrOrder ",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Should().BeSameAs(expectedSchema,
			because: "the schema provider should return the designer schema without remapping it");
		entitySchemaDesignerClient.Received(1).GetSchemaDesignItem(
			Arg.Is<GetSchemaDesignItemRequestDto>(request =>
				request.Name == "UsrOrder"
				&& request.PackageUId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
				&& request.UseFullHierarchy
				&& request.Cultures.SequenceEqual(new[] { EntitySchemaDesignerSupport.DefaultCultureName })),
			Arg.Any<RemoteCommandOptions>());
	}
}
