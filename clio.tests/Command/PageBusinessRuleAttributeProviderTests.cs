using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PageBusinessRuleAttributeProviderTests {
	[Test]
	[Category("Unit")]
	[Description("Resolves datasource-bound page attributes to entity descriptors and skips collection or unbound attributes.")]
	public void GetAttributes_Should_Return_Datasource_Bound_Page_Descriptors() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		entityAttributeProvider.GetAttributes("UsrTestBR", Arg.Any<Guid>())
			.Returns(new EntityBusinessRuleAttributeContext(
				new EntityDesignSchemaDto(),
				new Dictionary<string, BusinessRuleAttributeDescriptor> {
					["UsrText"] = new("UsrText", "Text", null),
					["UsrText2"] = new("UsrText2", "Text", null),
					["UsrLookupCountry"] = new("UsrLookupCountry", "Lookup", "Country")
				}));
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = CreateBundleWithAttributes(
			new Dictionary<string, object?> {
				["PDS_Text"] = CreateAttribute("PDS.UsrText"),
				["PDS_Text2"] = CreateAttribute("PDS.UsrText2"),
				["PDS_Lookup"] = CreateAttribute("PDS.UsrLookupCountry"),
				["PDS_Missing"] = CreateAttribute("PDS.MissingColumn"),
				["Unbound"] = new Dictionary<string, object?> { ["value"] = 123 },
				["PDS_List"] = new Dictionary<string, object?> {
					["isCollection"] = true,
					["modelConfig"] = new Dictionary<string, object?> {
						["path"] = "PDS.UsrText"
					}
				}
			});

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(["PDS_Text", "PDS_Text2", "PDS_Lookup"],
			because: "page conditions should only expose declared datasource-bound attributes whose entity columns can be resolved");
		result["PDS_Text"].Should().Be(
			new BusinessRuleAttributeDescriptor("PDS_Text", "Text", null),
			because: "page descriptors should use the declared page attribute name as the metadata path");
		result["PDS_Lookup"].Should().Be(
			new BusinessRuleAttributeDescriptor("PDS_Lookup", "Lookup", "Country"),
			because: "page lookup descriptors should keep the referenced entity schema from the datasource column");
		entityAttributeProvider.Received(1).GetAttributes(
			"UsrTestBR",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a DataSource column that is NOT surfaced on the page through the scopeId::path key, reusing the entity attribute provider.")]
	public void GetAttributes_Should_Resolve_NonSurfaced_DataSource_Column_By_Scope() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		entityAttributeProvider.GetAttributes("UsrTestBR", Arg.Any<Guid>())
			.Returns(new EntityBusinessRuleAttributeContext(
				new EntityDesignSchemaDto(),
				new Dictionary<string, BusinessRuleAttributeDescriptor> {
					["UsrHiddenColumn"] = new("UsrHiddenColumn", "Text", null),
					["UsrLookupCountry"] = new("UsrLookupCountry", "Lookup", "Country")
				}));
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		// No viewModelConfig attribute is declared for UsrHiddenColumn - it lives only in the DataSource.
		PageBundleInfo bundle = CreateBundleWithAttributes(new Dictionary<string, object?>());

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.TryGetValue("PDS::UsrHiddenColumn", out BusinessRuleAttributeDescriptor? hidden).Should().BeTrue(
			because: "a non-surfaced DataSource column is reachable through the scopeId::path key");
		hidden!.Should().Be(new BusinessRuleAttributeDescriptor("UsrHiddenColumn", "Text", null),
			because: "the scoped descriptor keeps the in-scope column path and type");
		result.TryGetValue("PDS::UsrLookupCountry", out BusinessRuleAttributeDescriptor? lookup).Should().BeTrue(
			because: "every supported DataSource column is reachable by scope, not just surfaced ones");
		lookup!.ReferenceSchemaName.Should().Be("Country",
			because: "scoped lookup descriptors keep the referenced entity schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a page parameter through the PageParameters scope and exposes it in Keys.")]
	public void GetAttributes_Should_Resolve_Page_Parameter_By_Scope() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewModelConfig = new JsonObject { ["attributes"] = new JsonObject() },
			ModelConfig = new JsonObject { ["dataSources"] = new JsonObject() },
			Parameters = [
				new PageParameterInfo { Name = "RequestType", DataValueType = 1 },
				new PageParameterInfo { Name = "AssignedTo", DataValueType = 10, ReferenceSchemaName = "Contact" }
			]
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.TryGetValue("PageParameters::RequestType", out BusinessRuleAttributeDescriptor? requestType).Should().BeTrue(
			because: "a page parameter is reachable through the PageParameters scope");
		requestType!.DataValueTypeName.Should().Be("Text",
			because: "the parameter data value type is mapped from its numeric type");
		result.TryGetValue("PageParameters::AssignedTo", out BusinessRuleAttributeDescriptor? assignedTo).Should().BeTrue(
			because: "lookup page parameters are also reachable");
		assignedTo!.ReferenceSchemaName.Should().Be("Contact",
			because: "a lookup parameter keeps its reference schema for type compatibility");
		result.Keys.Should().Contain("PageParameters::RequestType",
			because: "page parameters are enumerated as candidates for condition operands");
	}

	[Test]
	[Category("Unit")]
	[Description("Includes an unbound/technical page-local attribute (no modelConfig.path) with a boolean default value as a Boolean root operand.")]
	public void GetAttributes_Should_Include_Unbound_Boolean_Attribute() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = CreateBundleWithAttributes(new Dictionary<string, object?> {
			["UsrTechnicalFlag"] = new Dictionary<string, object?> { ["value"] = true }
		});

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.TryGetValue("UsrTechnicalFlag", out BusinessRuleAttributeDescriptor? flag).Should().BeTrue(
			because: "an unbound page-local attribute with a typed default is a valid root-scope condition operand");
		flag!.DataValueTypeName.Should().Be("Boolean",
			because: "a boolean default value infers a Boolean data value type for the technical attribute");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not resolve an operand under an unknown scope.")]
	public void GetAttributes_Should_Not_Resolve_Unknown_Scope() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = CreateBundleWithAttributes(new Dictionary<string, object?>());

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.ContainsKey("NotADataSource::Column").Should().BeFalse(
			because: "an unknown scope resolves no operand");
	}

	private static PageBundleInfo CreateBundleWithAttributes(IReadOnlyDictionary<string, object?> attributes) =>
		new() {
			ViewModelConfig = new JsonObject {
				["attributes"] = JsonSerializer.SerializeToNode(attributes)
			},
			ModelConfig = new JsonObject {
				["dataSources"] = JsonSerializer.SerializeToNode(new Dictionary<string, object?> {
					["PDS"] = new Dictionary<string, object?> {
						["type"] = "crt.EntityDataSource",
						["config"] = new Dictionary<string, object?> {
							["entitySchemaName"] = "UsrTestBR"
						}
					}
				})
			}
		};

	private static Dictionary<string, object?> CreateAttribute(string path) =>
		new() {
			["modelConfig"] = new Dictionary<string, object?> {
				["path"] = path
			}
		};
}
