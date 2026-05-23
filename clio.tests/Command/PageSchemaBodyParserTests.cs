using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageSchemaBodyParserTests {

	[Test]
	[Description("Parses JSON5-compatible page body sections, required markers, and legacy marker aliases")]
	public void Parse_WhenBodyContainsJson5AndLegacyMarkers_ReturnsStructuredSectionsAndRawBlocks() {
		// Arrange
		IPageSchemaBodyParser parser = new PageSchemaBodyParser();
		string body = """
			define("UsrTodo_FormPage", /**SCHEMA_DEPS*/['crt.ViewElement'],/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(request, next)/**SCHEMA_ARGS*/ {
				return {
					viewConfigDiff: /**SCHEMA_DIFF*/[
						{
							operation: 'insert',
							name: 'MainContainer',
							values: {
								type: 'crt.FlexContainer',
							},
						},
					]/**SCHEMA_DIFF*/,
					viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{
						values: {
							TodoName: {
								_id: 'TodoName',
								type: 'crt.StringAttribute',
							},
						},
					}/**SCHEMA_VIEW_MODEL_CONFIG*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
						{
							operation: 'insert',
							path: ['values'],
							propertyName: 'TodoOwner',
							values: {
								_id: 'TodoOwner',
								type: 'crt.LookupAttribute',
							},
						},
					]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfig: /**SCHEMA_MODEL_CONFIG*/{
						dataSources: {
							PDS: {
								type: 'crt.EntityDataSource',
							},
						},
					}/**SCHEMA_MODEL_CONFIG*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
						{
							operation: 'insert',
							path: ['dataSources'],
							propertyName: 'SecondaryDS',
							values: {
								type: 'crt.EntityDataSource',
							},
						},
					]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS_CONFIG*/[{ request: 'crt.HandleViewModelInitRequest' }]/**SCHEMA_HANDLERS_CONFIG*/,
					converters: /**SCHEMA_CONVERTERS*/{ TodoName: value => value }/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{ TodoName: ['required'] }/**SCHEMA_VALIDATORS*/
				};
			});
			""";

		// Act
		PageParsedSchemaBody result = parser.Parse(body);

		// Assert
		(result.ViewConfigDiff as JArray).Should().ContainSingle(
			because: "the legacy SCHEMA_DIFF marker should map to the view config diff");
		result.ViewConfigDiff[0]!["name"]!.ToString().Should().Be("MainContainer",
			because: "the JSON5 diff should be parsed into structured operations");
		result.ViewModelConfig["values"]!["TodoName"]!["_id"]!.ToString().Should().Be("TodoName",
			because: "the JSON5 view-model config should be parsed into structured JSON");
		(result.ViewModelConfigDiff as JArray).Should().ContainSingle(
			because: "the view-model diff marker should be parsed as an operation array");
		result.ModelConfig["dataSources"]!["PDS"]!["type"]!.ToString().Should().Be("crt.EntityDataSource",
			because: "the model config marker should be parsed into structured JSON");
		(result.ModelConfigDiff as JArray).Should().ContainSingle(
			because: "the model config diff marker should be parsed as an operation array");
		result.Deps.Should().Be("['crt.ViewElement'],",
			because: "deps should stay as raw source text");
		result.Args.Should().Be("(request, next)",
			because: "args should stay as raw source text");
		result.Handlers.Should().Be("[{ request: 'crt.HandleViewModelInitRequest' }]",
			because: "the legacy SCHEMA_HANDLERS_CONFIG marker should be preserved as raw source text");
		result.Converters.Should().Be("{ TodoName: value => value }",
			because: "converters should remain raw source text instead of being JSON parsed");
		result.Validators.Should().Be("{ TodoName: ['required'] }",
			because: "validators should remain raw source text instead of being JSON parsed");
	}

	[Test]
	[Description("Uses empty defaults when optional page body markers are absent")]
	public void Parse_WhenMarkersAreMissing_ReturnsFallbackValues() {
		// Arrange
		IPageSchemaBodyParser parser = new PageSchemaBodyParser();
		string body = "define('UsrEmpty_FormPage', [], function() { return {}; });";

		// Act
		PageParsedSchemaBody result = parser.Parse(body);

		// Assert
		(result.ViewConfigDiff as JArray).Should().BeEmpty(
			because: "missing view config markers should fall back to an empty diff array");
		(result.ViewModelConfig as JObject).Should().BeEmpty(
			because: "missing view-model config markers should fall back to an empty object");
		(result.ModelConfig as JObject).Should().BeEmpty(
			because: "missing model config markers should fall back to an empty object");
		result.Deps.Should().Be("[]",
			because: "missing deps markers should fall back to an empty dependency list");
		result.Args.Should().Be("()",
			because: "missing args markers should fall back to an empty AMD argument list");
		result.Handlers.Should().Be("[]",
			because: "missing handlers markers should fall back to an empty handlers array");
		result.Converters.Should().Be("{}",
			because: "missing converters markers should fall back to an empty converters object");
		result.Validators.Should().Be("{}",
			because: "missing validators markers should fall back to an empty validators object");
	}

	[Test]
	[Description("Parses JSON5 section syntax including comments, single-quoted strings, and trailing commas")]
	public void Parse_WhenSectionContainsJson5Features_ReturnsStructuredJson() {
		IPageSchemaBodyParser parser = new PageSchemaBodyParser();
		string body = """
			define("UsrJson5_FormPage", [], function() {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
						{
							// root insertion
							operation: 'insert',
							name: 'MainContainer',
							values: {
								type: 'crt.FlexContainer',
							},
						},
					]/**SCHEMA_VIEW_CONFIG_DIFF*/
				};
			});
			""";

		PageParsedSchemaBody result = parser.Parse(body);

		(result.ViewConfigDiff as JArray).Should().ContainSingle(
			because: "JSON5 arrays with comments and trailing commas should still parse");
		result.ViewConfigDiff[0]!["operation"]!.ToString().Should().Be("insert",
			because: "single-quoted string values should deserialize correctly");
		result.ViewConfigDiff[0]!["values"]!["type"]!.ToString().Should().Be("crt.FlexContainer",
			because: "nested JSON5 objects should be preserved in the parsed result");
	}

        [Test]
        [Description("Parses a plain JSON mobile body (schemaType=10) and returns viewConfigDiff/viewModelConfigDiff/modelConfigDiff as JArrays with AMD defaults for the AMD-only sections.")]
        public void Parse_WhenBodyIsPlainJsonMobileBody_ReturnsMobileParsedSections() {
                // Arrange
                IPageSchemaBodyParser parser = new PageSchemaBodyParser();
                string body = """
                        {
                          "viewConfigDiff": [
                            { "operation": "insert", "name": "TestComponent" }
                          ],
                          "viewModelConfigDiff": [
                            { "operation": "insert", "name": "TestAttr" }
                          ],
                          "modelConfigDiff": [
                            { "operation": "insert", "name": "TestModel" }
                          ]
                        }
                        """;

                // Act
                PageParsedSchemaBody result = parser.Parse(body);

                // Assert
                (result.ViewConfigDiff as JArray)!.Should().ContainSingle(
                        because: "viewConfigDiff from a mobile JSON body should be parsed as a JArray with one entry");
                result.ViewConfigDiff[0]!["operation"]!.ToString().Should().Be("insert",
                        because: "mobile viewConfigDiff operations should be preserved exactly");
                (result.ViewModelConfigDiff as JArray)!.Should().ContainSingle(
                        because: "viewModelConfigDiff from a mobile JSON body should be parsed as a JArray");
                (result.ModelConfigDiff as JArray)!.Should().ContainSingle(
                        because: "modelConfigDiff from a mobile JSON body should be parsed as a JArray");
                result.Deps.Should().Be("[]",
                        because: "mobile pages have no AMD deps section — should fall back to empty array");
                result.Args.Should().Be("()",
                        because: "mobile pages have no AMD args section — should fall back to empty args");
                result.Handlers.Should().Be("[]",
                        because: "mobile pages have no handlers section — should fall back to empty array");
                result.Converters.Should().Be("{}",
                        because: "mobile pages have no converters section — should fall back to empty object");
                result.Validators.Should().Be("{}",
                        because: "mobile pages have no validators section — should fall back to empty object");
        }

        [Test]
        [Description("Parses a mobile body with empty diff arrays and returns empty JArrays without throwing.")]
        public void Parse_WhenMobileBodyHasEmptyDiffArrays_ReturnsEmptyArrays() {
                // Arrange
                IPageSchemaBodyParser parser = new PageSchemaBodyParser();
                string body = """
                        {
                          "viewConfigDiff": [],
                          "viewModelConfigDiff": [],
                          "modelConfigDiff": []
                        }
                        """;

                // Act
                PageParsedSchemaBody result = parser.Parse(body);

                // Assert
                (result.ViewConfigDiff as JArray)!.Should().BeEmpty(
                        because: "empty viewConfigDiff in a mobile body should parse to an empty JArray");
                (result.ViewModelConfigDiff as JArray)!.Should().BeEmpty(
                        because: "empty viewModelConfigDiff in a mobile body should parse to an empty JArray");
                (result.ModelConfigDiff as JArray)!.Should().BeEmpty(
                        because: "empty modelConfigDiff in a mobile body should parse to an empty JArray");
        }

        [Test]
        [Description("Parses a mobile body that omits optional diff sections without throwing.")]
        public void Parse_WhenMobileBodyOmitsSomeSections_ReturnsEmptyFallbacksForMissingSections() {
                // Arrange
                IPageSchemaBodyParser parser = new PageSchemaBodyParser();
                string body = "{ \"viewConfigDiff\": [ { \"operation\": \"merge\", \"name\": \"Scaffold\" } ] }";

                // Act
                PageParsedSchemaBody result = parser.Parse(body);

                // Assert
                (result.ViewConfigDiff as JArray)!.Should().ContainSingle(
                        because: "the present section should be parsed correctly");
                (result.ViewModelConfigDiff as JArray)!.Should().BeEmpty(
                        because: "omitted viewModelConfigDiff should fall back to an empty JArray");
                (result.ModelConfigDiff as JArray)!.Should().BeEmpty(
                        because: "omitted modelConfigDiff should fall back to an empty JArray");
        }
}
