using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RunProcessButtonConfigReaderTests {

	private static string WrapViewConfigDiff(string viewConfigDiffArray) =>
		"define(\"UsrTest_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function() { return {"
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/" + viewConfigDiffArray + "/**SCHEMA_VIEW_CONFIG_DIFF*/,"
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ }; });";

	[Test]
	[Description("Extracts the static-constants variant: button name, process name, run type, and parameter code.")]
	public void Read_Should_Extract_Constants_Variant() {
		// Arrange
		string body = WrapViewConfigDiff("""
			[
				{
					"operation": "insert",
					"name": "RunBpButton",
					"values": {
						"type": "crt.Button",
						"clicked": {
							"request": "crt.RunBusinessProcessRequest",
							"params": {
								"processName": "UsrProcess_e629820",
								"processRunType": "RegardlessOfThePage",
								"processParameters": { "ProcessSchemaParameter2": "Hello!" }
							}
						}
					},
					"parentName": "MainHeaderTop",
					"propertyName": "items",
					"index": 0
				}
			]
			""");

		// Act
		var configs = RunProcessButtonConfigReader.Read(body);

		// Assert
		configs.Should().HaveCount(1, because: "the body has one run-process button");
		configs[0].ButtonName.Should().Be("RunBpButton", because: "the enclosing operation carries the button name");
		configs[0].ProcessName.Should().Be("UsrProcess_e629820", because: "processName is read from params");
		configs[0].ProcessRunType.Should().Be("RegardlessOfThePage", because: "processRunType is read from params");
		configs[0].ParameterCodes.Should().BeEquivalentTo(new[] { "ProcessSchemaParameter2" },
			because: "processParameters keys are the referenced parameter codes");
	}

	[Test]
	[Description("Extracts processParameters keys for the attribute-binding variant ($Attr values).")]
	public void Read_Should_Extract_Attribute_Binding_Variant_Keys() {
		// Arrange
		string body = WrapViewConfigDiff("""
			[
				{
					"operation": "insert",
					"name": "RunBpButton",
					"values": {
						"type": "crt.Button",
						"clicked": {
							"request": "crt.RunBusinessProcessRequest",
							"params": {
								"processName": "UsrProcess_e629820",
								"processParameters": { "ProcessSchemaParameter2": "$SomeAttribute" }
							}
						}
					}
				}
			]
			""");

		// Act
		var configs = RunProcessButtonConfigReader.Read(body);

		// Assert
		configs.Should().HaveCount(1, because: "the body has one run-process button");
		configs[0].ParameterCodes.Should().BeEquivalentTo(new[] { "ProcessSchemaParameter2" },
			because: "the key is captured regardless of whether the value is a binding expression");
	}

	[Test]
	[Description("Captures recordIdProcessParameterName and parameterMappings keys as parameter codes.")]
	public void Read_Should_Extract_RecordId_And_Mappings_As_Codes() {
		// Arrange
		string body = WrapViewConfigDiff("""
			[
				{
					"operation": "insert",
					"name": "RecordButton",
					"values": {
						"type": "crt.Button",
						"clicked": {
							"request": "crt.RunBusinessProcessRequest",
							"params": {
								"processName": "UsrProcess_e629820",
								"processRunType": "ForTheSelectedPage",
								"recordIdProcessParameterName": "ProcessSchemaParameter1",
								"parameterMappings": { "ProcessSchemaParameter3": "Id" }
							}
						}
					}
				}
			]
			""");

		// Act
		var configs = RunProcessButtonConfigReader.Read(body);

		// Assert
		configs.Should().HaveCount(1, because: "the body has one run-process button");
		configs[0].ParameterCodes.Should().BeEquivalentTo(
			new[] { "ProcessSchemaParameter1", "ProcessSchemaParameter3" },
			because: "both recordIdProcessParameterName and parameterMappings keys are parameter codes");
	}

	[Test]
	[Description("Returns no configs for a button bound to a different (non run-process) request.")]
	public void Read_Should_Return_Empty_For_Non_RunProcess_Body() {
		// Arrange
		string body = WrapViewConfigDiff("""
			[
				{ "operation": "insert", "name": "PlainButton", "values": { "type": "crt.Button",
					"clicked": { "request": "usr.SomethingElse" } } }
			]
			""");

		// Act
		var configs = RunProcessButtonConfigReader.Read(body);

		// Assert
		configs.Should().BeEmpty(because: "only crt.RunBusinessProcessRequest buttons are collected");
	}

	[Test]
	[Description("Extracts a run-process button from a mobile page body (top-level JSON viewConfigDiff, no marker).")]
	public void Read_Should_Extract_From_Mobile_Json_Body() {
		// Arrange
		// Mobile bodies are plain JSON with viewConfigDiff at the root (no SCHEMA_VIEW_CONFIG_DIFF marker).
		string mobileBody = """
			{
				"viewConfigDiff": [
					{
						"operation": "insert",
						"name": "RunBusinessProcessButton",
						"values": {
							"type": "crt.Button",
							"clicked": {
								"request": "crt.RunBusinessProcessRequest",
								"params": {
									"processName": "UsrProcess_2fa4621",
									"processRunType": "ForTheSelectedPage",
									"recordIdProcessParameterName": "ProcessSchemaParameter1"
								}
							}
						},
						"parentName": "AreaProfileContainer",
						"propertyName": "items",
						"index": 1
					}
				]
			}
			""";

		// Act
		var configs = RunProcessButtonConfigReader.Read(mobileBody);

		// Assert
		configs.Should().HaveCount(1, because: "a mobile body exposes viewConfigDiff at the JSON root");
		configs[0].ButtonName.Should().Be("RunBusinessProcessButton", because: "the operation carries the button name");
		configs[0].ProcessName.Should().Be("UsrProcess_2fa4621", because: "processName is read from mobile params");
		configs[0].ProcessRunType.Should().Be("ForTheSelectedPage", because: "processRunType is read from mobile params");
		configs[0].ParameterCodes.Should().BeEquivalentTo(new[] { "ProcessSchemaParameter1" },
			because: "recordIdProcessParameterName is captured as a parameter code on mobile too");
	}

	[Test]
	[Description("Returns no configs for a mobile body that has no viewConfigDiff at the JSON root.")]
	public void Read_Should_Return_Empty_For_Mobile_Body_Without_ViewConfigDiff() {
		// Arrange
		string mobileBody = """{ "modelConfigDiff": [] }""";

		// Act
		var configs = RunProcessButtonConfigReader.Read(mobileBody);

		// Assert
		configs.Should().BeEmpty(because: "without a root viewConfigDiff there is nothing to parse on mobile");
	}

	[Test]
	[Description("Returns no configs when the view-config-diff marker is absent from the body.")]
	public void Read_Should_Return_Empty_When_Marker_Missing() {
		// Arrange
		string body = "define(\"X\", [], function(){ return {}; });";

		// Act
		var configs = RunProcessButtonConfigReader.Read(body);

		// Assert
		configs.Should().BeEmpty(because: "without the SCHEMA_VIEW_CONFIG_DIFF marker there is nothing to parse");
	}

	[Test]
	[Description("Is best-effort: invalid JSON in the marker yields an empty result instead of throwing.")]
	public void Read_Should_Not_Throw_On_Invalid_Json() {
		// Arrange
		string body = WrapViewConfigDiff("[ this is not json ]");

		// Act
		System.Action read = () => RunProcessButtonConfigReader.Read(body);

		// Assert
		read.Should().NotThrow(because: "the reader must never break unrelated page edits on malformed JSON");
		RunProcessButtonConfigReader.Read(body).Should().BeEmpty(because: "unparseable content yields no configs");
	}

	[Test]
	[Description("Structural validation accepts both quoted-string and bare JSON literal parameter values.")]
	public void ValidateRunProcessButtonStructure_Should_Accept_Both_String_And_Literal_Values() {
		// Arrange
		// Both quoted strings and bare JSON literals (number/boolean) are valid: the RunProcess
		// contract coerces them to the string NameValuePair.Value and the engine parses them
		// (verified end-to-end via a process formula). update-page must NOT reject literals.
		string body = WrapViewConfigDiff("""
			[
				{
					"operation": "insert",
					"name": "TypedButton",
					"values": {
						"type": "crt.Button",
						"clicked": {
							"request": "crt.RunBusinessProcessRequest",
							"params": {
								"processName": "UsrProcess_e629820",
								"processParameters": {
									"ProcessSchemaParameter1": "ok string",
									"ProcessSchemaParameter2": 3.14,
									"ProcessSchemaParameter3": 42,
									"ProcessSchemaParameter4": true
								}
							}
						}
					}
				}
			]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateRunProcessButtonStructure(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "value types are not a structural concern — only processName is");
	}

	[Test]
	[Description("Structural validation fails and names the button when processName is missing.")]
	public void ValidateRunProcessButtonStructure_Should_Fail_When_ProcessName_Missing() {
		// Arrange
		string body = WrapViewConfigDiff("""
			[
				{
					"operation": "insert",
					"name": "BrokenButton",
					"values": {
						"type": "crt.Button",
						"clicked": {
							"request": "crt.RunBusinessProcessRequest",
							"params": { "processRunType": "RegardlessOfThePage" }
						}
					}
				}
			]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateRunProcessButtonStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(because: "processName is required on a run-process button");
		result.Errors.Should().ContainSingle(because: "one button is missing processName")
			.Which.Should().Contain("BrokenButton").And.Contain("processName",
				because: "the error should name the button and the missing field");
	}

	[Test]
	[Description("Structural validation passes when the run-process button declares processName.")]
	public void ValidateRunProcessButtonStructure_Should_Pass_When_ProcessName_Present() {
		// Arrange
		string body = WrapViewConfigDiff("""
			[
				{
					"operation": "insert",
					"name": "OkButton",
					"values": {
						"type": "crt.Button",
						"clicked": {
							"request": "crt.RunBusinessProcessRequest",
							"params": { "processName": "UsrProcess_e629820", "processRunType": "RegardlessOfThePage" }
						}
					}
				}
			]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateRunProcessButtonStructure(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "a button with processName satisfies the structural rule");
		result.Errors.Should().BeEmpty(because: "no structural problems are present");
	}
}
