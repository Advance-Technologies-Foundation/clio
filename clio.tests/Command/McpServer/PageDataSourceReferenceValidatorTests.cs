using System;
using System.Linq;
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
public sealed class PageDataSourceReferenceValidatorTests {

	private const string MobileBodyWithoutDataSource = """
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

	private const string PdsRootDeclaration = """
		{ "operation": "merge", "path": [], "values": {
			"dataSources": { "PDS": { "type": "crt.EntityDataSource", "config": { "entitySchemaName": "UsrCarRent" } } },
			"primaryDataSourceName": "PDS"
		} }
		""";

	private const string WebTemplateModelConfig = """{ "dataSources": { "AttachmentListDS": { "type": "crt.EntityDataSource" } } }""";

	private const string GeneratedWebViewConfigDiff = """
		[ { "operation": "merge", "name": "Feed", "values": { "dataSourceName": "PDS", "entitySchemaName": "UsrPdsRepro" } } ]
		""";

	private const string GeneratedWebViewModelConfig = """
		{ "attributes": {
			"UsrName": { "modelConfig": { "path": "PDS.UsrName" } },
			"Id": { "modelConfig": { "path": "PDS.Id" } } } }
		""";

	private const string GeneratedWebModelConfig = """
		{ "dataSources": {
			"PDS": { "type": "crt.EntityDataSource", "config": { "entitySchemaName": "UsrPdsRepro" }, "scope": "page" },
			"AttachmentListDS": { "type": "crt.EntityDataSource" } },
		  "primaryDataSourceName": "PDS" }
		""";

	private static PageDataSourceReferenceValidator CreateValidator() => new(new PageSchemaBodyParser());

	private static string WebBody(string viewConfigDiff = "[]", string? viewModelConfig = null,
		string? viewModelConfigDiff = null, string? modelConfig = null, string? modelConfigDiff = null) =>
		"define(\"UsrPdsRepro_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		$"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{viewConfigDiff}/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		(viewModelConfig is null ? "" : $"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{viewModelConfig}/**SCHEMA_VIEW_MODEL_CONFIG*/, ") +
		(viewModelConfigDiff is null ? "" : $"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{viewModelConfigDiff}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, ") +
		(modelConfig is null ? "" : $"modelConfig: /**SCHEMA_MODEL_CONFIG*/{modelConfig}/**SCHEMA_MODEL_CONFIG*/, ") +
		(modelConfigDiff is null ? "" : $"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{modelConfigDiff}/**SCHEMA_MODEL_CONFIG_DIFF*/, ") +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static Func<string> CountingResolver(string baseModelConfig, Action onRead) => () => {
		onRead();
		return baseModelConfig;
	};

	[Test]
	[Description("ENG-102161: a mobile replace body that keeps PDS bindings but sends an empty modelConfigDiff over a resolved base without PDS is rejected.")]
	public void Validate_MobileReplaceBodyDropsTemplateDataSource_ReportsUndeclaredPds() {
		// Arrange
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(MobileBodyWithoutDataSource, () => "{}");

		// Assert
		result.IsValid.Should().BeFalse(because: "every PDS binding resolves to nothing once the own modelConfigDiff is gone");
		result.Errors.Should().ContainSingle(because: "all references to one missing data source collapse into one error")
			.Which.Should().Contain("'PDS'", because: "the error must name the missing data source")
			.And.Contain("attribute 'UsrName'", because: "the error must name a binding that points at it")
			.And.Contain("element 'Feed'", because: "a viewConfigDiff dataSourceName is a reference too")
			.And.Contain("Mobile Designer", because: "the mobile remedy names the mobile designer")
			.And.Contain("do not re-run with validate=false",
				because: "update-page appends a generic validate=false hint, and taking it here saves the page without its data source");
	}

	[Test]
	[Description("A data source the inherited modelConfig declares satisfies the body's bindings.")]
	public void Validate_DataSourceDeclaredByTemplate_IsValid() {
		// Arrange
		const string templateModelConfig = """{ "dataSources": { "PDS": { "type": "crt.EntityDataSource" } } }""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(MobileBodyWithoutDataSource, () => templateModelConfig);

		// Assert
		result.IsValid.Should().BeTrue(because: "the template owns PDS, so a replace write cannot drop it");
	}

	[Test]
	[Description("A body that declares every referenced data source at the root never reads the inherited base.")]
	public void Validate_DataSourceDeclaredByBody_IsValidWithoutReadingBase() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []", $"\"modelConfigDiff\": [{PdsRootDeclaration}]");
		int baseReads = 0;
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, CountingResolver("{}", () => baseReads++));
		bool needsBase = validator.NeedsResolvedBase(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "the body carries the dataSources block forward");
		baseReads.Should().Be(0, because: "a self-sufficient body must not cost a get-page read");
		needsBase.Should().BeFalse(because: "sync-pages must not pre-resolve a base the check never uses");
	}

	[Test]
	[Description("The designer's own shape — a merge into [\"dataSources\"] plus a list data source — is valid over a base that has a dataSources object.")]
	public void Validate_MergeIntoDataSourcesOverTemplate_IsValid() {
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
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": {} }""");

		// Assert
		result.IsValid.Should().BeTrue(because: "the merge lands in the template's dataSources object and declares both sources");
	}

	[Test]
	[Description("A merge into [\"dataSources\"] over a base without that key is dropped by the differ, so it declares nothing.")]
	public void Validate_MergeIntoDataSourcesWithoutBaseKey_IsRejected() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "merge", "path": ["dataSources"], "values": { "PDS": { "type": "crt.EntityDataSource" } } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "JsonPathDiffApplier.Merge is a silent no-op when the target path is absent")
			.Which.Should().Contain("'PDS'", because: "the error must name the data source the dropped merge meant to declare");
	}

	[Test]
	[Description("A remove of a data source the page binds to is rejected, even when the template declares it.")]
	public void Validate_RemoveOperation_IsNotADeclaration() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "remove", "path": ["dataSources", "PDS"] } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": { "PDS": {} } }""");

		// Assert
		result.Errors.Should().ContainSingle(because: "the body removes the very data source it still binds to")
			.Which.Should().Contain("'PDS'", because: "the error must name the removed data source");
	}

	[Test]
	[Description("A merge below a data source that the base does not have declares nothing.")]
	public void Validate_MergeIntoMissingDataSource_IsNotADeclaration() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "merge", "path": ["dataSources", "PDS", "config"], "values": { "entitySchemaName": "UsrCarRent" } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": {} }""");

		// Assert
		result.Errors.Should().ContainSingle(because: "merging into PDS.config does not create PDS")
			.Which.Should().Contain("'PDS'", because: "the error must name the missing data source");
	}

	[Test]
	[Description("With no base available (validate-page) the check cannot reject; it passes and says the bindings were not checked.")]
	public void Validate_NoBaseAvailable_PassesWithWarning() {
		// Arrange
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(MobileBodyWithoutDataSource, null);

		// Assert
		result.IsValid.Should().BeTrue(because: "validate-page has no environment and must not reject a source the template may declare");
		result.Warnings.Should().ContainSingle(because: "a check that could not run must be visible to the caller")
			.Which.Should().Contain("PDS", because: "the warning names the unchecked data source")
			.And.Contain("no inherited modelConfig is available", because: "validate-page never tried to read a base")
			.And.NotContain("could not be read", because: "nothing failed: there was no base to read");
	}

	[Test]
	[Description("When reading the base fails the check passes and says the bindings were not checked.")]
	public void Validate_BaseReadFailed_PassesWithWarning() {
		// Arrange
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(MobileBodyWithoutDataSource, () => null);

		// Assert
		result.IsValid.Should().BeTrue(because: "a failed base read must not turn into a false rejection");
		result.Warnings.Should().ContainSingle(because: "a skipped check must be visible to the caller")
			.Which.Should().Contain("were not checked", because: "the caller must not read the pass as a clean result")
			.And.Contain("could not be read", because: "the warning says why the check did not run");
	}

	[Test]
	[Description("A body that binds to a data source it does not declare needs the inherited base.")]
	public void NeedsResolvedBase_BodyWithUndeclaredReference_IsTrue() {
		// Arrange
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		bool needsBase = validator.NeedsResolvedBase(MobileBodyWithoutDataSource);

		// Assert
		needsBase.Should().BeTrue(because: "PDS is bound but declared only in the page's own body, which a replace overwrites");
	}

	[Test]
	[Description("A remove with properties on a data source strips those keys only; the data source stays declared.")]
	public void Validate_RemovePropertiesFromDataSource_KeepsItDeclared() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "remove", "path": ["dataSources", "PDS"], "properties": ["scopes"] } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": { "PDS": { "scopes": [] } } }""");

		// Assert
		result.IsValid.Should().BeTrue(because: "the differ routes a remove with properties to the property group and keeps PDS");
	}

	[Test]
	[Description("Removing a key from [\"dataSources\"] runs after inserts, so it undoes an insert of the same name.")]
	public void Validate_RemovePropertiesAfterInsert_LeavesSourceUndeclared() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "remove", "path": ["dataSources"], "properties": ["PDS"] }, { "operation": "insert", "path": ["dataSources"], "propertyName": "PDS", "values": { "type": "crt.EntityDataSource" } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": {} }""");

		// Assert
		result.Errors.Should().ContainSingle(because: "the property remove runs after the insert and deletes PDS again")
			.Which.Should().Contain("'PDS'", because: "the error must name the removed data source");
	}

	[Test]
	[Description("The differ dispatches on the exact operation string, so a \"Merge\" declares nothing.")]
	public void Validate_OperationKindWithWrongCase_IsNotADeclaration() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "Merge", "path": [], "values": { "dataSources": { "PDS": {} } } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "a Merge operation is a no-op in the differ")
			.Which.Should().Contain("'PDS'", because: "the error must name the data source the no-op meant to declare");
	}

	[Test]
	[Description("A set on [\"dataSources\"] replaces the whole set, so a source only the base declared is gone.")]
	public void Validate_SetOnDataSources_ReplacesTheSet() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "set", "path": ["dataSources"], "values": { "OtherDS": {} } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": { "PDS": {} } }""");

		// Assert
		result.Errors.Should().ContainSingle(because: "the set removes the dataSources object and inserts only OtherDS")
			.Which.Should().Contain("'PDS'", because: "the error must name the data source the set dropped");
	}

	[Test]
	[Description("An attribute inserted under propertyName is named by that propertyName in the error.")]
	public void Validate_InsertedAttribute_IsLabelledByPropertyName() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "insert", "path": ["attributes"], "propertyName": "UsrName",
				"values": { "modelConfig": { "path": "PDS.UsrName" } } } ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "the inserted attribute binds to the missing PDS")
			.Which.Should().Contain("attribute 'UsrName'", because: "the agent must be pointed at the attribute it inserted")
			.And.NotContain("attribute 'attributes'", because: "the path segment is not the attribute's name");
	}

	[Test]
	[Description("Line separators and bidi controls in a body-sourced name are flattened before the name reaches the diagnostic.")]
	public void Validate_UnicodeSeparatorsInReferrer_AreFlattened() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "merge", "path": ["attributes"], "values": {
				"Usr\u2028Na\u202Eme": { "modelConfig": { "path": "PDS.UsrName" } } } } ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "the one undeclared PDS reference is reported")
			.Which.Should().Contain("attribute 'Usr Na me'", because: "U+2028 and U+202E are replaced with spaces")
			.And.NotContain("\u2028", because: "a line separator must not split the diagnostic in the MCP transcript")
			.And.NotContain("\u202E", because: "a bidi override must not reorder the diagnostic");
	}

	[Test]
	[Description("A root primaryDataSourceName that names an undeclared data source is rejected.")]
	public void Validate_PrimaryDataSourceNameUndeclared_ReportsError() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "viewModelConfigDiff": [],
			  "modelConfigDiff": [ { "operation": "merge", "path": [], "values": { "primaryDataSourceName": "PDS" } } ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.IsValid.Should().BeFalse(because: "the primary data source must exist");
		result.Errors.Should().ContainSingle(because: "the primary data source is the only reference and it is undeclared")
			.Which.Should().Contain("'PDS'", because: "the error must name the missing data source")
			.And.Contain("primaryDataSourceName", because: "the error must say which setting references it");
	}

	[Test]
	[Description("A primaryDataSourceName that only the template sets is not blamed on the body.")]
	public void Validate_TemplatePrimaryDataSourceUndeclared_IsNotReported() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "merge", "path": ["attributes"], "values": {
				"Items": { "modelConfig": { "path": "ListDS" } } } } ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": { "ListDS": {} }, "primaryDataSourceName": "PDS" }""");

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the body references only ListDS, which the template declares; the template's own primary is not the body's edit");
	}

	[Test]
	[Description("Path-addressed attribute merges are references, labelled with the attribute name.")]
	public void Validate_PathAddressedAttributeMerges_AreReferences() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [
				{ "operation": "merge", "path": ["attributes", "UsrName", "modelConfig"], "values": { "path": "PDS.UsrName" } },
				{ "operation": "merge", "path": ["attributes", "UsrPhone"], "values": { "modelConfig": { "path": "PDS.UsrPhone" } } }
			  ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "both merges bind to the missing PDS")
			.Which.Should().Contain("attribute 'UsrName'", because: "a merge into attributes.X.modelConfig names attribute X")
			.And.Contain("attribute 'UsrPhone'", because: "a merge into attributes.X names attribute X, not 'values'");
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
					{ "operation": ["merge"], "path": [{ "x": 1 }], "values": "PDS" }
				]
			}
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		Func<SchemaValidationResult> validate = () => validator.Validate(body, () => "{}");
		Func<bool> needsBase = () => validator.NeedsResolvedBase(body);

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
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "the one undeclared PDS reference is reported")
			.Which.Should().Contain("attribute 'Usr Name'", because: "the newline in the attribute name is replaced with a space")
			.And.NotContain("\n", because: "a body-sourced newline must not split the diagnostic in the MCP transcript");
	}

	[Test]
	[Description("The error lists at most three referrers and counts the rest.")]
	public void Validate_ManyReferrers_AreCapped() {
		// Arrange
		const string body = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "merge", "path": ["attributes"], "values": {
				"A": { "modelConfig": { "path": "PDS.A" } }, "B": { "modelConfig": { "path": "PDS.B" } },
				"C": { "modelConfig": { "path": "PDS.C" } }, "D": { "modelConfig": { "path": "PDS.D" } },
				"E": { "modelConfig": { "path": "PDS.E" } } } } ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "five references to one missing data source collapse into one error")
			.Which.Should().Contain("and 2 more", because: "only three referrers are listed by name")
			.And.NotContain("attribute 'D'", because: "the fourth referrer is counted, not listed");
	}

	[Test]
	[Description("W1: a generated web form body with its own full SCHEMA_MODEL_CONFIG settles every binding without reading the base.")]
	public void Validate_GeneratedWebBody_IsValidWithoutReadingBase() {
		// Arrange
		string body = WebBody(GeneratedWebViewConfigDiff, GeneratedWebViewModelConfig, modelConfig: GeneratedWebModelConfig);
		int baseReads = 0;
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, CountingResolver(WebTemplateModelConfig, () => baseReads++));

		// Assert
		result.IsValid.Should().BeTrue(because: "the full model config declares PDS and AttachmentListDS");
		baseReads.Should().Be(0, because: "a self-sufficient web body needs no get-page read");
	}

	[Test]
	[Description("W2: the generated web body with an empty SCHEMA_MODEL_CONFIG_DIFF instead of its full model config is rejected with the web remedy.")]
	public void Validate_WebBodyWithEmptyModelConfigDiff_IsRejected() {
		// Arrange
		string body = WebBody(GeneratedWebViewConfigDiff, GeneratedWebViewModelConfig, modelConfigDiff: "[]");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => WebTemplateModelConfig);

		// Assert
		result.IsValid.Should().BeFalse(because: "the template declares only AttachmentListDS, so PDS is gone");
		result.Errors.Should().ContainSingle(because: "every reference points at the one missing PDS")
			.Which.Should().Contain("attribute 'UsrName'", because: "full SCHEMA_VIEW_MODEL_CONFIG bindings are references")
			.And.Contain("SCHEMA_MODEL_CONFIG", because: "the web remedy names the web body sections")
			.And.Contain("Freedom UI Designer", because: "the web remedy names the web designer")
			.And.NotContain("Mobile Designer", because: "the mobile remedy does not apply to a web page");
	}

	[Test]
	[Description("W3: a web full SCHEMA_MODEL_CONFIG merges with the template, so a binding to a template-only data source is valid.")]
	public void Validate_WebFullModelConfigMergesWithTemplate_IsValid() {
		// Arrange
		const string viewModelConfig = """
			{ "attributes": { "AttachmentList": { "isCollection": true, "modelConfig": { "path": "AttachmentListDS" } },
			  "UsrName": { "modelConfig": { "path": "PDS.UsrName" } } } }
			""";
		const string ownModelConfig = """{ "dataSources": { "PDS": { "type": "crt.EntityDataSource" } }, "primaryDataSourceName": "PDS" }""";
		string body = WebBody(viewModelConfig: viewModelConfig, modelConfig: ownModelConfig);
		int baseReads = 0;
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, CountingResolver(WebTemplateModelConfig, () => baseReads++));

		// Assert
		result.IsValid.Should().BeTrue(because: "the bundle builder deep-merges the own config into the template's");
		baseReads.Should().Be(1, because: "AttachmentListDS is declared only by the template, so the base must be read");
	}

	[Test]
	[Description("W4: a web skeleton body binds to nothing and needs no base.")]
	public void Validate_WebSkeleton_IsValidWithoutReadingBase() {
		// Arrange
		string body = WebBody(viewModelConfigDiff: "[]", modelConfigDiff: "[]");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		bool needsBase = validator.NeedsResolvedBase(body);
		SchemaValidationResult result = validator.Validate(body, () => throw new InvalidOperationException("no read expected"));

		// Assert
		needsBase.Should().BeFalse(because: "a body with no bindings has nothing to check");
		result.IsValid.Should().BeTrue(because: "a skeleton cannot drop a data source it never binds to");
	}

	[Test]
	[Description("A full model config next to a non-empty model config diff is ignored, as the bundle builder ignores it.")]
	public void Validate_InlineModelConfigWithNonEmptyDiff_IsIgnored() {
		// Arrange
		string body = WebBody(GeneratedWebViewConfigDiff, GeneratedWebViewModelConfig, modelConfig: GeneratedWebModelConfig,
			modelConfigDiff: """[ { "operation": "merge", "path": [], "values": { "primaryDataSourceName": "AttachmentListDS" } } ]""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => WebTemplateModelConfig);

		// Assert
		result.Errors.Should().ContainSingle(because: "the builder applies the diff and drops the full config, so PDS is never declared")
			.Which.Should().Contain("'PDS'", because: "the error must name the data source only the ignored config declared");
	}

	[Test]
	[Description("A #PrimaryDataSourceName()# macro binding, which templates use, is resolved at runtime and is not a data-source reference.")]
	public void Validate_PrimaryDataSourceMacroBinding_IsNotReported() {
		// Arrange
		const string mobileBody = """
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "merge", "path": ["attributes"], "values": {
				"Id": { "modelConfig": { "path": "#PrimaryDataSourceName()#.Id" } } } } ] }
			""";
		string webBody = WebBody(viewModelConfig: """{ "attributes": { "Id": { "modelConfig": { "path": "#PrimaryDataSourceName()#.Id" } } } }""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult mobileResult = validator.Validate(mobileBody, () => "{}");
		SchemaValidationResult webResult = validator.Validate(webBody, () => "{}");
		bool needsBase = validator.NeedsResolvedBase(mobileBody);

		// Assert
		mobileResult.IsValid.Should().BeTrue(because: "PageWithTabsFreedomTemplate binds Id through this macro, so a copied attribute must not be rejected");
		webResult.IsValid.Should().BeTrue(because: "the macro is not a dataSources key on web either");
		needsBase.Should().BeFalse(because: "a macro is not a reference, so it never costs a get-page read");
	}

	[Test]
	[Description("An insert into [\"dataSources\"] declares the data source named by propertyName, not the keys of its values.")]
	public void Validate_InsertIntoDataSources_DeclaresPropertyName() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "insert", "path": ["dataSources"], "propertyName": "PDS",
				"values": { "type": "crt.EntityDataSource", "config": { "entitySchemaName": "UsrCarRent" } } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => """{ "dataSources": {} }""");

		// Assert
		result.IsValid.Should().BeTrue(because: "JsonDiffApplier.Insert writes values under propertyName, which is PDS");
	}

	[Test]
	[Description("A merge that declares a data source followed by its removal leaves it undeclared, because the differ applies merges before removes.")]
	public void Validate_MergeThenRemoveOfSameSource_IsRejected() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			$$"""
			"modelConfigDiff": [ {{PdsRootDeclaration}}, { "operation": "remove", "path": ["dataSources", "PDS"] } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "the remove runs after the merge and takes PDS away")
			.Which.Should().Contain("'PDS'", because: "the error must name the removed data source");
	}

	[Test]
	[Description("A merge with no path is not a root operation: the differ cannot locate it, so it declares nothing.")]
	public void Validate_MergeWithoutPath_IsNotADeclaration() {
		// Arrange
		string body = MobileBodyWithoutDataSource.Replace("\"modelConfigDiff\": []",
			"""
			"modelConfigDiff": [ { "operation": "merge", "values": { "dataSources": { "PDS": {} } } } ]
			""");
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().ContainSingle(because: "a pathless, nameless merge is a no-op in the differ")
			.Which.Should().Contain("'PDS'", because: "the error must name the data source the no-op meant to declare");
	}

	[Test]
	[Description("The number of data-source errors is capped so a body with many undeclared names cannot flood the response.")]
	public void Validate_ManyUndeclaredDataSources_AreCapped() {
		// Arrange
		string attributes = string.Join(", ", Enumerable.Range(1, 15).Select(i => $$"""
			"A{{i}}": { "modelConfig": { "path": "DS{{i}}.Name" } }
			"""));
		string body = $$"""
			{ "viewConfigDiff": [], "modelConfigDiff": [],
			  "viewModelConfigDiff": [ { "operation": "merge", "path": ["attributes"], "values": { {{attributes}} } } ] }
			""";
		PageDataSourceReferenceValidator validator = CreateValidator();

		// Act
		SchemaValidationResult result = validator.Validate(body, () => "{}");

		// Assert
		result.Errors.Should().HaveCount(11, because: "ten data sources are reported in full and the rest are counted")
			.And.Contain(e => e.Contains("5 more undeclared data sources"), because: "the remainder must still be visible");
	}

	[Test]
	[Description("The full mobile validation pipeline rejects the ENG-102161 body when the replace base has no PDS.")]
	public async Task RunAsync_MobileReplaceBodyDropsTemplateDataSource_FailsValidation() {
		// Arrange
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();

		// Act
		PageSyncValidationResult result = await MobilePageValidation.RunAsync(
			MobileBodyWithoutDataSource, mobileCatalog, webCatalog, CreateValidator(), resolveTemplateBase: () => (null, "{}"));

		// Assert
		result.ContentOk.Should().BeFalse(because: "sync-pages and update-page must not report success for a page with no data source");
		result.Errors.Should().Contain(e => e.Contains("'PDS'"), because: "the data-source error must reach the tool response");
	}

	[Test]
	[Description("The apply oracle and the data-source check share one base read in the mobile validation pipeline.")]
	public async Task RunAsync_BothChecksNeedBase_ReadsItOnce() {
		// Arrange
		int baseReads = 0;
		Func<(string ViewModelConfigJson, string ModelConfigJson)> resolver = () => {
			baseReads++;
			return ("""{ "attributes": {} }""", "{}");
		};

		// Act
		PageSyncValidationResult result = await MobilePageValidation.RunAsync(
			MobileBodyWithoutDataSource,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			CreateValidator(),
			resolveTemplateBase: resolver);

		// Assert
		result.ContentOk.Should().BeFalse(because: "the base has no PDS, so the data-source check still rejects the body");
		baseReads.Should().Be(1, because: "both checks need the base and must share a single get-page read");
	}
}
