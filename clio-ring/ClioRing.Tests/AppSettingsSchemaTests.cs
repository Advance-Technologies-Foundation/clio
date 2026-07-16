using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ClioRing.Models;
using FluentAssertions;
using Json.Schema;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Guards the shipped ClioRing application-settings sample and its editor schema against packaging and documentation drift.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class AppSettingsSchemaTests {
	private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);
	private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "app-settings.json");
	private static string SchemaPath => Path.Combine(AppContext.BaseDirectory, "app-settings.schema.json");
	private static string DesktopProjectPath => Path.Combine(AppContext.BaseDirectory, "ClioRing.Desktop.csproj");

	[Test]
	[Description("The shipped application settings satisfy the valid Draft 2020-12 schema copied beside the Ring test binaries.")]
	public void ShippedSettings_ShouldSatisfySchema_WhenCopiedToOutput() {
		// Arrange
		JsonSchema schema = Schema.Value;
		using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(SettingsPath));

		// Act
		EvaluationResults result = schema.Evaluate(settings.RootElement);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "building the schema validates its keywords and references, and the shipped sample must satisfy that contract");
	}

	[Test]
	[Description("The shipped settings file references the colocated schema used by JSON editors.")]
	public void Settings_ShouldReferenceColocatedSchema_WhenShipped() {
		// Arrange
		JsonObject settings = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();

		// Act
		string? schemaReference = settings["$schema"]?.GetValue<string>();

		// Assert
		schemaReference.Should().Be("./app-settings.schema.json",
			because: "a relative reference continues to work after build, publish, or local installation");
	}

	[Test]
	[Description("The Desktop shipping project copies the application-settings schema into build and publish output.")]
	public void DesktopProject_ShouldCopySchema_WhenBuiltOrPublished() {
		// Arrange
		XDocument project = XDocument.Load(DesktopProjectPath);

		// Act
		XElement? schemaItem = project.Descendants("None")
			.SingleOrDefault(item => string.Equals(
				(string?)item.Attribute("Update"), "app-settings.schema.json", StringComparison.Ordinal));
		string? outputCopyMode = (string?)schemaItem?.Element("CopyToOutputDirectory");
		string? publishCopyMode = (string?)schemaItem?.Element("CopyToPublishDirectory");

		// Assert
		schemaItem.Should().NotBeNull(
			because: "the schema must be part of the Desktop project's real shipping contract, not only the test output");
		outputCopyMode.Should().Be("PreserveNewest",
			because: "build output must copy the schema beside app-settings.json");
		publishCopyMode.Should().Be("PreserveNewest",
			because: "publish output must explicitly retain the schema beside app-settings.json");
	}

	[Test]
	[Description("The schema covers every supported root setting and explains channel and development-clio selection semantics.")]
	public void Schema_ShouldDescribeSupportedSettingsAndClioSelection_WhenReadByEditor() {
		// Arrange
		JsonObject schema = JsonNode.Parse(File.ReadAllText(SchemaPath))!.AsObject();
		JsonObject properties = schema["properties"]!.AsObject();
		JsonObject definitions = schema["$defs"]!.AsObject();
		string[] expectedProperties = typeof(AppSettings).GetProperties()
			.Select(property => property.Name).Append("$schema").ToArray();
		string[] expectedExperimentProperties = typeof(ExperimentSettings).GetProperties()
			.Select(property => property.Name).ToArray();
		string[] expectedIpcProperties = typeof(ClioIpcSettingsDto).GetProperties()
			.Select(property => property.Name).ToArray();

		// Act
		string[] actualProperties = properties.Select(property => property.Key).ToArray();
		string[] actualExperimentProperties = definitions["experiments"]!["properties"]!.AsObject()
			.Select(property => property.Key).ToArray();
		string[] actualIpcProperties = definitions["clioIpc"]!["properties"]!.AsObject()
			.Select(property => property.Key).ToArray();
		string channelDescription = properties["Channel"]!["description"]!.GetValue<string>();
		string devPathDescription = properties["DevClioPath"]!["description"]!.GetValue<string>();
		string ipcDescription = definitions["clioIpc"]!["description"]!.GetValue<string>();

		// Assert
		actualProperties.Should().BeEquivalentTo(expectedProperties,
			because: "the editor contract must stay aligned with every root AppSettings property plus $schema");
		actualExperimentProperties.Should().BeEquivalentTo(expectedExperimentProperties,
			because: "the editor contract must stay aligned with every ExperimentSettings property");
		actualIpcProperties.Should().BeEquivalentTo(expectedIpcProperties,
			because: "the editor contract must stay aligned with every ClioIpcSettingsDto property");
		channelDescription.Should().Contain("does not select the clio executable",
			because: "Channel is only a Ring display/deployment label");
		devPathDescription.Should().Contain("takes precedence over ClioIpc",
			because: "developers need the actual launch-selection precedence");
		ipcDescription.Should().Contain("when DevClioPath is absent or invalid",
			because: "the explicit child-process block is the second precedence level");
	}

	[TestCase("""{"WorkspaceFolder":"C:\\Workspaces","Unexpected":true}""")]
	[TestCase("""{"WorkspaceFolder":"   "}""")]
	[TestCase("""{"WorkspaceFolder":"C:\\Workspaces","ClioIpc":{"Command":"dotnet"}}""")]
	[TestCase("""{"WorkspaceFolder":"C:\\Workspaces","Channel":"   "}""")]
	[TestCase("""{"WorkspaceFolder":"C:\\Workspaces","ClioIpc":{"Command":"   ","Args":["mcp-server"]}}""")]
	[TestCase("""{"WorkspaceFolder":"C:\\Workspaces","ClioIpc":{"Command":"dotnet","Args":["   "]}}""")]
	[Description("The schema rejects unknown settings, incomplete or blank child launch configuration, and blank channel labels.")]
	public void Schema_ShouldRejectInvalidSettings_WhenEditorValidates(string json) {
		// Arrange
		JsonSchema schema = Schema.Value;
		using JsonDocument settings = JsonDocument.Parse(json);

		// Act
		EvaluationResults result = schema.Evaluate(settings.RootElement);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the schema should catch misspelled properties and incomplete or meaningless configuration before Ring starts");
	}

	private static JsonSchema LoadSchema() {
		using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(SchemaPath));
		return JsonSchema.Build(schemaDocument.RootElement.Clone());
	}
}
