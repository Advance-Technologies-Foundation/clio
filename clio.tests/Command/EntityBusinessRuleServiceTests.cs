using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class EntityBusinessRuleServiceTests {
	private IAddonSchemaDesignerClient _addonSchemaDesignerClient = null!;
	private IApplicationPackageListProvider _applicationPackageListProvider = null!;
	private IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient = null!;
	private IBusinessRuleFormulaValidationService _formulaValidationService = null!;
	private IBusinessRuleLookupReferenceValidator _lookupReferenceValidator = null!;
	private ISysSettingConditionOperandResolver _sysSettingResolver = null!;
	private EntityBusinessRuleService _service = null!;
	private AddonSchemaDto? _savedAddonSchema;

	[SetUp]
	public void SetUp() {
		_savedAddonSchema = null;
		_addonSchemaDesignerClient = Substitute.For<IAddonSchemaDesignerClient>();
		_entitySchemaDesignerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_formulaValidationService = Substitute.For<IBusinessRuleFormulaValidationService>();
		_lookupReferenceValidator = Substitute.For<IBusinessRuleLookupReferenceValidator>();
		_sysSettingResolver = Substitute.For<ISysSettingConditionOperandResolver>();
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
		EntityBusinessRuleSchemaProvider schemaProvider = new(_entitySchemaDesignerClient);
		_service = new EntityBusinessRuleService(
			new BusinessRulePackageResolver(_applicationPackageListProvider),
			new EntityBusinessRuleAttributeProvider(schemaProvider),
			new BusinessRuleAddonService(_addonSchemaDesignerClient),
			_formulaValidationService,
			new BusinessRuleValidator(_lookupReferenceValidator),
			new StaticFilterContextFactory(
				schemaProvider,
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>()),
			_sysSettingResolver);
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves system-setting operand types via ISysSettingConditionOperandResolver and threads the resolved map through conversion so the persisted metadata carries a typed SysSetting expression.")]
	public void Create_Should_Resolve_And_Persist_SysSetting_Operand() {
		// Arrange
		_sysSettingResolver.Resolve(Arg.Any<BusinessRule>()).Returns(
			new Dictionary<string, SysSettingOperandDescriptor>(StringComparer.Ordinal) {
				["DisableEquipmentDelivery"] = new("DisableEquipmentDelivery", "Boolean", null)
			});
		BusinessRule rule = new(
			"Lock status when equipment delivery is disabled",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysSetting", sysSettingName: "DisableEquipmentDelivery"),
						"equal",
						new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("true")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleCreateResult result = _service.Create(new BusinessRuleCreateRequest("UsrPkg", "UsrOrder", rule));

		// Assert
		_sysSettingResolver.Received(1).Resolve(rule);
		result.RuleName.Should().NotBeNullOrEmpty(
			because: "resolving the setting type and threading the map must let the rule persist");
		_savedAddonSchema.Should().NotBeNull(
			because: "the SysSetting rule must be saved to the entity add-on");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		// The add-on append preserves the pre-seeded existing rule at index 0, so select the appended rule by caption.
		JsonElement savedRule = metaData.RootElement.GetProperty("rules").EnumerateArray()
			.Single(r => r.GetProperty("caption").GetString() == "Lock status when equipment delivery is disabled");
		JsonElement left = savedRule
			.GetProperty("cases")[0].GetProperty("condition").GetProperty("conditions")[0].GetProperty("leftExpression");
		left.GetProperty("type").GetString().Should().Be("SysSetting",
			because: "the resolved operand must persist as a SysSetting expression");
		left.GetProperty("sysSettingName").GetString().Should().Be("DisableEquipmentDelivery",
			because: "the setting code must round-trip into the persisted metadata");
		left.GetProperty("dataValueTypeName").GetString().Should().Be("Boolean",
			because: "the resolver-provided data value type must reach the persisted operand");
	}

	[TestCase("", "UsrOrder", true, "package-name is required.")]
	[TestCase("UsrPkg", "", true, "entity-schema-name is required.")]
	[TestCase("UsrPkg", "UsrOrder", false, "rule is required.")]
	[Category("Unit")]
	[Description("Rejects missing request-level fields before package resolution or remote schema calls start.")]
	public void Create_Should_Reject_Request_Level_Guards(
		string packageName,
		string entitySchemaName,
		bool includeRule,
		string expectedMessage) {
		// Arrange
		BusinessRuleCreateRequest request = new(
			packageName,
			entitySchemaName,
			includeRule ? CreateRule() : null!);

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage(expectedMessage,
				because: "the service should fail fast on missing required request fields before remote dependencies are invoked");
		_entitySchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchemaDesignItem(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Fails before schema designer calls when the requested package cannot be found in the installed package list.")]
	public void Create_Should_Reject_When_Package_Cannot_Be_Resolved() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"MissingPkg",
			"UsrOrder",
			CreateRule());

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Package 'MissingPkg' was not found.",
				because: "package resolution is owned by the service before any remote schema work begins");
		_entitySchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchemaDesignItem(default!, default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Loads the target entity schema, appends the new rule metadata, normalizes resources, and saves the updated add-on payload.")]
	public void Create_Should_Load_Target_Entity_And_Append_Rule_Before_Saving_Addon_Metadata() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule(actions: [
				new MakeRequiredBusinessRuleAction(["Owner", "Amount"]),
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should return the generated metadata rule name after a successful save");
		_entitySchemaDesignerClient.Received(1).GetSchemaDesignItem(
			Arg.Is<GetSchemaDesignItemRequestDto>(dto =>
				dto.Name == "UsrOrder"
				&& dto.PackageUId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
				&& dto.UseFullHierarchy
				&& dto.Cultures.Count == 1
				&& dto.Cultures[0] == EntitySchemaDesignerSupport.DefaultCultureName),
			Arg.Any<RemoteCommandOptions>());
		_addonSchemaDesignerClient.Received(1).GetSchema(
			Arg.Is<AddonGetRequestDto>(dto =>
				dto.AddonName == "BusinessRule"
				&& dto.TargetSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& dto.TargetParentSchemaUId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
				&& dto.TargetPackageUId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
				&& dto.TargetSchemaManagerName == "EntitySchemaManager"
				&& dto.UseFullHierarchy));
		_savedAddonSchema.Should().NotBeNull(
			because: "the service should persist the updated add-on payload after appending the rule");

		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];

		rules.Should().HaveCount(2,
			because: "the new rule should be appended while preserving existing metadata");
		rules[0].GetProperty("uId").GetString().Should().Be("existing-rule",
			because: "the existing rule should remain in the saved add-on metadata");
		rules[1].GetProperty("caption").GetString().Should().Be("Require owner for drafts",
			because: "the appended rule should keep the requested caption");
		rules[1].GetProperty("name").GetString().Should().StartWith("BusinessRule_",
			because: "the appended rule should get a generated internal name");
		_savedAddonSchema.Resources.Should().NotContain(resource =>
				resource.Key.StartsWith("AddonConfig.Rules.", StringComparison.Ordinal),
			because: "resource keys returned by GetSchema must be normalized before SaveSchema");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == "existing-rule.Caption",
			because: "existing resource keys should be normalized instead of being rewritten with the server prefix");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == "LocalizableStrings.Module.Title",
			because: "non business-rule resource keys should not be normalized as rule captions");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == $"{rules[1].GetProperty("uId").GetString()}.Caption"
				&& resource.Value[0].Value == "Require owner for drafts",
			because: "the service should upsert the caption resource for the generated rule id");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
		_addonSchemaDesignerClient.Received(1).BuildConfiguration();
	}

	[Test]
	[Category("Unit")]
	[Description("Validates lookup constants before formula validation and add-on metadata mutation.")]
	public void Create_Should_Validate_Lookup_Constants_Before_Saving_Addon_Metadata() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule(actions: [
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						new(
							new BusinessRuleExpression("AttributeValue", "Owner", null),
							new BusinessRuleExpression("Const", null,
								JsonSerializer.Deserialize<JsonElement>("\"11111111-1111-1111-1111-111111111111\"")))
					})
			]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "successful lookup validation should allow rule creation to continue");
		_lookupReferenceValidator.Received(1).Validate(
			request.Rule,
			Arg.Any<IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>>());
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Category("Unit")]
	[Description("Stops before formula validation or add-on metadata mutation when a lookup constant points to a missing record.")]
	public void Create_Should_Not_Save_Addon_Metadata_When_Lookup_Validation_Fails() {
		// Arrange
		_lookupReferenceValidator
			.When(validator => validator.Validate(
				Arg.Any<BusinessRule>(),
				Arg.Any<IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>>()))
			.Do(_ => throw new ArgumentException("Lookup record was not found."));
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule());

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Lookup record was not found.",
				because: "invalid lookup references must stop before destructive add-on writes");
		_formulaValidationService.DidNotReceiveWithAnyArgs().Validate(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Creates apply-filter business rules as a parent lookup filter plus autogenerated child clear and populate rules.")]
	public void Create_Should_Persist_ApplyFilter_As_Parent_And_Autogenerated_Child_Rules() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Filter city by country",
				new BusinessRuleConditionGroup("AND", []),
				[
					new ApplyFilterBusinessRuleAction(
						"City",
						"Country",
						"Country",
						null,
						clearValue: true,
						populateValue: true)
				]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should still return the generated parent rule name for apply-filter creation");
		_savedAddonSchema.Should().NotBeNull(
			because: "apply-filter creation should persist updated add-on metadata");

		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];

		rules.Should().HaveCount(4,
			because: "the add-on should keep the existing rule and append the parent plus clear and populate apply-filter rules");
		JsonElement parentRule = rules[1];
		JsonElement clearRule = rules[2];
		JsonElement populateRule = rules[3];

		parentRule.GetProperty("caption").GetString().Should().Be("Filter city by country",
			because: "the parent apply-filter rule should preserve the requested caption");
		JsonElement parentAction = parentRule.GetProperty("cases")[0].GetProperty("actions")[0];
		parentAction.GetProperty("typeName").GetString().Should().Be(BusinessRuleConstants.BusinessRuleFilterLookupElementTypeName,
			because: "the parent rule should persist the filter lookup action type");
		JsonElement parentLeftExpression = parentAction.GetProperty("leftExpression");
		parentLeftExpression.GetProperty("path").GetString().Should().Be("City",
			because: "the saved parent left filter expression should keep the target lookup root path");
		parentLeftExpression.GetProperty("filterExpression").GetString().Should().Be("Country",
			because: "the saved parent left filter expression should keep the target-side filter path");
		parentLeftExpression.GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "the saved parent left filter expression should preserve the lookup data value type");
		parentLeftExpression.GetProperty("referenceSchemaName").GetString().Should().Be("City",
			because: "the saved parent left filter expression should preserve the target lookup schema");
		JsonElement parentRightExpression = parentAction.GetProperty("rightExpression");
		parentRightExpression.GetProperty("path").GetString().Should().Be("Country",
			because: "the saved parent right filter expression should keep the source lookup root path");
		parentRightExpression.GetProperty("filterExpression").GetString().Should().Be("null",
			because: "the saved parent right filter expression should keep the null sentinel when no source filter path is provided");
		parentRightExpression.GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "the saved parent right filter expression should preserve the lookup data value type");
		parentRightExpression.GetProperty("referenceSchemaName").GetString().Should().Be("Country",
			because: "the saved parent right filter expression should preserve the source lookup schema");
		parentRule.GetProperty("triggers").EnumerateArray()
			.Select(trigger => trigger.GetProperty("name").GetString())
			.Should().Contain(["", "Country"],
				because: "the parent apply-filter rule should run on DataLoaded and source lookup changes");

		clearRule.GetProperty("parentUId").GetString().Should().Be(parentRule.GetProperty("uId").GetString(),
			because: "clear child rules should point back to the parent apply-filter rule");
		clearRule.GetProperty("name").GetString().Should().StartWith("Autogenerated_",
			because: "clear child rules should use autogenerated internal names");
		clearRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("comparisonType").GetInt32()
			.Should().Be(BusinessRuleConstants.ComparisonNotEqual,
				because: "clear child rules should compare source and target filter values for mismatch");
		clearRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("leftExpression")
			.EnumerateObject().Select(property => property.Name)
			.Should().BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "UI-generated clear child conditions omit lookup descriptor metadata on attribute expressions");
		clearRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("rightExpression")
			.EnumerateObject().Select(property => property.Name)
			.Should().BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "UI-generated clear child conditions keep only the minimal attribute expression payload");
		JsonElement clearSetValuesItems = clearRule.GetProperty("cases")[0].GetProperty("actions")[0].GetProperty("items");
		clearSetValuesItems.GetArrayLength().Should().Be(1,
			because: "the saved clear child rule should persist one set-values item");
		JsonElement clearSetValueItem = clearSetValuesItems[0];
		clearSetValueItem.GetProperty("expression").GetProperty("path").GetString().Should().Be("City",
			because: "the saved clear child rule should reset the target lookup root");
		clearSetValueItem.GetProperty("expression").GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "the saved clear child target expression should preserve the lookup data value type");
		clearSetValueItem.GetProperty("expression").GetProperty("referenceSchemaName").GetString().Should().Be("City",
			because: "the saved clear child target expression should preserve the target lookup schema");
		clearSetValueItem.GetProperty("value").GetProperty("typeName").GetString()
			.Should().Be(BusinessRuleConstants.BusinessRuleEmptyValueExpressionTypeName,
				because: "the saved clear child rule should assign the empty lookup expression");
		clearSetValueItem.GetProperty("value").GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "the saved clear child empty expression should preserve the lookup data value type");
		clearSetValueItem.GetProperty("value").GetProperty("value").GetString().Should().Be(Guid.Empty.ToString(),
			because: "the saved clear child rule should assign the zero GUID empty lookup value");

		populateRule.GetProperty("parentUId").GetString().Should().Be(parentRule.GetProperty("uId").GetString(),
			because: "populate child rules should point back to the parent apply-filter rule");
		populateRule.GetProperty("name").GetString().Should().Contain("PopulateValue",
			because: "populate child rules should keep deterministic autogenerated suffixes");
		populateRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("comparisonType").GetInt32()
			.Should().Be(BusinessRuleConstants.ComparisonIsFilledIn,
				because: "populate child rules should run only when the target lookup is selected");
		populateRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("leftExpression")
			.EnumerateObject().Select(property => property.Name)
			.Should().BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "UI-generated populate child conditions also omit lookup descriptor metadata");
		JsonElement populateSetValuesItems = populateRule.GetProperty("cases")[0].GetProperty("actions")[0].GetProperty("items");
		populateSetValuesItems.GetArrayLength().Should().Be(1,
			because: "the saved populate child rule should persist one set-values item");
		JsonElement populateSetValueItem = populateSetValuesItems[0];
		populateSetValueItem.GetProperty("expression").GetProperty("path").GetString().Should().Be("Country",
			because: "the saved populate child rule should assign the source lookup root");
		populateSetValueItem.GetProperty("expression").GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "the saved populate child target expression should preserve the lookup data value type");
		populateSetValueItem.GetProperty("expression").GetProperty("referenceSchemaName").GetString().Should().Be("Country",
			because: "the saved populate child target expression should preserve the source lookup schema");
		populateSetValueItem.GetProperty("value").GetProperty("path").GetString().Should().Be("City.Country",
			because: "the saved populate child rule should copy the target-related lookup path");
		populateSetValueItem.GetProperty("value").GetProperty("dataValueTypeName").GetString().Should().Be("Lookup",
			because: "the saved populate child copied value should preserve the lookup data value type");
		populateSetValueItem.GetProperty("value").GetProperty("referenceSchemaName").GetString().Should().Be("Country",
			because: "the saved populate child copied value should preserve the final lookup schema");

		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == $"{parentRule.GetProperty("uId").GetString()}.Caption"
				&& resource.Value[0].Value == "Filter city by country",
			because: "only the parent apply-filter rule should receive a caption resource");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == $"{clearRule.GetProperty("uId").GetString()}.Caption"
				&& resource.Value[0].Value == $"ChildRule-{parentRule.GetProperty("uId").GetString()}-ClearValue",
			because: "autogenerated clear child rules need a non-empty caption resource to satisfy platform validation");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == $"{populateRule.GetProperty("uId").GetString()}.Caption"
				&& resource.Value[0].Value == $"ChildRule-{parentRule.GetProperty("uId").GetString()}-PopulateValue",
			because: "autogenerated populate child rules need a non-empty caption resource to satisfy platform validation");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-filter requests that omit the condition list before any remote schema or add-on calls run.")]
	public void Create_Should_Reject_ApplyFilter_With_Null_Condition_List() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Filter city by country",
				new BusinessRuleConditionGroup("AND", null!),
				[
					new ApplyFilterBusinessRuleAction(
						"City",
						"Country",
						"Country",
						null,
						clearValue: true,
						populateValue: false)
				]));

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions is required.",
				because: "apply-filter may omit condition items, but the service should still reject payloads that omit the conditions collection itself");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves the user-authored parent condition when saving the Supervisor-created ninja apply-filter regression scenario.")]
	public void Create_Should_Persist_ApplyFilter_Parent_Condition_For_Supervisor_Created_Ninja() {
		// Arrange
		string supervisorId = "4055a3b6-867e-3756-311a-576cbaba3230";
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			new BusinessRule(
				"Filter clan by Supervisor-created ninja",
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "CreatedBy"),
							"equal",
							new BusinessRuleExpression("Const", value: JsonSerializer.SerializeToElement(supervisorId)))
					]),
				[
					new ApplyFilterBusinessRuleAction(
						"Clan",
						"CreatedBy",
						"CreatedBy",
						null,
						clearValue: true,
						populateValue: false)
				]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should still create a parent apply-filter rule for the reported regression scenario");
		_savedAddonSchema.Should().NotBeNull(
			because: "the service should persist add-on metadata for the regression scenario");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
		rules.Should().HaveCount(3,
			because: "clear-only apply-filter should append the parent and one autogenerated clear child rule");
		JsonElement parentRule = rules[1];
		JsonElement parentCondition = parentRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("conditions")[0];
		parentCondition.GetProperty("leftExpression").GetProperty("path").GetString().Should().Be("CreatedBy",
			because: "the saved parent apply-filter rule should preserve the original CreatedBy condition");
		parentCondition.GetProperty("rightExpression").GetProperty("value").GetString().Should().Be(supervisorId,
			because: "the saved parent apply-filter rule should preserve the Supervisor GUID constant");
		parentRule.GetProperty("triggers").EnumerateArray()
			.Select(trigger => trigger.GetProperty("name").GetString())
			.Should().Contain("CreatedBy",
				because: "the saved parent rule should stay reactive to the condition attribute as well as the apply-filter source");
		JsonElement parentAction = parentRule.GetProperty("cases")[0].GetProperty("actions")[0];
		parentAction.GetProperty("leftExpression").GetProperty("path").GetString().Should().Be("Clan",
			because: "the parent filter-lookup target should remain unchanged while preserving the parent condition");
		parentAction.GetProperty("rightExpression").GetProperty("path").GetString().Should().Be("CreatedBy",
			because: "the parent filter-lookup source should remain unchanged while preserving the parent condition");
		JsonElement clearRule = rules[2];
		clearRule.GetProperty("cases")[0].GetProperty("condition").GetProperty("leftExpression")
			.EnumerateObject().Select(property => property.Name)
			.Should().BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "the autogenerated clear child rule should still keep its UI-shaped minimal condition payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Calls BuildConfiguration after a successful save so that the ConfigurationHash is updated and offline users get cache invalidation on their next startup.")]
	public void Create_Should_Call_BuildConfiguration_After_Successful_Save() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule());

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should return the generated rule name after a successful save");
		_addonSchemaDesignerClient.Received(1).BuildConfiguration();
	}

	[Test]
	[Category("Unit")]
	[Description("Calls ResetClientScriptCache after a successful save so the saved addon schema is visible to the current user immediately.")]
	public void Create_Should_Call_ResetClientScriptCache_After_Successful_Save() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule());

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "the service should return the generated rule name after a successful save");
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
	}

	[Test]
	[Category("Unit")]
	[Description("Validates formula set-values expressions through the expression service contract before loading or saving add-on metadata.")]
	public void Create_Should_Validate_SetValues_Formulas_Before_Saving_Addon_Metadata() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule(actions: [
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						new(
							new BusinessRuleExpression("AttributeValue", "TotalScore", null),
							new BusinessRuleExpression("Formula", expression: "BaseScore + BonusScore"))
					})
			]));

		// Act
		BusinessRuleCreateResult result = _service.Create(request);

		// Assert
		result.RuleName.Should().StartWith("BusinessRule_",
			because: "formula validation should allow the service to continue when the remote validator accepts the expression");
		_formulaValidationService.Received(1).Validate(Arg.Is<BusinessRuleFormulaValidationContext>(context =>
			context.TargetPath == "TotalScore"
			&& context.Formula == "BaseScore + BonusScore"
			&& context.Metadata.Expression == "#UsrOrderRecord.BaseScore# + #UsrOrderRecord.BonusScore#"
			&& context.Metadata.ResultDataValueType == "Integer"));
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Category("Unit")]
	[Description("Stops before loading add-on metadata when remote formula validation returns an error.")]
	public void Create_Should_Not_Save_Addon_Metadata_When_Formula_Validation_Fails() {
		// Arrange
		_formulaValidationService
			.When(service => service.Validate(Arg.Any<BusinessRuleFormulaValidationContext>()))
			.Do(_ => throw new ArgumentException("Formula validation failed."));
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule(actions: [
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						new(
							new BusinessRuleExpression("AttributeValue", "TotalScore", null),
							new BusinessRuleExpression("Formula", expression: "BaseScore + BonusScore"))
					})
			]));

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula validation failed.",
				because: "invalid formula metadata should stop before add-on metadata is changed");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Stops before saving add-on metadata when business-rule validation fails against the loaded entity schema.")]
	public void Create_Should_Not_Save_Addon_Metadata_When_Rule_Validation_Fails() {
		// Arrange
		BusinessRuleCreateRequest request = new(
			"UsrPkg",
			"UsrOrder",
			CreateRule(leftPath: "MissingStatus"));

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*Unknown attribute 'MissingStatus'*",
				because: "invalid rule references should fail validation before any add-on save happens");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().ResetClientScriptCache();
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().BuildConfiguration();
	}

	[Test]
	[Category("Unit")]
	[Description("Saves an entire batch of rules with a single add-on round-trip: one GetSchema, one SaveSchema, one ResetClientScriptCache, and one BuildConfiguration regardless of rule count.")]
	public void Create_Batch_Should_Save_All_Rules_With_A_Single_Addon_RoundTrip() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Rule 1"),
				CreateRule(caption: "Rule 2", actions: [new MakeReadOnlyBusinessRuleAction(["Status"])]),
				CreateRule(caption: "Rule 3", actions: [new MakeRequiredBusinessRuleAction(["Amount"])])
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Create(request);

		// Assert
		results.Should().OnlyContain(result => result.Success, because: "all three rules are valid");
		results.Select(result => result.Name).Should().Equal(["Rule 1", "Rule 2", "Rule 3"],
			because: "per-rule outcomes are returned in input order keyed by caption");
		results.Should().OnlyContain(result => result.RuleName!.StartsWith("BusinessRule_"),
			because: "each created rule reports its generated internal rule name");
		_addonSchemaDesignerClient.Received(1).GetSchema(Arg.Any<AddonGetRequestDto>());
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
		_addonSchemaDesignerClient.Received(1).BuildConfiguration();

		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
		rules.Should().HaveCount(4, because: "the existing rule plus all three appended rules are saved in one payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Isolates a per-rule validation failure: the bad rule is excluded and reported while the remaining rules are still saved in the single batch write.")]
	public void Create_Batch_Should_Isolate_Per_Rule_Validation_Failure() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Good rule 1"),
				CreateRule(caption: "Bad rule", leftPath: "MissingStatus"),
				CreateRule(caption: "Good rule 2", actions: [new MakeReadOnlyBusinessRuleAction(["Status"])])
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Create(request);

		// Assert
		results.Should().HaveCount(3, because: "every input rule gets an outcome entry");
		results[0].Success.Should().BeTrue(because: "the first rule is valid");
		results[1].Success.Should().BeFalse(because: "the rule references an unknown attribute");
		results[1].Error.Should().Contain("MissingStatus");
		results[2].Success.Should().BeTrue(because: "a failed rule must not abort the remaining rules");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());

		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
		rules.Should().HaveCount(3, because: "only the two valid rules are appended to the existing rule");
	}

	[Test]
	[Category("Unit")]
	[Description("Performs no add-on write when every rule in the batch fails validation.")]
	public void Create_Batch_Should_Not_Save_When_All_Rules_Fail_Validation() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Bad 1", leftPath: "MissingA"),
				CreateRule(caption: "Bad 2", leftPath: "MissingB")
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Create(request);

		// Assert
		results.Should().OnlyContain(result => !result.Success, because: "every rule failed validation");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().BuildConfiguration();
	}

	[TestCase("", "UsrOrder", "package-name is required.")]
	[TestCase("UsrPkg", "", "entity-schema-name is required.")]
	[Category("Unit")]
	[Description("Rejects missing batch request-level fields before remote dependencies are invoked.")]
	public void Create_Batch_Should_Reject_Request_Level_Guards(
		string packageName,
		string entitySchemaName,
		string expectedMessage) {
		// Arrange
		BusinessRulesBatchRequest request = new(packageName, entitySchemaName, [CreateRule()]);

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a batch with no rules before remote dependencies are invoked.")]
	public void Create_Batch_Should_Reject_When_Rules_Empty() {
		// Arrange
		BusinessRulesBatchRequest request = new("UsrPkg", "UsrOrder", []);

		// Act
		Action act = () => _service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rules is required and must contain at least one rule.",
				because: "an empty batch is a request-level error");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Marks every converted rule of the batch as failed with the save error when the single add-on save throws, while pre-save validation failures keep their own error.")]
	public void Create_Batch_Should_Mark_All_Pending_Failed_When_Save_Throws() {
		// Arrange
		_addonSchemaDesignerClient
			.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(_ => throw new InvalidOperationException("Add-on save failed."));
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Good rule 1"),
				CreateRule(caption: "Bad rule", leftPath: "MissingStatus"),
				CreateRule(caption: "Good rule 2", actions: [new MakeReadOnlyBusinessRuleAction(["Status"])])
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Create(request);

		// Assert
		results.Should().HaveCount(3, because: "every input rule still gets an outcome");
		results[0].Success.Should().BeFalse(because: "the single add-on save failed for the whole batch");
		results[0].Error.Should().Contain("Add-on save failed.", because: "all converted rules share the same save error");
		results[1].Success.Should().BeFalse(because: "this rule failed validation before the save");
		results[1].Error.Should().Contain("MissingStatus", because: "a pre-save validation failure keeps its own error, not the save error");
		results[2].Success.Should().BeFalse(because: "the single add-on save failed for the whole batch");
		results[2].Error.Should().Contain("Add-on save failed.", because: "all converted rules share the same save error");
	}

	[TestCase("", "UsrOrder", "package-name is required.")]
	[TestCase("UsrPkg", "", "entity-schema-name is required.")]
	[Category("Unit")]
	[Description("Rejects missing read request-level fields before remote dependencies are invoked.")]
	public void Read_Should_Reject_Request_Level_Guards(
		string packageName,
		string entitySchemaName,
		string expectedMessage) {
		// Arrange
		BusinessRulesReadRequest request = new(packageName, entitySchemaName);

		// Act
		Action act = () => _service.Read(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage(expectedMessage,
				because: "read request guards must run before any remote call");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Reads the persisted rules through the correct add-on schema request (BusinessRule addon, EntitySchemaManager, full hierarchy) and maps them to friendly rules.")]
	public void Read_Should_Return_Persisted_Rules_Through_Correct_Addon_Request() {
		// Arrange
		BusinessRulesReadRequest request = new("UsrPkg", "UsrOrder");

		// Act
		IReadOnlyList<BusinessRule> models = _service.Read(request);

		// Assert
		models.Should().ContainSingle(because: "the fixture add-on schema persists exactly one parent rule");
		models[0].Name.Should().Be("BusinessRule_old",
			because: "the rule must carry the persisted internal rule name as the update/delete match key");
		models[0].Caption.Should().Be("Existing rule",
			because: "the rule must carry the persisted caption");
		_addonSchemaDesignerClient.Received(1).GetSchema(
			Arg.Is<AddonGetRequestDto>(dto =>
				dto.AddonName == "BusinessRule"
				&& dto.TargetSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& dto.TargetParentSchemaUId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
				&& dto.TargetPackageUId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
				&& dto.TargetSchemaManagerName == "EntitySchemaManager"
				&& dto.UseFullHierarchy));
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Fails a rule without a name as an isolated per-rule failure result instead of throwing, and performs no add-on work when it is the only rule.")]
	public void Update_Should_Fail_Per_Rule_Without_Save_When_Name_Is_Missing() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[CreateRule(caption: "No name")]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().ContainSingle(because: "one input rule yields one outcome");
		results[0].Success.Should().BeFalse(because: "update requires the rule name as the match key");
		results[0].Name.Should().Be("No name",
			because: "a rule without a name is identified by its caption in the outcome");
		results[0].Error.Should().Be("name is required to update a business rule.",
			because: "the failure must tell the caller the match key is missing");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Replaces the matched rule in place preserving its persisted rule and case uIds, trims the caller-supplied name, applies the explicit enabled=false intent, upserts the caption resource, and saves exactly once.")]
	public void Update_Should_Replace_Existing_Rule_Preserving_Identity_And_Honoring_Enabled() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Updated caption") with {
					Name = " BusinessRule_old ",
					Enabled = false
				}
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().ContainSingle(because: "one input rule yields one outcome");
		results[0].Success.Should().BeTrue(because: "the trimmed name matches the persisted rule");
		results[0].Name.Should().Be("BusinessRule_old",
			because: "the caller-supplied name must be trimmed before matching and reporting");
		_savedAddonSchema.Should().NotBeNull(because: "a matched update must persist the mutated metadata");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
		rules.Should().ContainSingle(because: "the matched rule is replaced in place without adding new rules");
		rules[0].GetProperty("uId").GetString().Should().Be("existing-rule",
			because: "the persisted rule uId must be preserved so the platform stores a short diff");
		rules[0].GetProperty("name").GetString().Should().Be("BusinessRule_old",
			because: "the replacement keeps the persisted internal rule name");
		rules[0].GetProperty("enabled").GetBoolean().Should().BeFalse(
			because: "the caller's explicit enabled=false intent must be applied");
		rules[0].GetProperty("caption").GetString().Should().Be("Updated caption",
			because: "the replacement carries the new caption");
		rules[0].GetProperty("cases")[0].GetProperty("uId").GetString().Should().Be("existing-case",
			because: "the persisted case uId must be grafted onto the replacement's single case");
		_savedAddonSchema.Resources.Should().Contain(resource =>
				resource.Key == "existing-rule.Caption" && resource.Value[0].Value == "Updated caption",
			because: "the caption resource must be upserted with the new caption");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
		_addonSchemaDesignerClient.Received(1).BuildConfiguration();
	}

	[Test]
	[Category("Unit")]
	[Description("Isolates a per-rule validation failure inside an update batch: the invalid rule fails alone while the valid rule is still updated with a single save.")]
	public void Update_Should_Isolate_Validation_Failure_And_Still_Save_Valid_Rules() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(BuildAddonSchemaWithRules(("BusinessRule_old", "existing-rule"), ("BusinessRule_bad", "existing-bad")));
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Good rule") with { Name = "BusinessRule_old" },
				CreateRule(caption: "Bad rule", leftPath: "MissingStatus") with { Name = "BusinessRule_bad" }
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().HaveCount(2, because: "every input rule gets an outcome in input order");
		results[0].Success.Should().BeTrue(because: "the valid rule must be updated despite the failing sibling");
		results[1].Success.Should().BeFalse(because: "the second rule references an unknown attribute");
		results[1].Error.Should().Contain("MissingStatus",
			because: "the validation failure keeps its own error message");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[TestCase(true)]
	[TestCase(false)]
	[Category("Unit")]
	[Description("Rejects delete requests without rule names before any remote schema or add-on work starts.")]
	public void Delete_Should_Reject_When_RuleNames_Are_Missing(bool useNullList) {
		// Arrange
		BusinessRulesDeleteRequest request = new(
			"UsrPkg",
			"UsrOrder",
			useNullList ? null! : []);

		// Act
		Action act = () => _service.Delete(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule-names is required and must contain at least one rule name.",
				because: "delete without rule names is a request-level error");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().GetSchema(default!);
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Deletes the named rule and its caption resource through the correct add-on schema request, saving exactly once.")]
	public void Delete_Should_Remove_Rule_And_Caption_Resource_When_Name_Matches() {
		// Arrange
		BusinessRulesDeleteRequest request = new("UsrPkg", "UsrOrder", ["BusinessRule_old"]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Delete(request);

		// Assert
		results.Should().ContainSingle(because: "one input name yields one outcome");
		results[0].Success.Should().BeTrue(because: "the name matches the persisted rule");
		_addonSchemaDesignerClient.Received(1).GetSchema(
			Arg.Is<AddonGetRequestDto>(dto =>
				dto.AddonName == "BusinessRule"
				&& dto.TargetSchemaManagerName == "EntitySchemaManager"
				&& dto.UseFullHierarchy));
		_savedAddonSchema.Should().NotBeNull(because: "a matched delete must persist the mutated metadata");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		metaData.RootElement.GetProperty("rules").GetArrayLength().Should().Be(0,
			because: "the only persisted rule was deleted");
		_savedAddonSchema.Resources.Should().NotContain(resource => resource.Key == "existing-rule.Caption",
			because: "the deleted rule's caption resource must be removed");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
		_addonSchemaDesignerClient.Received(1).ResetClientScriptCache();
		_addonSchemaDesignerClient.Received(1).BuildConfiguration();
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a not-found failure without saving when the only update rule names a rule absent from the schema.")]
	public void Update_Should_Report_Not_Found_Without_Saving_When_Name_Is_Unknown() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[CreateRule(caption: "Missing") with { Name = "BusinessRule_missing" }]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().ContainSingle(because: "one input rule yields one outcome");
		results[0].Success.Should().BeFalse(because: "no persisted rule matches the requested name");
		results[0].Error.Should().Be("Business rule 'BusinessRule_missing' was not found.",
			because: "the failure must name the missing rule so the caller can correct it");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Isolates an unknown-name failure inside a mixed batch: the matched rule is still updated with a single save while the unknown name fails alone.")]
	public void Update_Should_Isolate_Unknown_Name_When_Batch_Has_Matched_Rules() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "Good") with { Name = "BusinessRule_old" },
				CreateRule(caption: "Missing") with { Name = "BusinessRule_missing" }
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().HaveCount(2, because: "every input rule gets an outcome in input order");
		results[0].Success.Should().BeTrue(because: "the matched rule must still be updated despite the failing sibling");
		results[1].Success.Should().BeFalse(because: "no persisted rule matches the second name");
		results[1].Error.Should().Contain("BusinessRule_missing", because: "the failure must name the missing rule");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a repeated rule name inside one update batch: the first occurrence applies and the repeat fails per-item so the second definition cannot double-apply.")]
	public void Update_Should_Reject_Repeated_Name_Within_One_Batch() {
		// Arrange
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "First") with { Name = "BusinessRule_old" },
				CreateRule(caption: "Second") with { Name = "BusinessRule_old" }
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().HaveCount(2, because: "every input rule gets an outcome in input order");
		results[0].Success.Should().BeTrue(because: "the first occurrence of the name applies normally");
		results[1].Success.Should().BeFalse(because: "the repeated name must not double-apply within one batch");
		results[1].Error.Should().Contain("more than once", because: "the failure must explain the in-batch repeat");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails an update item when the persisted metadata carries more than one parent rule with the requested name, without saving.")]
	public void Update_Should_Fail_Item_When_Persisted_Name_Is_Ambiguous() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(BuildAddonSchemaWithRules(("Dup_Rule", "dup-a"), ("dup_rule", "dup-b")));
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[CreateRule(caption: "Dup") with { Name = "Dup_Rule" }]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results[0].Success.Should().BeFalse(because: "an ambiguous name must not update an arbitrary rule");
		results[0].Error.Should().Contain("more than one rule", because: "the failure must explain the ambiguity");
		_addonSchemaDesignerClient.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Marks every pending (matched) update item failed with the save error when the single batch save throws, preserving single-save atomicity.")]
	public void Update_Should_Fail_All_Pending_With_Save_Error_When_Save_Throws() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(BuildAddonSchemaWithRules(("BusinessRule_old", "existing-rule"), ("BusinessRule_two", "existing-two")));
		_addonSchemaDesignerClient
			.When(client => client.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(_ => throw new InvalidOperationException("Add-on save failed."));
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				CreateRule(caption: "One") with { Name = "BusinessRule_old" },
				CreateRule(caption: "Two") with { Name = "BusinessRule_two" }
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results.Should().HaveCount(2, because: "every input rule still gets an outcome");
		results.Should().OnlyContain(result => !result.Success,
			because: "the single batch save failed, so no item can report success");
		results.Should().OnlyContain(result => result.Error == "Add-on save failed.",
			because: "all pending items share the same save error under single-save atomicity");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves the persisted enabled flag when the update rule omits enabled.")]
	public void Update_Should_Preserve_Existing_Enabled_When_Omitted() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto {
				MetaData = $$"""
					{
					  "typeName": "{{BusinessRuleConstants.BusinessRulesMetadataTypeName}}",
					  "rules": [
					    {
					      "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
					      "uId": "existing-rule",
					      "name": "BusinessRule_old",
					      "enabled": false,
					      "caption": "Existing rule",
					      "cases": [{ "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}", "uId": "existing-case", "actions": [] }],
					      "triggers": []
					    }
					  ]
					}
					""",
				Resources = []
			});
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[CreateRule(caption: "Keep disabled") with { Name = "BusinessRule_old" }]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results[0].Success.Should().BeTrue(because: "the trimmed name matches the persisted rule");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		metaData.RootElement.GetProperty("rules")[0].GetProperty("enabled").GetBoolean().Should().BeFalse(
			because: "omitting enabled on update must preserve the persisted enabled=false value");
	}

	[Test]
	[Category("Unit")]
	[Description("Replaces an apply-filter rule in place, preserves its persisted parent uId, removes its old autogenerated child, appends the regenerated child anchored to the preserved uId, and saves once.")]
	public void Update_Should_Regenerate_ApplyFilter_Children_And_Remove_Old_When_Name_Matches() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto {
				MetaData = $$"""
					{
					  "typeName": "{{BusinessRuleConstants.BusinessRulesMetadataTypeName}}",
					  "rules": [
					    {
					      "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
					      "uId": "af-parent",
					      "name": "BusinessRule_af",
					      "enabled": true,
					      "caption": "Filter",
					      "cases": [{ "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}", "uId": "af-case", "actions": [] }],
					      "triggers": []
					    },
					    {
					      "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
					      "uId": "af-child",
					      "name": "Autogenerated_af-parent_ClearValue",
					      "enabled": true,
					      "caption": "ChildRule-af-parent-ClearValue",
					      "parentUId": "af-parent",
					      "cases": [],
					      "triggers": []
					    }
					  ]
					}
					""",
				Resources = [
					new AddonResourceDto { Key = "af-child.Caption", Value = [new AddonResourceValueDto { Key = "en-US", Value = "ChildRule-af-parent-ClearValue" }] }
				]
			});
		BusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrOrder",
			[
				new BusinessRule(
					"Filter city by country",
					new BusinessRuleConditionGroup("AND", []),
					[
						new ApplyFilterBusinessRuleAction("City", "Country", "Country", null, clearValue: true, populateValue: false)
					]) { Name = "BusinessRule_af" }
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results[0].Success.Should().BeTrue(because: "the name matches the persisted apply-filter parent");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
		rules.Should().HaveCount(2,
			because: "the replaced parent stays plus one regenerated clear child; the old child is removed");
		rules[0].GetProperty("uId").GetString().Should().Be("af-parent",
			because: "the apply-filter parent must keep its persisted rule uId");
		rules.Should().NotContain(rule => rule.GetProperty("uId").GetString() == "af-child",
			because: "the old autogenerated child of the replaced rule must be removed");
		rules[1].GetProperty("parentUId").GetString().Should().Be("af-parent",
			because: "the regenerated child must be anchored to the preserved parent uId");
		rules[1].GetProperty("name").GetString().Should().Be("Autogenerated_af-parent_ClearValue",
			because: "the parent uId embedded in the regenerated child name must be the preserved uId");
		_savedAddonSchema.Resources.Should().NotContain(resource => resource.Key == "af-child.Caption",
			because: "the caption resource of the removed old child must be deleted");
		_addonSchemaDesignerClient.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Category("Unit")]
	[Description("Honors caller-supplied block uIds from the update payload so unchanged condition blocks keep their identity.")]
	public void Update_Should_Preserve_Block_UIds_From_Payload() {
		// Arrange
		const string conditionUId = "aaaaaaaa-0000-0000-0000-000000000001";
		BusinessRule rule = new(
			"Keep identity",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\""))) {
						UId = conditionUId
					}
				]),
			[new MakeRequiredBusinessRuleAction(["Owner"])]) {
			Name = "BusinessRule_old"
		};
		BusinessRulesBatchRequest request = new("UsrPkg", "UsrOrder", [rule]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = _service.Update(request);

		// Assert
		results[0].Success.Should().BeTrue(because: "the name matches the persisted rule");
		using JsonDocument metaData = JsonDocument.Parse(_savedAddonSchema!.MetaData);
		metaData.RootElement.GetProperty("rules")[0]
			.GetProperty("cases")[0].GetProperty("condition").GetProperty("conditions")[0]
			.GetProperty("uId").GetString().Should().Be(conditionUId,
				because: "update must honor the caller-supplied condition block uId instead of minting a fresh one");
	}

	private static AddonSchemaDto BuildAddonSchemaWithRules(params (string Name, string UId)[] rules) {
		string rulesJson = string.Join(",", rules.Select(rule => $$"""
			{
			  "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			  "uId": "{{rule.UId}}",
			  "name": "{{rule.Name}}",
			  "enabled": true,
			  "caption": "{{rule.Name}}",
			  "cases": [{ "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}", "uId": "{{rule.UId}}-case", "actions": [] }],
			  "triggers": []
			}
			"""));
		return new AddonSchemaDto {
			MetaData = $$"""{ "typeName": "{{BusinessRuleConstants.BusinessRulesMetadataTypeName}}", "rules": [{{rulesJson}}] }""",
			Resources = []
		};
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
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000005"),
					Name = "BaseScore",
					DataValueType = 4
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000006"),
					Name = "BonusScore",
					DataValueType = 4
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000007"),
					Name = "TotalScore",
					DataValueType = 4
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000008"),
					Name = "Country",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "Country"
					}
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000009"),
					Name = "City",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "City"
					}
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000010"),
					Name = "City.Country",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "Country"
					}
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000011"),
					Name = "CreatedBy",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "Contact"
					}
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000012"),
					Name = "Clan",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "NinjaClan"
					}
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000013"),
					Name = "Clan.CreatedBy",
					DataValueType = 10,
					ReferenceSchema = new EntityDesignSchemaDto {
						Name = "Contact"
					}
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
				},
				new AddonResourceDto {
					Key = "LocalizableStrings.Module.Title",
					Value = [
						new AddonResourceValueDto {
							Key = "en-US",
							Value = "Module title"
						}
					]
				}
			]
		};
	}

	private static BusinessRule CreateRule(
		string leftPath = "Status",
		string caption = "Require owner for drafts",
		List<BusinessRuleAction>? actions = null) {
		return new BusinessRule(
			caption,
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", leftPath, null),
						"equal",
						new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("\"Draft\"")))
				]),
			actions ?? [
				new MakeRequiredBusinessRuleAction(["Owner"])
			]);
	}
}

