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
		result.Keys.Should().BeEquivalentTo(
			["PDS_Text", "PDS_Text2", "PDS_Lookup", "PDS.UsrText", "PDS.UsrText2", "PDS.UsrLookupCountry"],
			because: "page conditions expose declared on-page attributes plus every supported data source column addressed by '<dataSource>.<column>'");
		result["PDS_Text"].Should().Be(
			new BusinessRuleAttributeDescriptor("PDS_Text", "Text", null),
			because: "on-page descriptors should use the declared page attribute name as the metadata path and carry no scope");
		result["PDS_Lookup"].Should().Be(
			new BusinessRuleAttributeDescriptor("PDS_Lookup", "Lookup", "Country"),
			because: "page lookup descriptors should keep the referenced entity schema from the datasource column");
		result["PDS.UsrText"].Should().Be(
			new BusinessRuleAttributeDescriptor("UsrText", "Text", null, "PDS"),
			because: "data-source-scoped descriptors use the entity column as the metadata path and carry the data source name as scope");
		result["PDS.UsrLookupCountry"].Should().Be(
			new BusinessRuleAttributeDescriptor("UsrLookupCountry", "Lookup", "Country", "PDS"),
			because: "scoped lookup descriptors keep the referenced entity schema and the data source scope");
		entityAttributeProvider.Received(1).GetAttributes(
			"UsrTestBR",
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
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
