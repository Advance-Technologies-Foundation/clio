using System;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class BusinessRuleReadServiceTests {
	private IBusinessRulePackageResolver _packageResolver = null!;
	private IEntityBusinessRuleSchemaProvider _entitySchemaProvider = null!;
	private IPageBusinessRuleSchemaProvider _pageSchemaProvider = null!;
	private IAddonSchemaDesignerClient _addonSchemaDesignerClient = null!;
	private BusinessRuleReadService _service = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_packageResolver = Substitute.For<IBusinessRulePackageResolver>();
		_entitySchemaProvider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		_pageSchemaProvider = Substitute.For<IPageBusinessRuleSchemaProvider>();
		_addonSchemaDesignerClient = Substitute.For<IAddonSchemaDesignerClient>();
		_packageResolver.ResolveUId("UsrApp").Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		_entitySchemaProvider.GetSchema("Contact", Arg.Any<Guid>()).Returns(new EntityDesignSchemaDto {
			UId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			ParentSchema = new EntityDesignSchemaDto {
				UId = Guid.Parse("33333333-3333-3333-3333-333333333333")
			}
		});
		_pageSchemaProvider.GetSchema("Contact_FormPage", Arg.Any<Guid>()).Returns(new PageBusinessRuleSchemaContext(
			"44444444-4444-4444-4444-444444444444",
			Guid.Parse("55555555-5555-5555-5555-555555555555"),
			new PageBundleInfo()));
		_service = new BusinessRuleReadService(
			_packageResolver,
			_entitySchemaProvider,
			_pageSchemaProvider,
			_addonSchemaDesignerClient);
	}

	[Test]
	[Category("Unit")]
	[Description("Lists entity business-rule summaries through package-scoped AddonSchemaDesigner metadata.")]
	public void List_Should_Read_Entity_Rule_Summaries_From_Addon_Metadata() {
		// Arrange
		AddonGetRequestDto? capturedRequest = null;
		_addonSchemaDesignerClient.GetSchema(Arg.Do<AddonGetRequestDto>(request => capturedRequest = request))
			.Returns(CreateAddonSchema(CreateBusinessRuleConfig(
				"RuleUId",
				"BusinessRule_Entity",
				"Inline caption",
				"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionRequiredElement",
				"Owner")));

		// Act
		BusinessRuleListResponse result = _service.List(new BusinessRuleReadRequest("UsrApp", "entity", "Contact"));

		// Assert
		result.Success.Should().BeTrue(
			because: "a successful add-on schema read should produce a successful list response");
		result.ScopeType.Should().Be(BusinessRuleScopeTypes.Entity,
			because: "entity scope should be normalized in the response");
		result.Rules.Should().ContainSingle(
			because: "the add-on metadata contains exactly one business rule");
		result.Rules.Single().Name.Should().Be("BusinessRule_Entity",
			because: "list should expose the agent-friendly rule name");
		result.Rules.Single().Caption.Should().Be("Require owner",
			because: "caption should be read from add-on resources when present");
		capturedRequest!.TargetSchemaManagerName.Should().Be(BusinessRuleConstants.EntitySchemaManagerName,
			because: "entity reads should use entity add-on metadata");
		capturedRequest.TargetPackageUId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"),
			because: "business-rule reads must stay scoped to the requested package");
	}

	[Test]
	[Category("Unit")]
	[Description("Lists page business-rule summaries through package-scoped AddonSchemaDesigner metadata.")]
	public void List_Should_Read_Page_Rule_Summaries_From_Addon_Metadata() {
		// Arrange
		AddonGetRequestDto? capturedRequest = null;
		_addonSchemaDesignerClient.GetSchema(Arg.Do<AddonGetRequestDto>(request => capturedRequest = request))
			.Returns(CreateAddonSchema(CreateBusinessRuleConfig(
				"PageRuleUId",
				"BusinessRule_Page",
				"Inline page caption",
				"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionHideElement",
				"SaveButton")));

		// Act
		BusinessRuleListResponse result = _service.List(new BusinessRuleReadRequest("UsrApp", "page", "Contact_FormPage"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page business rules should be read through the same add-on metadata service");
		result.ScopeType.Should().Be(BusinessRuleScopeTypes.Page,
			because: "page scope should be normalized in the response");
		result.Rules.Single().Name.Should().Be("BusinessRule_Page",
			because: "list should expose page business-rule names");
		result.Rules.Single().Caption.Should().Be("Require owner",
			because: "caption should be resolved from resources independently of scope");
		capturedRequest!.TargetSchemaManagerName.Should().Be(BusinessRuleConstants.ClientUnitSchemaManagerName,
			because: "page reads should use client-unit add-on metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("Gets a business rule by case-insensitive rule name and returns the normalized full body.")]
	public void Get_Should_Read_Rule_By_Case_Insensitive_Name() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(CreateAddonSchema(CreateBusinessRuleConfig(
				"RuleUId",
				"BusinessRule_Entity",
				"Inline caption",
				"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionRequiredElement",
				"Owner")));

		// Act
		BusinessRuleGetResponse result = _service.Get(new BusinessRuleGetRequest(
			"UsrApp",
			"entity",
			"Contact",
			"businessrule_entity"));

		// Assert
		result.Success.Should().BeTrue(
			because: "ruleName matching should be exact but case-insensitive");
		result.Rule!.Name.Should().Be("BusinessRule_Entity",
			because: "the selected rule should be returned");
		result.Rule.Caption.Should().Be("Require owner",
			because: "get should use localized caption resources");
		result.Rule.Condition!.LogicalOperation.Should().Be("AND",
			because: "platform logical operation 1 maps to the MCP AND operator");
		result.Rule.Actions.Single().ActionType.Should().Be("make-required",
			because: "get should return the normalized action body");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure when get-business-rule meets unsupported action metadata.")]
	public void Get_Should_Fail_For_Unsupported_Action_Metadata() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(CreateAddonSchema(CreateBusinessRuleConfig(
				"RuleUId",
				"BusinessRule_Custom",
				"Custom action",
				"Terrasoft.Core.BusinessRules.Models.Actions.CustomAction",
				"Owner")));

		// Act
		BusinessRuleGetResponse result = _service.Get(new BusinessRuleGetRequest(
			"UsrApp",
			"entity",
			"Contact",
			"BusinessRule_Custom"));

		// Assert
		result.Success.Should().BeFalse(
			because: "get should not silently return partial normalized data for unsupported shapes");
		result.Error.Should().Contain("not supported",
			because: "callers need a clear reason why the rule cannot be represented");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps list summaries readable when full business-rule metadata contains unsupported action shapes.")]
	public void List_Should_Ignore_Unsupported_Action_Metadata() {
		// Arrange
		_addonSchemaDesignerClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(CreateAddonSchema(CreateBusinessRuleConfig(
				"RuleUId",
				"BusinessRule_Custom",
				"Custom action",
				"Terrasoft.Core.BusinessRules.Models.Actions.CustomAction",
				"Owner")));

		// Act
		BusinessRuleListResponse result = _service.List(new BusinessRuleReadRequest("UsrApp", "entity", "Contact"));

		// Assert
		result.Success.Should().BeTrue(
			because: "list should not normalize full unsupported rule bodies");
		result.Rules.Single().Name.Should().Be("BusinessRule_Custom",
			because: "the readable rule identity should still be returned");
		result.Rules.Single().Caption.Should().Be("Require owner",
			because: "list should still return the caption summary");
	}

	private static AddonSchemaDto CreateAddonSchema(string rulesConfig) =>
		new() {
			MetaData = $$"""
				{
				  "typeName": "Terrasoft.Core.BusinessRules.BusinessRulesMetaData",
				  "rules": {{rulesConfig}}
				}
				""",
			Resources = [
				new AddonResourceDto {
					Key = "AddonConfig.Rules.RuleUId.Caption",
					Value = [
						new AddonResourceValueDto {
							Key = EntitySchemaDesignerSupport.DefaultCultureName,
							Value = "Require owner"
						}
					]
				},
				new AddonResourceDto {
					Key = "AddonConfig.Rules.PageRuleUId.Caption",
					Value = [
						new AddonResourceValueDto {
							Key = EntitySchemaDesignerSupport.DefaultCultureName,
							Value = "Require owner"
						}
					]
				}
			]
		};

	private static string CreateBusinessRuleConfig(
		string uId,
		string name,
		string caption,
		string actionTypeName,
		string actionItems) =>
		$"[{CreateBusinessRuleJson(uId, name, caption, actionTypeName, actionItems)}]";

	private static string CreateBusinessRuleJson(
		string uId,
		string name,
		string caption,
		string actionTypeName,
		string actionItems) =>
		$$"""
		{
		  "typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
		  "uId": "{{uId}}",
		  "name": "{{name}}",
		  "caption": "{{caption}}",
		  "enabled": true,
		  "cases": [
		    {
		      "typeName": "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase",
		      "uId": "CaseUId",
		      "condition": {
		        "typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition",
		        "uId": "ConditionGroupUId",
		        "logicalOperation": 1,
		        "conditions": [
		          {
		            "typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleCondition",
		            "uId": "ConditionUId",
		            "leftExpression": {
		              "typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleAttributeExpression",
		              "type": "AttributeValue",
		              "path": "Status"
		            },
		            "comparisonType": 1
		          }
		        ]
		      },
		      "actions": [
		        {
		          "typeName": "{{actionTypeName}}",
		          "uId": "ActionUId",
		          "enabled": true,
		          "items": "{{actionItems}}"
		        }
		      ]
		    }
		  ],
		  "triggers": []
		}
		""";
}
