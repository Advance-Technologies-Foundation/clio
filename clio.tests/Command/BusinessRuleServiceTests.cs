using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class BusinessRuleServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IApplicationPackageListProvider _applicationPackageListProvider = null!;
	private IJsonConverter _jsonConverter = null!;
	private BusinessRuleService _service = null!;
	private string? _savedAddonRequestBody;

	[SetUp]
	public void SetUp() {
		_savedAddonRequestBody = null;
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_jsonConverter = Substitute.For<IJsonConverter>();
		_jsonConverter.CorrectJson(Arg.Any<string>()).Returns(call => call.Arg<string>());
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_applicationPackageListProvider.GetPackages().Returns(new[] {
			new PackageInfo(new PackageDescriptor {
				Name = "UsrPkg",
				UId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
			}, string.Empty, [])
		});
		_settingsRepository.FindEnvironment("dev").Returns(new EnvironmentSettings {
			Uri = "http://localhost",
			IsNetCore = true
		});
		_applicationClientFactory.CreateEnvironmentClient(Arg.Any<EnvironmentSettings>())
			.Returns(_applicationClient);
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => BuildResponse((string)callInfo[0], (string)callInfo[1]));
		_service = new BusinessRuleService(_settingsRepository, _applicationClientFactory, _applicationPackageListProvider, _jsonConverter);
	}

	[Test]
	[Category("Unit")]
	[Description("Appends a new entity business rule, preserves existing editable rules, and writes the generated caption resource back through the add-on designer service.")]
	public void Create_Should_Append_Rule_And_Save_Addon_Metadata() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Require owner for drafts",
				null,
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleOperand("attribute", "Status", null, null),
							"equal",
							new BusinessRuleOperand("constant", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\""), null))
					]),
				[
					new BusinessRuleAction("make-required", ["Owner", "Amount"]),
					new BusinessRuleAction("make-read-only", ["Status"])
				]));

		// Act
		BusinessRuleCreateResult result = _service.Create("dev", request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should return the generated internal rule name");

		_savedAddonRequestBody.Should().NotBeNullOrWhiteSpace(
			because: "the service should persist the updated add-on payload");
		using JsonDocument saveRequest = JsonDocument.Parse(_savedAddonRequestBody!);
		JsonElement schema = saveRequest.RootElement;
		schema.TryGetProperty("metaData", out JsonElement metaDataElement).Should().BeTrue(
			because: "the add-on save payload should carry serialized metadata");
		using JsonDocument metaData = JsonDocument.Parse(metaDataElement.GetString()!);
		JsonElement[] rules = metaData.RootElement.GetProperty("rules").EnumerateArray().ToArray();
		rules.Should().HaveCount(2,
			because: "existing rules must be preserved while the new rule is appended");
		rules[0].GetProperty("uId").GetString().Should().Be("existing-rule",
			because: "the existing rule should remain in place");
		rules[1].GetProperty("caption").GetString().Should().Be("Require owner for drafts",
			because: "the new rule should be serialized with the requested caption");
		rules[1].GetProperty("name").GetString().Should().StartWith("BusinessRule_",
			because: "the internal rule name should be generated automatically");
		rules[1].GetProperty("enabled").GetBoolean().Should().BeTrue(
			because: "enabled should default to true in the persisted metadata");
		rules[1].GetProperty("cases")[0].GetProperty("actions").GetArrayLength().Should().Be(2,
			because: "all requested actions should be preserved in the saved add-on");
		rules[1].GetProperty("cases")[0].GetProperty("actions")[0].GetRawText().Should().Contain("Owner")
			.And.Contain("Amount",
				because: "one action should be able to persist multiple target attributes without splitting into separate actions");
		rules[1].GetProperty("triggers")[0].GetProperty("name").GetString().Should().Be("Status",
			because: "the saved rule should include the derived trigger name");
		rules[1].GetProperty("triggers")[0].TryGetProperty("scopeId", out _).Should().BeFalse(
			because: "scopeId should be omitted when not provided so SaveSchema does not parse an empty GUID");

		JsonElement[] resources = schema.GetProperty("resources").EnumerateArray().ToArray();
		resources.Should().NotContain(resource =>
				resource.GetProperty("key").GetString()!.StartsWith("AddonConfig.Rules.", StringComparison.Ordinal),
			because: "GetSchema returns resources with 'AddonConfig.Rules.' prefix that must be stripped before SaveSchema, " +
				"otherwise ProcessSchemaDesignerUtilities.ApplyResources fails on Guid.Parse(path[0])");
		resources.Should().Contain(resource =>
				resource.GetProperty("key").GetString() == "existing-rule.Caption",
			because: "the existing resource key should be normalized from 'AddonConfig.Rules.existing-rule.Caption' to 'existing-rule.Caption'");
		resources.Should().Contain(resource =>
				resource.GetProperty("key").GetString() == $"{rules[1].GetProperty("uId").GetString()}.Caption" &&
				resource.GetProperty("value")[0].GetProperty("value").GetString() == "Require owner for drafts",
			because: "the caption resource must be saved under the generated rule resource key");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown referenced attributes before saving the add-on metadata.")]
	public void Create_Should_Reject_Unknown_Attributes() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Invalid rule",
				true,
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleOperand("attribute", "MissingStatus", null, null),
							"equal",
							new BusinessRuleOperand("constant", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\""), null))
					]),
				[
					new BusinessRuleAction("make-required", ["Owner"])
				]));

		// Act
		Action act = () => _service.Create("dev", request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*Unknown attribute 'MissingStatus'*",
				because: "the service should validate condition and action references against the target entity schema before save");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("AddonSchemaDesignerService.svc/SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes integer constant expressions with dataValueTypeName before value so AddonSchemaDesignerService can parse numeric constants without treating them as text.")]
	public void Create_Should_Serialize_Integer_Constant_After_DataValueTypeName() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Readonly amount when status is 5",
				true,
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleOperand("attribute", "Amount", null, null),
							"equal",
							new BusinessRuleOperand("constant", null, JsonSerializer.Deserialize<JsonElement>("5"), null))
					]),
				[
					new BusinessRuleAction("make-read-only", ["Amount"])
				]));

		// Act
		_service.Create("dev", request);

		// Assert
		_savedAddonRequestBody.Should().NotBeNullOrWhiteSpace(
			because: "the updated add-on payload should be captured for save");
		using JsonDocument saveRequest = JsonDocument.Parse(_savedAddonRequestBody!);
		using JsonDocument metaData = JsonDocument.Parse(saveRequest.RootElement.GetProperty("metaData").GetString()!);
		string rightExpressionJson = metaData.RootElement
			.GetProperty("rules")[1]
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetRawText();

		rightExpressionJson.IndexOf("\"dataValueTypeName\"", StringComparison.Ordinal).Should().BeGreaterThan(-1,
			because: "integer constant expressions should declare their business-rule data type");
		rightExpressionJson.IndexOf("\"value\"", StringComparison.Ordinal).Should().BeGreaterThan(-1,
			because: "integer constant expressions should persist the comparison value");
		rightExpressionJson.IndexOf("\"dataValueTypeName\"", StringComparison.Ordinal).Should()
			.BeLessThan(rightExpressionJson.IndexOf("\"value\"", StringComparison.Ordinal),
				because: "AddonSchemaDesignerService reads metadata sequentially and needs dataValueTypeName before value to avoid treating numeric tokens as text");
		metaData.RootElement
			.GetProperty("rules")[1]
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetProperty("value")
			.GetInt32()
			.Should()
			.Be(5, because: "the numeric constant should remain a JSON number after reordering");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps boolean entity columns from designer responses that use type so business-rule constants are serialized as Boolean instead of defaulting to Text.")]
	public void Create_Should_Map_Boolean_Column_Type_From_Type_Field() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Readonly amount when completed",
				true,
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleOperand("attribute", "Completed", null, null),
							"equal",
							new BusinessRuleOperand("constant", null, JsonSerializer.Deserialize<JsonElement>("true"), null))
					]),
				[
					new BusinessRuleAction("make-read-only", ["Amount"])
				]));

		// Act
		_service.Create("dev", request);

		// Assert
		_savedAddonRequestBody.Should().NotBeNullOrWhiteSpace(
			because: "the service should persist the updated add-on payload");
		using JsonDocument saveRequest = JsonDocument.Parse(_savedAddonRequestBody!);
		using JsonDocument metaData = JsonDocument.Parse(saveRequest.RootElement.GetProperty("metaData").GetString()!);
		JsonElement condition = metaData.RootElement
			.GetProperty("rules")[1]
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0];

		condition.GetProperty("leftExpression").GetProperty("dataValueTypeName").GetString().Should().Be("Boolean",
			because: "designer responses send boolean column metadata under type and the business-rule mapper should preserve that runtime type");
		condition.GetProperty("rightExpression").GetProperty("dataValueTypeName").GetString().Should().Be("Boolean",
			because: "constant values should inherit the actual column runtime type rather than defaulting to Text");
		condition.GetProperty("rightExpression").GetProperty("value").GetBoolean().Should().BeTrue(
			because: "boolean constants should remain booleans in the persisted add-on metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts lookup constant payloads when the right-hand constant is a plain GUID string and persists it as-is.")]
	public void Create_Should_Persist_Lookup_Constant_Guid_String() {
		// Arrange
		const string ownerId = "e0be1264-f36b-1410-fa98-00155d043204";
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Require status for owner",
				true,
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleOperand("attribute", "Owner", null, null),
							"equal",
							new BusinessRuleOperand(
								"constant",
								null,
								JsonSerializer.Deserialize<JsonElement>($"\"{ownerId}\""),
								null))
					]),
				[
					new BusinessRuleAction("make-required", ["Status"])
				]));

		// Act
		BusinessRuleCreateResult result = _service.Create("dev", request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should create a rule when lookup constants are passed as plain GUID strings");
		_savedAddonRequestBody.Should().NotBeNullOrWhiteSpace(
			because: "a valid lookup constant should be persisted into the add-on payload");
		using JsonDocument saveRequest = JsonDocument.Parse(_savedAddonRequestBody!);
		using JsonDocument metaData = JsonDocument.Parse(saveRequest.RootElement.GetProperty("metaData").GetString()!);
		JsonElement rightExpression = metaData.RootElement
			.GetProperty("rules")[1]
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression");

		rightExpression.GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "lookup constants should keep the lookup runtime type in persisted metadata");
		rightExpression.GetProperty("referenceSchemaName").GetString().Should().Be("Contact",
			because: "lookup constants should preserve the reference schema name for the designer metadata");
		rightExpression.GetProperty("value").GetString().Should().Be(ownerId,
			because: "the saved metadata should contain the same raw GUID string that was provided in the request");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects lookup constant payloads when the right-hand constant is not a plain GUID string.")]
	public void Create_Should_Reject_Lookup_Constant_Object_Payload() {
		// Arrange
		const string ownerId = "e0be1264-f36b-1410-fa98-00155d043204";
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Require status for owner",
				true,
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleOperand("attribute", "Owner", null, null),
							"equal",
							new BusinessRuleOperand(
								"constant",
								null,
								JsonSerializer.SerializeToElement(new {
									Value = ownerId,
									DisplayValue = "John Best"
								}),
								null))
					]),
				[
					new BusinessRuleAction("make-required", ["Status"])
				]));

		// Act
		Action act = () => _service.Create("dev", request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*rightExpression.value must be a GUID string*",
				because: "lookup operands should only accept a plain GUID string on the right-hand side");
		_savedAddonRequestBody.Should().BeNull(
			because: "validation should reject the request before the add-on save call is attempted");
	}

	private string BuildResponse(string url, string requestBody) {
		if (url.Contains("SelectQuery", StringComparison.Ordinal)) {
			if (requestBody.Contains("\"SysPackage\"", StringComparison.Ordinal)) {
				return """
				{"success":true,"rows":[{"UId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}]}
				""";
			}

			throw new InvalidOperationException($"Unexpected select query payload: {requestBody}");
		}

		if (url.Contains("EntitySchemaDesignerService.svc/GetSchemaDesignItem", StringComparison.Ordinal)) {
			return """
			{
			  "success": true,
			  "schema": {
			    "uId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
			    "name": "UsrOrder",
			    "package": { "uId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "name": "UsrPkg" },
			    "columns": [
			      { "uId": "10000000-0000-0000-0000-000000000001", "name": "Status", "type": 1 },
			      { "uId": "10000000-0000-0000-0000-000000000004", "name": "Completed", "type": 12 },
			      { "uId": "10000000-0000-0000-0000-000000000002", "name": "Owner", "type": 10, "referenceSchema": { "name": "Contact", "primaryDisplayColumn": { "name": "Name" } } },
			      { "uId": "10000000-0000-0000-0000-000000000003", "name": "Amount", "type": 6 }
			    ],
			    "inheritedColumns": [],
			    "indexes": [],
			    "parentSchema": { "uId": "cccccccc-cccc-cccc-cccc-cccccccccccc", "name": "BaseEntity" }
			  }
			}
			""";
		}

		if (url.Contains("AddonSchemaDesignerService.svc/GetSchema", StringComparison.Ordinal)) {
			return """
			{
			  "success": true,
			  "schema": {
			    "metaData": "{\"typeName\":\"Terrasoft.Core.BusinessRules.BusinessRules\",\"rules\":[{\"typeName\":\"Terrasoft.Core.BusinessRules.BusinessRule\",\"uId\":\"existing-rule\",\"name\":\"BusinessRule_old\",\"enabled\":true,\"caption\":\"Existing rule\",\"cases\":[{\"typeName\":\"Terrasoft.Core.BusinessRules.Models.BusinessRuleCase\",\"uId\":\"existing-case\",\"actions\":[]}],\"triggers\":[]}]}",
			    "resources": [
			      {
			        "key": "AddonConfig.Rules.existing-rule.Caption",
			        "value": [
			          { "key": "en-US", "value": "Existing rule" }
			        ]
			      }
			    ]
			  }
			}
			""";
		}

		if (url.Contains("AddonSchemaDesignerService.svc/SaveSchema", StringComparison.Ordinal)) {
			_savedAddonRequestBody = requestBody;
			return """
			{"success":true,"value":true}
			""";
		}

		throw new InvalidOperationException($"Unexpected request url: {url}");
	}
}
