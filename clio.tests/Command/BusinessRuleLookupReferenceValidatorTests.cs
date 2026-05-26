using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleLookupReferenceValidatorTests {
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private BusinessRuleLookupReferenceValidator _validator = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\"}]}");
		_validator = new BusinessRuleLookupReferenceValidator(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	[Category("Unit")]
	[Description("Checks lookup condition and set-values constants against their referenced lookup schemas through OData.")]
	public void Validate_Should_Check_Lookup_Condition_And_SetValues_Constants() {
		// Arrange
		BusinessRule rule = CreateLookupRule(
			conditionValue: "11111111-1111-1111-1111-111111111111",
			setValue: "22222222-2222-2222-2222-222222222222");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = CreateAttributeMap();

		// Act
		_validator.Validate(rule, attributeMap);

		// Assert
		_serviceUrlBuilder.Received(1).Build(
			BusinessRuleLookupReferenceValidator.BuildLookupExistsPath(
				"Contact",
				Guid.Parse("11111111-1111-1111-1111-111111111111")));
		_serviceUrlBuilder.Received(1).Build(
			BusinessRuleLookupReferenceValidator.BuildLookupExistsPath(
				"Account",
				Guid.Parse("22222222-2222-2222-2222-222222222222")));
		_applicationClient.Received(2).ExecuteGetRequest(
			Arg.Any<string>(),
			30_000,
			1,
			1);
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a clear validation error when a lookup constant is a valid GUID but no referenced record exists.")]
	public void Validate_Should_Reject_Missing_Lookup_Record() {
		// Arrange
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		BusinessRule rule = CreateLookupRule(
			conditionValue: "11111111-1111-1111-1111-111111111111",
			setValue: "22222222-2222-2222-2222-222222222222");

		// Act
		Action act = () => _validator.Validate(rule, CreateAttributeMap());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*record '11111111-1111-1111-1111-111111111111' was not found in lookup schema 'Contact'*odata-read*",
				because: "coding agents should receive an actionable message that points them to lookup Id resolution");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips non-lookup constants and lookup AttributeValue sources because only literal lookup IDs need record existence validation.")]
	public void Validate_Should_Skip_Non_Lookup_Constants_And_AttributeValue_Sources() {
		// Arrange
		BusinessRule rule = new(
			"Copy values",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status"),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new SetValuesBusinessRuleAction([
					new BusinessRuleSetValueItem(
						new BusinessRuleExpression("AttributeValue", "PrimaryAccount"),
						new BusinessRuleExpression("AttributeValue", "Account"))
				])
			]);

		// Act
		_validator.Validate(rule, CreateAttributeMap());

		// Assert
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default!, default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Reports unresolved lookup metadata when a lookup attribute has no reference schema.")]
	public void Validate_Should_Reject_Lookup_Attribute_Without_Reference_Schema() {
		// Arrange
		BusinessRule rule = CreateLookupRule(
			conditionValue: "11111111-1111-1111-1111-111111111111",
			setValue: "22222222-2222-2222-2222-222222222222");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Owner"] = new("Owner", "Lookup", null),
				["PrimaryAccount"] = new("PrimaryAccount", "Lookup", "Account")
			};

		// Act
		Action act = () => _validator.Validate(rule, attributeMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value references lookup attribute 'Owner', but its reference schema cannot be resolved.",
				because: "existence validation needs the target lookup schema name to build a deterministic OData query");
	}

	private static BusinessRule CreateLookupRule(string conditionValue, string setValue) =>
		new(
			"Set account for owner",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Owner"),
						"equal",
						new BusinessRuleExpression("Const", null, Json(conditionValue)))
				]),
			[
				new SetValuesBusinessRuleAction([
					new BusinessRuleSetValueItem(
						new BusinessRuleExpression("AttributeValue", "PrimaryAccount"),
						new BusinessRuleExpression("Const", null, Json(setValue)))
				])
			]);

	private static IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> CreateAttributeMap() =>
		new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
			["Owner"] = new("Owner", "Lookup", "Contact"),
			["PrimaryAccount"] = new("PrimaryAccount", "Lookup", "Account"),
			["Account"] = new("Account", "Lookup", "Account"),
			["Status"] = new("Status", "Text", null)
		};

	private static JsonElement Json(string value) =>
		JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");
}
