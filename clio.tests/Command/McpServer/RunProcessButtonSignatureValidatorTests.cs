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
	public void Validate_Should_Pass_When_All_Codes_Exist_As_Inputs() {
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage",
			["ProcessSchemaParameter2"]);

		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, [Param("ProcessSchemaParameter2")]);

		result.Error.Should().BeNull();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	public void Validate_Should_Error_On_Unknown_Code_With_Valid_Codes_And_Caption_Note() {
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage", ["Parameter2"]);

		RunProcessButtonSignatureValidator.Result result = RunProcessButtonSignatureValidator.Validate(
			config, [Param("ProcessSchemaParameter2"), Param("ProcessSchemaParameter1")]);

		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("MyButton");
		result.Error.Should().Contain("Parameter2");
		result.Error.Should().Contain("ProcessSchemaParameter2").And.Contain("ProcessSchemaParameter1");
		result.Error.Should().Contain("CODE");
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	public void Validate_Should_Warn_When_Code_Is_Output_Only() {
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage", ["ResultParam"]);

		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, [Param("ResultParam", "Output")]);

		result.Error.Should().BeNull();
		result.Warnings.Should().ContainSingle().Which.Should().Contain("ResultParam").And.Contain("Output");
	}

	[Test]
	public void Validate_Should_Error_When_Signature_Has_No_Parameters() {
		RunProcessButtonConfig config = new(null, "UsrProc", "RegardlessOfThePage", ["AnyCode"]);

		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, []);

		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("a run-process button");
		result.Error.Should().Contain("AnyCode");
	}

	[Test]
	public void Validate_Should_Tolerate_Null_Parameters() {
		RunProcessButtonConfig config = new("MyButton", "UsrProc", "RegardlessOfThePage", ["AnyCode"]);

		RunProcessButtonSignatureValidator.Result result =
			RunProcessButtonSignatureValidator.Validate(config, null);

		result.Error.Should().NotBeNull();
	}
}
