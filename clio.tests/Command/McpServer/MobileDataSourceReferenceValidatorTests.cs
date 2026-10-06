using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobileDataSourceReferenceValidatorTests {

	private const string BodyWithoutDataSource = """
		{
			"viewConfigDiff": [
				{ "operation": "merge", "name": "Feed", "values": { "dataSourceName": "PDS", "entitySchemaName": "UsrCarRent" } }
			],
			"viewModelConfigDiff": [
				{ "operation": "merge", "path": ["attributes"], "values": {
					"Id": { "modelConfig": { "path": "PDS.Id" } },
					"UsrName": { "modelConfig": { "path": "PDS.UsrName" } }
				} }
			],
			"modelConfigDiff": []
		}
		""";

	private const string PdsDeclaration = """
		{ "operation": "merge", "path": [], "values": {
			"dataSources": { "PDS": { "type": "crt.EntityDataSource", "config": { "entitySchemaName": "UsrCarRent" } } },
			"primaryDataSourceName": "PDS"
		} }
		""";

	[Test]
	[Description("ENG-102161: a replace body that keeps PDS bindings but sends an empty modelConfigDiff over a base without PDS is rejected.")]
	public void Validate_ReplaceBodyDropsTemplateDataSource_ReportsUndeclaredPds() {
		// Arrange
		const string replaceBase = "{}";

		// Act
		SchemaValidationResult result = MobileDataSourceReferenceValidator.Validate(BodyWithoutDataSource, () => replaceBase);

		// Assert
		result.IsValid.Should().BeFalse(because: "every PDS binding resolves to nothing once the own modelConfigDiff is gone");
		result.Errors.Should().ContainSingle(because: "all references to one missing data source collapse into one error")
			.Which.Should().Contain("'PDS'", because: "the error must name the missing data source")
			.And.Contain("attribute 'UsrName'", because: "the error must name a binding that points at it")
			.And.Contain("element 'Feed'", because: "a viewConfigDiff dataSourceName is a reference too")
			.And.Contain("update-page mode=append", because: "the error must tell the agent how to keep the template's data source")
			.And.Contain("do not re-run with validate=false",
				because: "update-page appends a generic validate=false hint, and taking it here saves the page without its data source");
	}

	[Test]
	[Description("A data source the inherited modelConfig declares satisfies the body's bindings.")]
	public void Validate_DataSourceDeclaredByTemplate_IsValid() {
		// Arrange
		const string templateModelConfig = """{ "dataSources": { "PDS": { "type": "crt.EntityDataSource" } }, "primaryDataSourceName": "PDS" }""";

		// Act
		SchemaValidationResult result = MobileDataSourceReferenceValidator.Validate(BodyWithoutDataSource, () => templateModelConfig);

		// Assert
		result.IsValid.Should().BeTrue(because: "the template owns PDS, so a replace write cannot drop it");
	}

	[Test]
	[Description("A body that declares every referenced data source never reads the inherited base.")]
	public void Validate_DataSourceDeclaredByBody_IsValidWithoutReadingBase() {
		// Arrange
		string body = BodyWithoutDataSource.Replace("\"modelConfigDiff\": []", $"\"modelConfigDiff\": [{PdsDeclaration}]");
		bool baseRead = false;

		// Act
		SchemaValidationResult result = MobileDataSourceReferenceValidator.Validate(body, () => {
			baseRead = true;
			return "{}";
		});
		bool needsBase = MobileDataSourceReferenceValidator.NeedsResolvedBase(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "the body carries the dataSources block forward");
		baseRead.Should().BeFalse(because: "a self-sufficient body must not cost a get-page read");
		needsBase.Should().BeFalse(because: "sync-pages must not pre-resolve a base the check never uses");
	}

	[Test]
	[Description("The designer's own shape — a merge into [\"dataSources\"] plus a collection attribute bound to a list data source — is valid.")]
	public void Validate_DesignerShapeWithListDataSource_IsValid() {
		// Arrange
		const string body = """
			{
				"viewConfigDiff": [],
				"viewModelConfigDiff": [
					{ "operation": "merge", "path": ["attributes"], "values": {
						"LeadName": { "modelConfig": { "path": "PDS.LeadName" } },
						"Options": {
							"isCollection": true,
							"modelConfig": { "path": "OptionsDS" },
							"viewModelConfig": { "attributes": { "OptionsDS_Number": { "modelConfig": { "path": "OptionsDS.Number" } } } }
						}
					} }
				],
				"modelConfigDiff": [
					{ "operation": "merge", "path": [], "values": { "primaryDataSourceName": "PDS" } },
					{ "operation": "merge", "path": ["dataSources"], "values": {
						"PDS": { "type": "crt.EntityDataSource" },
						"OptionsDS": { "type": "crt.EntityDataSource" }
					} }
				]
			}
			""";

		// Act
		SchemaValidationResult result = MobileDataSourceReferenceValidator.Validate(body, () => "{}");

		// Assert
		result.IsValid.Should().BeTrue(because: "both PDS and the nested list data source are declared by the body");
	}

	[Test]
	[Description("When the inherited base is unknown the check cannot tell a template-owned source from a dropped one and passes.")]
	public void Validate_BaseUnavailable_IsValid() {
		// Act
		SchemaValidationResult withoutResolver = MobileDataSourceReferenceValidator.Validate(BodyWithoutDataSource, null);
		SchemaValidationResult withFailedRead = MobileDataSourceReferenceValidator.Validate(BodyWithoutDataSource, () => null);

		// Assert
		withoutResolver.IsValid.Should().BeTrue(
			because: "validate-page has no environment and must not reject a source the template may declare");
		withFailedRead.IsValid.Should().BeTrue(because: "a failed base read must not turn into a false rejection");
	}

	[Test]
	[Description("A primaryDataSourceName that names an undeclared data source is rejected.")]
	public void Validate_PrimaryDataSourceNameUndeclared_ReportsError() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "viewModelConfigDiff": [],
			  "modelConfigDiff": [ { "operation": "merge", "path": [], "values": { "primaryDataSourceName": "PDS" } } ] }
			""";

		// Act
		SchemaValidationResult result = MobileDataSourceReferenceValidator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "the primary data source is the only reference and it is undeclared")
			.Which.Should().Contain("'PDS'", because: "the error must name the missing data source")
			.And.Contain("primaryDataSourceName", because: "the error must say which setting references it");
	}

	[Test]
	[Description("Non-string path, name, primaryDataSourceName and values are skipped instead of throwing a cast exception.")]
	public void Validate_NonStringValues_DoesNotThrow() {
		// Arrange
		const string body = """
			{
				"viewConfigDiff": [ { "operation": "merge", "name": ["Feed"], "values": { "dataSourceName": { "x": 1 } } } ],
				"viewModelConfigDiff": [
					{ "operation": "merge", "path": ["attributes"], "values": { "UsrName": { "modelConfig": { "path": { "x": 1 } } } } }
				],
				"modelConfigDiff": [
					{ "operation": "merge", "path": [], "values": [ "PDS" ] },
					{ "operation": "merge", "path": [], "values": { "primaryDataSourceName": ["PDS"] } },
					{ "operation": "merge", "path": [{ "x": 1 }], "values": "PDS" }
				]
			}
			""";

		// Act
		Func<SchemaValidationResult> validate = () => MobileDataSourceReferenceValidator.Validate(body, () => "{}");
		Func<bool> needsBase = () => MobileDataSourceReferenceValidator.NeedsResolvedBase(body);

		// Assert
		validate.Should().NotThrow(because: "a malformed body must produce a validation result, not a raw cast error")
			.Which.IsValid.Should().BeTrue(because: "none of the malformed values is a usable data source reference");
		needsBase.Should().NotThrow(because: "sync-pages calls the gate before validation and must not fail on it");
	}

	[Test]
	[Description("Control characters in a body-sourced name are flattened before the name reaches the diagnostic.")]
	public void Validate_ControlCharactersInReferrer_AreFlattened() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "merge", "path": ["attributes"], "values": {
				"Usr\nName": { "modelConfig": { "path": "PDS.UsrName" } } } } ] }
			""";

		// Act
		SchemaValidationResult result = MobileDataSourceReferenceValidator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "the one undeclared PDS reference is reported")
			.Which.Should().Contain("attribute 'Usr Name'", because: "the newline in the attribute name is replaced with a space")
			.And.NotContain("\n", because: "a body-sourced newline must not split the diagnostic in the MCP transcript");
	}

	[Test]
	[Description("A body binding to a data source it does not declare needs the inherited base, even with an empty modelConfigDiff.")]
	public void NeedsResolvedBase_BodyWithUndeclaredReference_IsTrue() {
		// Act
		bool needsBase = MobileDataSourceReferenceValidator.NeedsResolvedBase(BodyWithoutDataSource);

		// Assert
		needsBase.Should().BeTrue(
			because: "the apply oracle skips an empty modelConfigDiff, so this check is the only one that needs the base here");
	}

	[Test]
	[Description("The full mobile validation pipeline rejects the ENG-102161 body when the replace base has no PDS.")]
	public async Task RunAsync_ReplaceBodyDropsTemplateDataSource_FailsValidation() {
		// Arrange
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		PageSyncValidationResult result = await MobilePageValidation.RunAsync(
			BodyWithoutDataSource, mobileCatalog, webCatalog, resolveTemplateBase: () => (null, "{}"));

		// Assert
		result.ContentOk.Should().BeFalse(because: "sync-pages and update-page must not report success for a page with no data source");
		result.Errors.Should().Contain(e => e.Contains("'PDS'"), because: "the data-source error must reach the tool response");
	}
}
