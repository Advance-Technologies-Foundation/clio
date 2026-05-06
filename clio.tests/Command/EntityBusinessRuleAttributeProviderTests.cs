using System;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class EntityBusinessRuleAttributeProviderTests {
	[Test]
	[Category("Unit")]
	[Description("Builds entity business-rule attribute descriptors from own and inherited entity columns.")]
	public void GetAttributes_Should_Build_Descriptors_From_Entity_Columns() {
		// Arrange
		IEntityBusinessRuleSchemaProvider schemaProvider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		schemaProvider.GetSchema("UsrOrder", Arg.Any<Guid>()).Returns(new EntityDesignSchemaDto {
			Columns = [
				new EntitySchemaColumnDto {
					Name = "Owner",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "Contact"
					}
				}
			],
			InheritedColumns = [
				new EntitySchemaColumnDto {
					Name = "CreatedOn",
					DataValueType = 7
				}
			]
		});
		EntityBusinessRuleAttributeProvider provider = new(schemaProvider);

		// Act
		EntityBusinessRuleAttributeContext context = provider.GetAttributes(
			"UsrOrder",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		context.Attributes.Should().ContainKeys(["Owner", "CreatedOn"],
			because: "business rules can reference both own and inherited entity columns");
		context.Attributes["Owner"].Should().Be(
			new BusinessRuleAttributeDescriptor("Owner", "Lookup", "Contact"),
			because: "lookup descriptors should keep reference schema metadata for value conversion");
		context.Attributes["CreatedOn"].Should().Be(
			new BusinessRuleAttributeDescriptor("CreatedOn", "DateTime", null),
			because: "date-time descriptors should preserve the mapped Creatio data value type");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves supported attributes without materializing unused columns that have missing or unsupported data value types.")]
	public void GetAttributes_Should_Not_Reject_Unused_Unsupported_Columns() {
		// Arrange
		IEntityBusinessRuleSchemaProvider schemaProvider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		schemaProvider.GetSchema("UsrOrder", Arg.Any<Guid>()).Returns(new EntityDesignSchemaDto {
			Columns = [
				new EntitySchemaColumnDto {
					Name = "Status",
					DataValueType = 1
				},
				new EntitySchemaColumnDto {
					Name = "BrokenWithoutType",
					DataValueType = null
				},
				new EntitySchemaColumnDto {
					Name = "BrokenUnsupported",
					DataValueType = 999
				}
			],
			InheritedColumns = []
		});
		EntityBusinessRuleAttributeProvider provider = new(schemaProvider);

		// Act
		EntityBusinessRuleAttributeContext context = provider.GetAttributes(
			"UsrOrder",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		context.Attributes["Status"].Should().Be(
			new BusinessRuleAttributeDescriptor("Status", "Text", null),
			because: "unused unsupported columns should not prevent valid rule attributes from being resolved");
	}
}
