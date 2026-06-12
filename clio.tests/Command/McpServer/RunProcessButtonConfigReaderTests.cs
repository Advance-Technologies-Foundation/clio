using System.Linq;
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
	public void Read_Should_Extract_Constants_Variant() {
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

		var configs = RunProcessButtonConfigReader.Read(body);

		configs.Should().HaveCount(1);
		configs[0].ButtonName.Should().Be("RunBpButton");
		configs[0].ProcessName.Should().Be("UsrProcess_e629820");
		configs[0].ProcessRunType.Should().Be("RegardlessOfThePage");
		configs[0].ParameterCodes.Should().BeEquivalentTo("ProcessSchemaParameter2");
	}

	[Test]
	public void Read_Should_Extract_Attribute_Binding_Variant_Keys() {
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

		var configs = RunProcessButtonConfigReader.Read(body);

		configs.Should().HaveCount(1);
		configs[0].ParameterCodes.Should().BeEquivalentTo("ProcessSchemaParameter2");
	}

	[Test]
	public void Read_Should_Extract_RecordId_And_Mappings_As_Codes() {
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

		var configs = RunProcessButtonConfigReader.Read(body);

		configs.Should().HaveCount(1);
		configs[0].ParameterCodes.Should().BeEquivalentTo("ProcessSchemaParameter1", "ProcessSchemaParameter3");
	}

	[Test]
	public void Read_Should_Return_Empty_For_Non_RunProcess_Body() {
		string body = WrapViewConfigDiff("""
			[
				{ "operation": "insert", "name": "PlainButton", "values": { "type": "crt.Button",
					"clicked": { "request": "usr.SomethingElse" } } }
			]
			""");

		RunProcessButtonConfigReader.Read(body).Should().BeEmpty();
	}

	[Test]
	public void Read_Should_Return_Empty_When_Marker_Missing() {
		RunProcessButtonConfigReader.Read("define(\"X\", [], function(){ return {}; });").Should().BeEmpty();
	}

	[Test]
	public void Read_Should_Not_Throw_On_Invalid_Json() {
		string body = WrapViewConfigDiff("[ this is not json ]");

		FluentActions.Invoking(() => RunProcessButtonConfigReader.Read(body)).Should().NotThrow();
		RunProcessButtonConfigReader.Read(body).Should().BeEmpty();
	}

	[Test]
	public void ValidateRunProcessButtonStructure_Should_Accept_Both_String_And_Literal_Values() {
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

		SchemaValidationService.ValidateRunProcessButtonStructure(body).IsValid.Should().BeTrue();
	}

	[Test]
	public void ValidateRunProcessButtonStructure_Should_Fail_When_ProcessName_Missing() {
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

		SchemaValidationResult result = SchemaValidationService.ValidateRunProcessButtonStructure(body);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle()
			.Which.Should().Contain("BrokenButton").And.Contain("processName");
	}

	[Test]
	public void ValidateRunProcessButtonStructure_Should_Pass_When_ProcessName_Present() {
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

		SchemaValidationResult result = SchemaValidationService.ValidateRunProcessButtonStructure(body);

		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}
}
