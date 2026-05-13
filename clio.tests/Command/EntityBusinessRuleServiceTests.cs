using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
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
	private EntityBusinessRuleService _service = null!;
	private AddonSchemaDto? _savedAddonSchema;

	[SetUp]
	public void SetUp() {
		_savedAddonSchema = null;
		_addonSchemaDesignerClient = Substitute.For<IAddonSchemaDesignerClient>();
		_entitySchemaDesignerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_formulaValidationService = Substitute.For<IBusinessRuleFormulaValidationService>();
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
		_service = new EntityBusinessRuleService(
			new BusinessRulePackageResolver(_applicationPackageListProvider),
			new EntityBusinessRuleAttributeProvider(new EntityBusinessRuleSchemaProvider(_entitySchemaDesignerClient)),
			new BusinessRuleAddonService(_addonSchemaDesignerClient),
			Substitute.For<ILocalEsqFilterBuilder>(),
			_formulaValidationService,
			Substitute.For<IFilterSchemaProvider>());
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
		EntityBusinessRuleCreateRequest request = new(
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
		EntityBusinessRuleCreateRequest request = new(
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
		EntityBusinessRuleCreateRequest request = new(
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
	[Description("Calls BuildConfiguration after a successful save so that the ConfigurationHash is updated and offline users get cache invalidation on their next startup.")]
	public void Create_Should_Call_BuildConfiguration_After_Successful_Save() {
		// Arrange
		EntityBusinessRuleCreateRequest request = new(
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
		EntityBusinessRuleCreateRequest request = new(
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
		EntityBusinessRuleCreateRequest request = new(
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
		EntityBusinessRuleCreateRequest request = new(
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
		EntityBusinessRuleCreateRequest request = new(
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

