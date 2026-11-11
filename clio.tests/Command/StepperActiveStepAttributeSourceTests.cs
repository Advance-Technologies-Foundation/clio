using System.Text.Json.Nodes;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class StepperActiveStepAttributeSourceTests {
	private static readonly JsonObject PageAttributes = new() {
		["RentalStep"] = new JsonObject { ["value"] = null },
		["_Rental_Step2"] = new JsonObject { ["value"] = null },
		["PDS_Stage"] = new JsonObject { ["modelConfig"] = new JsonObject { ["path"] = "PDS.UsrStage" } }
	};

	[Test]
	[Category("Unit")]
	[Description("Handles crt.Stepper elements.")]
	public void ComponentType_Should_Be_Stepper() {
		// Arrange
		StepperActiveStepAttributeSource source = new();

		// Act
		string componentType = source.ComponentType;

		// Assert
		componentType.Should().Be("crt.Stepper", because: "the source resolves the open step of a stepper");
	}

	[TestCase(null, "RentalStepper", "RentalStepper_ActiveStep", TestName = "unbound")]
	[TestCase("null", "RentalStepper", "RentalStepper_ActiveStep", TestName = "explicit null")]
	[TestCase("\"DeliveryStep\"", "RentalStepper", "RentalStepper_ActiveStep", TestName = "constant step name")]
	[TestCase(null, "Quote.Stepper", "Quote_Stepper_ActiveStep", TestName = "dots in the stepper name")]
	[TestCase("\"$RentalStep\"", "RentalStepper", "RentalStep", TestName = "plain attribute binding")]
	[TestCase("\"$_Rental_Step2\"", "RentalStepper", "_Rental_Step2", TestName = "plain attribute binding with underscores and digits")]
	[Category("Unit")]
	[Description("Resolves the open step to the bound plain attribute, or to the generated '<name>_ActiveStep' when activeStep is unbound or a constant.")]
	public void TryResolve_Should_Return_Active_Step_Attribute(string? activeStepJson, string stepperName, string expectedPath) {
		// Arrange
		StepperActiveStepAttributeSource source = new();
		JsonObject stepper = CreateStepper(stepperName, activeStepJson);

		// Act
		bool resolved = source.TryResolve(stepper, stepperName, PageAttributes, out BusinessRuleAttributeDescriptor? descriptor);

		// Assert
		resolved.Should().BeTrue(because: "an unbound, constant or plain-attribute activeStep prepares the stepper");
		descriptor.Should().Be(
			new BusinessRuleAttributeDescriptor(expectedPath, "Text", null) { IsComponentAttribute = true },
			because: "the open step is an unscoped Text attribute compared with a step name");
	}

	[TestCase("\"$Stage.Value\"", TestName = "nested path")]
	[TestCase("\"$Stage | crt.ToObjectProp\"", TestName = "converter")]
	[TestCase("\"$Stage:prop\"", TestName = "property binding")]
	[TestCase("\"$PDS_Stage\"", TestName = "record column")]
	[TestCase("\"$UndeclaredStep\"", TestName = "undeclared attribute")]
	[TestCase("\"$\"", TestName = "bare dollar")]
	[TestCase("\"$1Step\"", TestName = "name starting with a digit")]
	[TestCase("\"$Rental-Step\"", TestName = "name with a non-identifier character")]
	[TestCase("1", TestName = "non-string value")]
	[Category("Unit")]
	[Description("Resolves nothing for an activeStep binding that leaves the stepper unprepared at runtime.")]
	public void TryResolve_Should_Return_False_For_Unprepared_Stepper(string activeStepJson) {
		// Arrange
		StepperActiveStepAttributeSource source = new();
		JsonObject stepper = CreateStepper("RentalStepper", activeStepJson);

		// Act
		bool resolved = source.TryResolve(stepper, "RentalStepper", PageAttributes, out BusinessRuleAttributeDescriptor? descriptor);

		// Assert
		resolved.Should().BeFalse(because: "only an unbound, constant or declared plain-attribute activeStep lets the stepper change step");
		descriptor.Should().BeNull(because: "an unprepared stepper exposes no active-step attribute");
	}

	private static JsonObject CreateStepper(string name, string? activeStepJson) {
		JsonObject stepper = new() { ["type"] = "crt.Stepper", ["name"] = name };
		if (activeStepJson is not null) {
			stepper.Add("activeStep", JsonNode.Parse(activeStepJson));
		}
		return stepper;
	}
}
