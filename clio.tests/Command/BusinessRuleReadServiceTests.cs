using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class BusinessRuleReadServiceTests {
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private BusinessRuleReadService _service = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetBusinessRules).Returns("https://example.org/GetBusinessRules");
		_service = new BusinessRuleReadService(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	[Category("Unit")]
	[Description("Lists entity business rules through BusinessRulesManagerService using Model scope and normalized MCP action names.")]
	public void List_Should_Read_Entity_Rules_With_Model_Scope() {
		// Arrange
		string? requestBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Do<string>(body => requestBody = body),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(CreateServiceResponse(CreateBusinessRuleConfig(
				"RuleUId",
				"BusinessRule_Entity",
				"Require owner",
				"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionRequiredElement",
				"Owner")));

		// Act
		BusinessRuleListResponse result = _service.List(new BusinessRuleReadRequest("entity", "Contact"));

		// Assert
		result.Success.Should().BeTrue(
			because: "a successful platform response should produce a successful list response");
		result.ScopeType.Should().Be(BusinessRuleScopeTypes.Entity,
			because: "entity scope should be normalized in the response");
		result.Rules.Should().ContainSingle(
			because: "the service response contains exactly one business rule");
		BusinessRuleReadItem rule = result.Rules.Single();
		rule.UId.Should().Be("RuleUId",
			because: "stable rule identity should be preserved for follow-up get/edit/delete flows");
		rule.Caption.Should().Be("Require owner",
			because: "caption is part of the normalized response");
		rule.Condition!.LogicalOperation.Should().Be("AND",
			because: "platform logical operation 1 maps to the MCP AND operator");
		rule.Actions.Single().ActionType.Should().Be("make-required",
			because: "platform action type names should be normalized to MCP action discriminators");
		rule.Actions.Single().FieldSelectionItems.Should().Equal(["Owner"],
			because: "field-selection action items should be split into normalized string items");
		JsonDocument.Parse(requestBody!).RootElement
			.GetProperty("scopes")[0]
			.GetProperty("type")
			.GetString()
			.Should().Be("Model",
				because: "entity scope must be sent to the platform as Model");
	}

	[Test]
	[Category("Unit")]
	[Description("Lists page business rules through BusinessRulesManagerService using ViewModel scope and normalized page action names.")]
	public void List_Should_Read_Page_Rules_With_ViewModel_Scope() {
		// Arrange
		string? requestBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Do<string>(body => requestBody = body),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(CreateServiceResponse(CreateBusinessRuleConfig(
				"PageRuleUId",
				"BusinessRule_Page",
				"Hide button",
				"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionHideElement",
				"SaveButton")));

		// Act
		BusinessRuleListResponse result = _service.List(new BusinessRuleReadRequest("page", "Contact_FormPage"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page business rules should be read through the same platform manager endpoint");
		result.ScopeType.Should().Be(BusinessRuleScopeTypes.Page,
			because: "page scope should be normalized in the response");
		result.Rules.Single().Actions.Single().ActionType.Should().Be("hide-element",
			because: "page-only action type names should be normalized for MCP callers");
		JsonDocument.Parse(requestBody!).RootElement
			.GetProperty("scopes")[0]
			.GetProperty("type")
			.GetString()
			.Should().Be("ViewModel",
				because: "page scope must be sent to the platform as ViewModel");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an ambiguity response when get-business-rule matches more than one caption.")]
	public void Get_Should_Return_Ambiguity_When_Caption_Matches_Multiple_Rules() {
		// Arrange
		string rulesConfig = $"""
			[
			  {CreateBusinessRuleJson("Rule1", "BusinessRule_1", "Duplicate caption", "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionRequiredElement", "Owner")},
			  {CreateBusinessRuleJson("Rule2", "BusinessRule_2", "Duplicate caption", "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement", "Name")}
			]
			""";
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(CreateServiceResponse(rulesConfig));

		// Act
		BusinessRuleGetResponse result = _service.Get(new BusinessRuleGetRequest(
			"entity",
			"Contact",
			null,
			null,
			"Duplicate caption"));

		// Assert
		result.Success.Should().BeFalse(
			because: "caption selectors can be ambiguous and must not pick a rule silently");
		result.Error.Should().Contain("ambiguous",
			because: "callers need a clear reason why no single rule was returned");
		result.Matches.Should().HaveCount(2,
			because: "the response should include identities that let the caller retry by ruleUId");
		result.Matches.Select(match => match.UId).Should().BeEquivalentTo(["Rule1", "Rule2"],
			because: "all ambiguous matching rule identities should be returned");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips unsupported custom action metadata while preserving the readable rule identity.")]
	public void List_Should_Skip_Unsupported_Action_Metadata() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(CreateServiceResponse(CreateBusinessRuleConfig(
				"RuleUId",
				"BusinessRule_Custom",
				"Custom action",
				"Terrasoft.Core.BusinessRules.Models.Actions.CustomAction",
				"Owner")));

		// Act
		BusinessRuleListResponse result = _service.List(new BusinessRuleReadRequest("entity", "Contact"));

		// Assert
		result.Success.Should().BeTrue(
			because: "unsupported fragments should not hide the whole readable rule");
		result.Rules.Single().Caption.Should().Be("Custom action",
			because: "the readable rule identity should still be returned");
		result.Rules.Single().Actions.Should().BeEmpty(
			because: "unsupported custom actions are outside the normalized MCP schema");
	}

	private static string CreateServiceResponse(string businessRulesConfig) =>
		JsonSerializer.Serialize(new {
			success = true,
			businessRules = new[] {
				new {
					schemaName = "Contact",
					type = "Model",
					modelType = (string?)null,
					businessRulesConfig
				}
			}
		});

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
