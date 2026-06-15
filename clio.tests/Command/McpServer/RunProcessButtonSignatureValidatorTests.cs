using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RunProcessButtonSignatureValidatorTests {

	private static ProcessSignatureParameter Param(string name, string direction = "Input") =>
		new() { Name = name, Direction = direction };

	[Test]
	[Description("Passes with no error or warning when every referenced code is an input parameter on the signature.")]
	public void Validate_Should_Pass_When_All_Codes_Exist_As_Inputs() {
		// Arrange
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage",
			["ProcessSchemaParameter2"]);

		// Act
		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, [Param("ProcessSchemaParameter2")]);

		// Assert
		result.Error.Should().BeNull(because: "every referenced code exists on the signature");
		result.Warnings.Should().BeEmpty(because: "an input parameter mapping produces no warning");
	}

	[Test]
	[Description("Errors on an unknown code, listing the valid codes and the CODE-not-caption note.")]
	public void Validate_Should_Error_On_Unknown_Code_With_Valid_Codes_And_Caption_Note() {
		// Arrange
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage", ["Parameter2"]);

		// Act
		RunProcessButtonSignatureValidator.Result result = RunProcessButtonSignatureValidator.Validate(
			config, [Param("ProcessSchemaParameter2"), Param("ProcessSchemaParameter1")]);

		// Assert
		result.Error.Should().NotBeNull(because: "the referenced code is not a real parameter");
		result.Error.Should().Contain("MyButton", because: "the message should name the offending button");
		result.Error.Should().Contain("Parameter2", because: "the message should name the unknown code");
		result.Error.Should().Contain("ProcessSchemaParameter2").And.Contain("ProcessSchemaParameter1",
			because: "the valid codes should be listed so the caller can correct the config");
		result.Error.Should().Contain("CODE",
			because: "the message should remind that the key must be the code, not the caption");
		result.Warnings.Should().BeEmpty(because: "a hard error does not also emit warnings");
	}

	[Test]
	[Description("Warns (not errors) when a referenced code resolves to an Output-only parameter.")]
	public void Validate_Should_Warn_When_Code_Is_Output_Only() {
		// Arrange
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage", ["ResultParam"]);

		// Act
		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, [Param("ResultParam", "Output")]);

		// Assert
		result.Error.Should().BeNull(because: "the code exists, so it is not a hard error");
		result.Warnings.Should().ContainSingle(because: "an output-only target is advisory, not blocking")
			.Which.Should().Contain("ResultParam").And.Contain("Output",
				because: "the warning should name the parameter and its direction");
	}

	[Test]
	[Description("Errors when the signature has no parameters but the button references a code.")]
	public void Validate_Should_Error_When_Signature_Has_No_Parameters() {
		// Arrange
		RunProcessButtonConfig config = new(null, "UsrProc", "RegardlessOfThePage", ["AnyCode"]);

		// Act
		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, []);

		// Assert
		result.Error.Should().NotBeNull(because: "no signature parameter can match the referenced code");
		result.Error.Should().Contain("a run-process button",
			because: "the message falls back to a generic label when the button name is missing");
		result.Error.Should().Contain("AnyCode", because: "the message should name the unknown code");
	}

	[Test]
	[Description("Tolerates a null parameters collection and still reports the unknown code as an error.")]
	public void Validate_Should_Tolerate_Null_Parameters() {
		// Arrange
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage", ["AnyCode"]);

		// Act
		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, null);

		// Assert
		result.Error.Should().NotBeNull(because: "a null parameter list is treated as no valid codes");
	}
}
