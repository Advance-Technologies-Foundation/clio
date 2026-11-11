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

	[Test]
	[Category("Unit")]
	[Description("Exposes page parameters from bundle.parameters as PageParameters-scoped condition sources, including parameters that have no view-model attribute, and skips nameless or unsupported-type parameters.")]
	public void GetAttributes_Should_Expose_Page_Parameters_As_Scoped_Condition_Sources() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
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
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
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
	[Description("Exposes '<Stepper>_ActiveStep' as an unscoped Text condition attribute for every named crt.Stepper in the view config, including nested ones, and skips unnamed steppers.")]
	public void GetAttributes_Should_Expose_Active_Step_Attribute_For_Named_Steppers() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""
				[
					{ "type": "crt.Stepper", "name": "RequestStepper", "items": [ { "type": "crt.StepperItem", "name": "DeliveryStep" } ] },
					{ "type": "crt.FlexContainer", "name": "Container", "items": [
						{ "type": "crt.Stepper", "name": "Stepper_ns7dls6" },
						{ "type": "crt.Stepper" },
						{ "type": "crt.Stepper", "name": "   " },
						{ "type": "crt.Input", "name": "NotAStepper" }
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
			["RequestStepper_ActiveStep", "Stepper_ns7dls6_ActiveStep"],
			because: "the stepper generates '<name>_ActiveStep' at runtime, so only named steppers can be addressed");
		result["RequestStepper_ActiveStep"].Should().Be(
			new BusinessRuleAttributeDescriptor("RequestStepper_ActiveStep", "Text", null) { IsComponentAttribute = true },
			because: "the active step is compared to a step name, so it is a Text attribute, unscoped, with the key as the metadata path");
	}

	[Test]
	[Category("Unit")]
	[Description("Adds no stepper attribute when the view config has no crt.Stepper.")]
	public void GetAttributes_Should_Not_Expose_Active_Step_Attribute_Without_Stepper() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""[ { "type": "crt.Input", "name": "Input_1" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Should().BeEmpty(because: "a page without a stepper has no active-step attribute");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes the attribute bound to a stepper's activeStep as its active step and does not offer the generated '<Stepper>_ActiveStep'.")]
	public void GetAttributes_Should_Expose_Bound_Attribute_For_Stepper_With_Bound_Active_Step() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewModelConfig = new JsonObject {
				["attributes"] = new JsonObject { ["RentalStep"] = new JsonObject { ["value"] = null } }
			},
			ViewConfig = JsonNode.Parse("""
				[ { "type": "crt.Stepper", "name": "RentalStepper", "activeStep": "$RentalStep",
				    "items": [ { "type": "crt.StepperItem", "name": "DeliveryStep" } ] } ]
				""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(["RentalStep"],
			because: "a bound stepper keeps its open step in the bound attribute; rules on the generated name have no effect");
		result["RentalStep"].Should().Be(
			new BusinessRuleAttributeDescriptor("RentalStep", "Text", null) { IsComponentAttribute = true },
			because: "the bound attribute holds a step name, so it is an unscoped Text attribute with its own name as the path");
	}

	[Test]
	[Category("Unit")]
	[Description("Replaces dots in the stepper name with underscores in the generated active-step attribute.")]
	public void GetAttributes_Should_Replace_Dots_In_Generated_Active_Step_Attribute() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""[ { "type": "crt.Stepper", "name": "Quote.Stepper", "activeStep": null } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(["Quote_Stepper_ActiveStep"],
			because: "the platform builds the generated attribute prefix from the stepper name with dots replaced by underscores");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes the generated '<Stepper>_ActiveStep' for a stepper whose activeStep is a constant step name, as the Interface Designer does.")]
	public void GetAttributes_Should_Expose_Generated_Attribute_For_Constant_Active_Step() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""[ { "type": "crt.Stepper", "name": "RentalStepper", "activeStep": "DeliveryStep" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(["RentalStepper_ActiveStep"],
			because: "a constant only sets the opening step; the open step lives in the generated attribute");
	}

	[TestCase("\"$Stage.Value\"", TestName = "nested path")]
	[TestCase("\"$Stage | crt.ToObjectProp\"", TestName = "converter")]
	[TestCase("\"$Stage:prop\"", TestName = "property binding")]
	[TestCase("\"$PDS_Stage\"", TestName = "record column")]
	[TestCase("1", TestName = "non-string value")]
	[Category("Unit")]
	[Description("Adds no active-step attribute for a stepper whose activeStep binding leaves it unprepared at runtime.")]
	public void GetAttributes_Should_Not_Expose_Active_Step_Attribute_For_Unprepared_Stepper(string activeStepJson) {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = new() {
			ViewModelConfig = new JsonObject {
				["attributes"] = new JsonObject {
					["Stage"] = new JsonObject { ["value"] = null },
					["PDS_Stage"] = JsonSerializer.SerializeToNode(CreateAttribute("PDS.UsrStage"))
				}
			},
			ViewConfig = JsonNode.Parse(
				$$"""[ { "type": "crt.Stepper", "name": "RentalStepper", "activeStep": {{activeStepJson}} } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Should().BeEmpty(
			because: "only an unbound activeStep or one bound to a plain page attribute prepares the stepper");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps a declared attribute that has the same key as a runtime stepper attribute and coexists with data source, page parameter and declared attributes.")]
	public void GetAttributes_Should_Keep_Declared_Attribute_On_Stepper_Key_Collision_And_Coexist_With_Other_Sources() {
		// Arrange
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		entityAttributeProvider.GetAttributes("UsrTestBR", Arg.Any<Guid>())
			.Returns(new EntityBusinessRuleAttributeContext(
				new EntityDesignSchemaDto(),
				new Dictionary<string, BusinessRuleAttributeDescriptor> {
					["UsrText"] = new("UsrText", "Text", null)
				}));
		PageBusinessRuleAttributeProvider provider = new(entityAttributeProvider);
		PageBundleInfo bundle = CreateBundleWithAttributes(
			new Dictionary<string, object?> {
				["PDS_Text"] = CreateAttribute("PDS.UsrText"),
				["RequestStepper_ActiveStep"] = CreateAttribute("PDS.UsrText")
			});
		bundle = new PageBundleInfo {
			ViewModelConfig = bundle.ViewModelConfig,
			ModelConfig = bundle.ModelConfig,
			Parameters = [new PageParameterInfo { Name = "UsrFlag", DataValueType = 12 }],
			ViewConfig = JsonNode.Parse("""[ { "type": "crt.Stepper", "name": "RequestStepper" }, { "type": "crt.Stepper", "name": "OtherStepper" } ]""")!.AsArray()
		};

		// Act
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> result = provider.GetAttributes(
			bundle,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

		// Assert
		result.Keys.Should().BeEquivalentTo(
			["PDS_Text", "RequestStepper_ActiveStep", "PDS.UsrText", "PageParameters.UsrFlag", "OtherStepper_ActiveStep"],
			because: "stepper attributes are added next to declared, data source and page parameter attributes");
		result["RequestStepper_ActiveStep"].IsComponentAttribute.Should().BeFalse(
			because: "a declared attribute of the same name keeps its own descriptor instead of being overwritten");
		result["OtherStepper_ActiveStep"].IsComponentAttribute.Should().BeTrue(
			because: "an undeclared stepper attribute is a component attribute descriptor");
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
