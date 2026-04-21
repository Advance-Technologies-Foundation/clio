using System;
using System.Text.Json;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleServiceTests {
	private IAddonSchemaDesignerClient _addonSchemaDesignerClient = null!;
	private IApplicationPackageListProvider _applicationPackageListProvider = null!;
	private IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient = null!;
	private BusinessRuleService _service = null!;
	private AddonSchemaDto? _savedAddonSchema;

	[SetUp]
	public void SetUp() {
		_savedAddonSchema = null;
		_addonSchemaDesignerClient = Substitute.For<IAddonSchemaDesignerClient>();
		_entitySchemaDesignerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_applicationPackageListProvider.GetPackages().Returns(new[] {
			new PackageInfo(new PackageDescriptor {
				Name = "UsrPkg",
				UId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
			}, string.Empty, [])
		});
		_entitySchemaDesignerClient.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true,
				Schema = BuildEntitySchema()
			});
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(BuildAddonSchema());
		_addonSchemaDesignerClient
			.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(callInfo => _savedAddonSchema = callInfo.Arg<AddonSchemaDto>());
		_service = new BusinessRuleService(
			_addonSchemaDesignerClient,
			_entitySchemaDesignerClient,
			_applicationPackageListProvider);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects create requests with no package name before remote schema lookups start.")]
	public void Create_Should_Reject_Request_When_Package_Name_Is_Missing() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"",
			"UsrOrder",
			new BusinessRule(
				"Require owner for drafts",
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "Status", null),
							"equal",
							new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\"")))
					]),
				[
					new BusinessRuleAction("make-required", ["Owner"])
				]));

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("package-name is required.",
				because: "request-level preconditions belong to the create operation even though rule validation is handled separately");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
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
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "Status", null),
							"equal",
							new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\"")))
					]),
				[
					new BusinessRuleAction("make-required", ["Owner", "Amount"]),
					new BusinessRuleAction("make-read-only", ["Status"])
				]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should return the generated internal rule name");
		_addonSchemaDesignerClient.Received(1).GetSchema(
			Arg.Is<AddonGetRequestDto>(dto =>
				dto.AddonName == "BusinessRule"
				&& dto.TargetSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& dto.TargetParentSchemaUId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
				&& dto.TargetPackageUId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
				&& dto.TargetSchemaManagerName == "EntitySchemaManager"
				&& dto.UseFullHierarchy));

		_savedAddonSchema.Should().NotBeNull(
			because: "the service should persist the updated add-on payload");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
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
		JsonElement[] triggers = [.. rules[1].GetProperty("triggers").EnumerateArray()];
		triggers.Should().HaveCount(2,
			because: "there should be a ChangeAttributeValue trigger for Status and a global DataLoaded trigger");
		triggers[0].GetProperty("name").GetString().Should().Be("Status",
			because: "the saved rule should include the derived trigger name");
		triggers[0].GetProperty("type").GetInt32().Should().Be(0,
			because: "the condition-derived trigger should be ChangeAttributeValue (type 0)");
		triggers[0].TryGetProperty("scopeId", out _).Should().BeFalse(
			because: "scopeId should be omitted when not provided so SaveSchema does not parse an empty GUID");
		triggers[1].GetProperty("name").GetString().Should().BeEmpty(
			because: "the DataLoaded trigger should have an empty name for global scope");
		triggers[1].GetProperty("type").GetInt32().Should().Be(2,
			because: "the DataLoaded trigger type should be 2");

		_savedAddonSchema.Resources.Should().NotContain(resource =>
				resource.Key.StartsWith("AddonConfig.Rules.", StringComparison.Ordinal),
			because: "GetSchema returns resources with 'AddonConfig.Rules.' prefix that must be stripped before SaveSchema, " +
				"otherwise ProcessSchemaDesignerUtilities.ApplyResources fails on Guid.Parse(path[0])");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == "existing-rule.Caption",
			because: "the existing resource key should be normalized from 'AddonConfig.Rules.existing-rule.Caption' to 'existing-rule.Caption'");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == $"{rules[1].GetProperty("uId").GetString()}.Caption" &&
				resource.Value[0].Value == "Require owner for drafts",
			because: "the caption resource must be saved under the generated rule resource key");
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
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
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "MissingStatus", null),
							"equal",
							new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\"")))
					]),
				[
					new BusinessRuleAction("make-required", ["Owner"])
				]));

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*Unknown attribute 'MissingStatus'*",
				because: "the service should validate condition and action references against the target entity schema before save");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
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
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "Amount", null),
							"equal",
							new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("5")))
					]),
				[
					new BusinessRuleAction("make-read-only", ["Amount"])
				]));

		// Act
		_service.Create(request);

		// Assert
		_savedAddonSchema.Should().NotBeNull(
			because: "the updated add-on payload should be captured for save");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
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
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "Completed", null),
							"equal",
							new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("true")))
					]),
				[
					new BusinessRuleAction("make-read-only", ["Amount"])
				]));

		// Act
		_service.Create(request);

		// Assert
		_savedAddonSchema.Should().NotBeNull(
			because: "the service should persist the updated add-on payload");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
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
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "Owner", null),
							"equal",
							new BusinessRuleExpression(
								"Const",
								null,
								JsonSerializer.Deserialize<JsonElement>($"\"{ownerId}\"")))
					]),
				[
					new BusinessRuleAction("make-required", ["Status"])
				]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should create a rule when lookup constants are passed as plain GUID strings");
		_savedAddonSchema.Should().NotBeNull(
			because: "a valid lookup constant should be persisted into the add-on payload");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
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

	private static EntityDesignSchemaDto BuildEntitySchema() {
		return new EntityDesignSchemaDto {
			UId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
			Name = "UsrOrder",
			Columns = [
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
					Name = "Status",
					DataValueType = 1
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
					Name = "Completed",
					DataValueType = 12
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
					Name = "Owner",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "Contact"
					}
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
					Name = "Amount",
					DataValueType = 6
				}
			],
			InheritedColumns = [],
			Indexes = [],
			ParentSchema = new EntityDesignSchemaDto {
				UId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
				Name = "BaseEntity"
			}
		};
	}

	private static AddonSchemaDto BuildAddonSchema() {
		return new AddonSchemaDto {
			MetaData = "{\"typeName\":\"Terrasoft.Core.BusinessRules.BusinessRules\",\"rules\":[{\"typeName\":\"Terrasoft.Core.BusinessRules.BusinessRule\",\"uId\":\"existing-rule\",\"name\":\"BusinessRule_old\",\"enabled\":true,\"caption\":\"Existing rule\",\"cases\":[{\"typeName\":\"Terrasoft.Core.BusinessRules.Models.BusinessRuleCase\",\"uId\":\"existing-case\",\"actions\":[]}],\"triggers\":[]}]}",
			Resources = [
				new AddonResourceDto {
					Key = "AddonConfig.Rules.existing-rule.Caption",
					Value = [
						new AddonResourceValueDto {
							Key = "en-US",
							Value = "Existing rule"
						}
					]
				}
			]
		};
	}
}
