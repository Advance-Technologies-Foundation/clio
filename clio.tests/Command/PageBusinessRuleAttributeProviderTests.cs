using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using System.Text.Json;
using System;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
		PageBusinessRuleAttributeProvider provider = CreateProvider(entityAttributeProvider);
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

	[Test]
	[Category("Unit")]
	[Description("Exposes page parameters from bundle.parameters as PageParameters-scoped condition sources, including parameters that have no view-model attribute, and skips nameless or unsupported-type parameters.")]
	public void GetAttributes_Should_Expose_Page_Parameters_As_Scoped_Condition_Sources() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = CreateProvider(entityAttributeProvider);
		PageBundleInfo bundle = CreateBundleWithParameters([
			new PageParameterInfo { Name = "UsrBooleanParameter1", DataValueType = 12 },
			new PageParameterInfo { Name = "UsrIntegerParameter1", DataValueType = 4 },
			new PageParameterInfo { Name = "UsrOwnerParameter", DataValueType = 10, ReferenceSchemaName = "Contact" },
			new PageParameterInfo { Name = "UsrUnsupported", DataValueType = 999 },
			new PageParameterInfo { Name = "   ", DataValueType = 4 }
		]);

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(
			["PageParameters.UsrBooleanParameter1", "PageParameters.UsrIntegerParameter1", "PageParameters.UsrOwnerParameter"],
			because: "every named page parameter with a supported data value type is a PageParameters-scoped condition source, whether or not it is bound to a control");
		result["PageParameters.UsrIntegerParameter1"].Should().Be(
			new BusinessRuleAttributeDescriptor("UsrIntegerParameter1", "Integer", null, "PageParameters"),
			because: "a page parameter with no view-model attribute is still addressable via the PageParameters scope using its raw name as the metadata path");
		result["PageParameters.UsrBooleanParameter1"].Should().Be(
			new BusinessRuleAttributeDescriptor("UsrBooleanParameter1", "Boolean", null, "PageParameters"),
			because: "the parameter data value type code maps to the Creatio type name and the scope is PageParameters");
		result["PageParameters.UsrOwnerParameter"].Should().Be(
			new BusinessRuleAttributeDescriptor("UsrOwnerParameter", "Lookup", "Contact", "PageParameters"),
			because: "lookup page parameters keep their reference schema so equality comparisons can be type-checked");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes a page parameter bound to a control under BOTH its raw view-model attribute name (unscoped) and the PageParameters-scoped key, so either addressing form resolves.")]
	public void GetAttributes_Should_Expose_Bound_Page_Parameter_Under_Both_Addressing_Forms() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = CreateProvider(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewModelConfig = new JsonObject {
				["attributes"] = JsonSerializer.SerializeToNode(new Dictionary<string, object?> {
					["PageParameters_UsrBooleanParameter1_uno0kqd"] = CreateAttribute("PageParameters.UsrBooleanParameter1")
				})
			},
			Parameters = [new PageParameterInfo { Name = "UsrBooleanParameter1", DataValueType = 12 }]
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(
			["PageParameters_UsrBooleanParameter1_uno0kqd", "PageParameters.UsrBooleanParameter1"],
			because: "a bound page parameter is addressable both by its raw view-model attribute name and by the PageParameters scope");
		result["PageParameters_UsrBooleanParameter1_uno0kqd"].Should().Be(
			new BusinessRuleAttributeDescriptor("PageParameters_UsrBooleanParameter1_uno0kqd", "Boolean", null),
			because: "the raw view-model attribute form is unscoped (local ViewModel scope) with the flat attribute key as the metadata path");
		result["PageParameters.UsrBooleanParameter1"].Should().Be(
			new BusinessRuleAttributeDescriptor("UsrBooleanParameter1", "Boolean", null, "PageParameters"),
			because: "the scoped form uses the raw parameter name and the PageParameters scope");
	}

	[Test]
	[Category("Unit")]
	[Description("Asks the registered source for every named element of its component type, including nested ones, and skips unnamed elements and other types.")]
	public void GetAttributes_Should_Expose_Component_Attribute_For_Every_Named_Element_Of_Source_Type() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = CreateProvider(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""
				[
					{ "type": "test.Widget", "name": "FirstWidget" },
					{ "type": "crt.FlexContainer", "name": "Container", "items": [
						{ "type": "test.Widget", "name": "NestedWidget" },
						{ "type": "test.Widget" },
						{ "type": "test.Widget", "name": "   " },
						{ "type": "crt.Input", "name": "NotAWidget" }
					] }
				]
				""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(
			["FirstWidget_State", "NestedWidget_State"],
			because: "only named elements of a type with a registered source expose a component attribute");
		result["FirstWidget_State"].Should().Be(
			new BusinessRuleAttributeDescriptor("FirstWidget_State", "Text", null) { IsComponentAttribute = true },
			because: "the provider stores the descriptor the source resolved, keyed by its path");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes no component attribute when the view config has no element of a registered source type.")]
	public void GetAttributes_Should_Not_Expose_Component_Attribute_Without_Matching_Element() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = CreateProvider(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""[ { "type": "crt.Input", "name": "Input_1" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Should().BeEmpty(because: "a page without an element of a registered source type has no component attribute");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps a declared attribute that has the same key as a component attribute and coexists with data source, page parameter and declared attributes.")]
	public void GetAttributes_Should_Keep_Declared_Attribute_On_Component_Key_Collision_And_Coexist_With_Other_Sources() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		entityAttributeProvider.GetAttributes("UsrTestBR", Arg.Any<Guid>())
			.Returns(new EntityBusinessRuleAttributeContext(
				new EntityDesignSchemaDto(),
				new Dictionary<string, BusinessRuleAttributeDescriptor> {
					["UsrText"] = new("UsrText", "Text", null)
				}));
		PageBusinessRuleAttributeProvider provider = CreateProvider(entityAttributeProvider);
		PageBundleInfo bundle = CreateBundleWithAttributes(
			new Dictionary<string, object?> {
				["PDS_Text"] = CreateAttribute("PDS.UsrText"),
				["FirstWidget_State"] = CreateAttribute("PDS.UsrText")
			});
		bundle = new PageBundleInfo {
			ViewModelConfig = bundle.ViewModelConfig,
			ModelConfig = bundle.ModelConfig,
			Parameters = [new PageParameterInfo { Name = "UsrFlag", DataValueType = 12 }],
			ViewConfig = JsonNode.Parse("""[ { "type": "test.Widget", "name": "FirstWidget" }, { "type": "test.Widget", "name": "OtherWidget" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(
			["PDS_Text", "FirstWidget_State", "PDS.UsrText", "PageParameters.UsrFlag", "OtherWidget_State"],
			because: "component attributes are added next to declared, data source and page parameter attributes");
		result["FirstWidget_State"].IsComponentAttribute.Should().BeFalse(
			because: "a declared attribute of the same name keeps its own descriptor instead of being overwritten");
		result["OtherWidget_State"].IsComponentAttribute.Should().BeTrue(
			because: "an undeclared component attribute comes from the source");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes no component attribute for an element type that has no registered source.")]
	public void GetAttributes_Should_Not_Expose_Component_Attribute_Without_Source() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider, []);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""[ { "type": "test.Widget", "name": "FirstWidget" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Should().BeEmpty(because: "the provider knows no component; only a registered source exposes one");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the provider from the real container with exactly one source per component type, so the stepper active step is exposed.")]
	public void Provider_Should_Resolve_From_Container_With_Stepper_Source() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		using ServiceProvider container = (ServiceProvider)new BindingsModule(fileSystem).Register(new EnvironmentSettings {
			Uri = "http://localhost"
		});
		IPageBusinessRuleAttributeProvider provider = container.GetRequiredService<IPageBusinessRuleAttributeProvider>();
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""[ { "type": "crt.Stepper", "name": "RequestStepper" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(["RequestStepper_ActiveStep"],
			because: "the container registers each component source once and the provider receives the stepper source");
	}

	private static PageBusinessRuleAttributeProvider CreateProvider(IEntityBusinessRuleAttributeProvider entityAttributeProvider) =>
		new(entityAttributeProvider, [new WidgetStateSource()]);

	private sealed class WidgetStateSource : IComponentConditionAttributeSource {
		public string ComponentType => "test.Widget";

		public bool TryResolve(
			JsonObject element,
			string elementName,
			JsonObject pageAttributes,
			[NotNullWhen(true)] out BusinessRuleAttributeDescriptor? descriptor) {
			descriptor = new BusinessRuleAttributeDescriptor(elementName + "_State", "Text", null) {
				IsComponentAttribute = true
			};
			return true;
		}
	}

	private static PageBundleInfo CreateBundleWithParameters(IReadOnlyList<PageParameterInfo> parameters) =>
		new() {
			Parameters = parameters
		};

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
