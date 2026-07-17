using System;
using System.Collections.Generic;
using System.Linq;
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
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://creatio/DataService/json/SyncReply/SelectQuery");
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rows\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\"}],\"success\":true}");
		_validator = new BusinessRuleLookupReferenceValidator(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	[Category("Unit")]
	[Description("Checks lookup condition and set-values constants against their referenced lookup schemas through an ESQ SelectQuery.")]
	public void Validate_Should_Check_Lookup_Condition_And_SetValues_Constants() {
		// Arrange
		BusinessRule rule = CreateLookupRule(
			conditionValue: "11111111-1111-1111-1111-111111111111",
			setValue: "22222222-2222-2222-2222-222222222222");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = CreateAttributeMap();

		// Act
		_validator.Validate(rule, attributeMap);

		// Assert — both the condition and the set-values lookup are validated via the DataService SelectQuery route
		_serviceUrlBuilder.Received(2).Build(ServiceUrlBuilder.KnownRoute.Select);
		_applicationClient.Received(2).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default!, default, default, default);
		// The condition lookup (Contact) and the set-values lookup (Account) are each queried by Id
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Contact\"")
				&& body.Contains("11111111-1111-1111-1111-111111111111")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Account\"")
				&& body.Contains("22222222-2222-2222-2222-222222222222")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		// The Id (Guid primary key) filter must use the Guid data value type (0), not Text (1).
		// Parse the captured request bodies rather than substring-matching so the assertion is
		// robust to JSON formatting (indentation, property order) changes.
		List<string> postedBodies = _applicationClient.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Select(call => (string)call.GetArguments()[1]!)
			.ToList();
		postedBodies.Should().HaveCount(2,
			because: "both lookup references are validated with a SelectQuery POST");
		foreach (string body in postedBodies) {
			using JsonDocument document = JsonDocument.Parse(body);
			JsonElement idFilter = document.RootElement
				.GetProperty("filters").GetProperty("items")
				.EnumerateObject().First().Value;
			int dataValueType = idFilter
				.GetProperty("rightExpression").GetProperty("parameter")
				.GetProperty("dataValueType").GetInt32();
			dataValueType.Should().Be(0,
				because: "the Id primary-key filter must use the Guid data value type (0), not Text (1)");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a server-side SelectQuery failure as the module's ArgumentException contract, not a raw InvalidOperationException.")]
	public void Validate_Should_Surface_Server_Failure_As_ArgumentException() {
		// Arrange — SelectQuery returns a failure envelope (ExecuteSelectQuery throws InvalidOperationException)
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"server boom\"}}");
		BusinessRule rule = CreateLookupRule(
			conditionValue: "11111111-1111-1111-1111-111111111111",
			setValue: "22222222-2222-2222-2222-222222222222");

		// Act
		Action act = () => _validator.Validate(rule, CreateAttributeMap());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*could not be verified*server boom*",
				because: "PageBusinessRuleValidator only catches ArgumentException, so server failures must be translated");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a clear validation error when a lookup constant is a valid GUID but no referenced record exists.")]
	public void Validate_Should_Reject_Missing_Lookup_Record() {
		// Arrange — SelectQuery succeeds but returns no rows
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rows\":[],\"success\":true}");
		BusinessRule rule = CreateLookupRule(
			conditionValue: "11111111-1111-1111-1111-111111111111",
			setValue: "22222222-2222-2222-2222-222222222222");

		// Act
		Action act = () => _validator.Validate(rule, CreateAttributeMap());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*record '11111111-1111-1111-1111-111111111111' was not found in lookup schema 'Contact'*execute-esq*",
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
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			default!, default!, default, default, default);
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
				because: "existence validation needs the target lookup schema name to build a deterministic SelectQuery");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a scoped lookup condition operand through its scopeId::path composite key when validating the lookup constant, instead of throwing on a missing bare-path root key.")]
	public void Validate_Should_Resolve_Scoped_Lookup_Operand_By_Composite_Key() {
		// Arrange
		BusinessRule rule = new(
			"Gate on a DataSource contact",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Contact", scopeId: "PDS"),
						"equal",
						new BusinessRuleExpression("Const", null, Json("11111111-1111-1111-1111-111111111111")))
				]),
			[
				new HideElementBusinessRuleAction(["ReminderLabel"])
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS::Contact"] = new("Contact", "Lookup", "Contact")
			};

		// Act
		_validator.Validate(rule, attributeMap);

		// Assert — the scoped Contact descriptor drives a SelectQuery against the Contact schema, proving the
		// composite key resolved; a bare-path lookup would have thrown KeyNotFoundException on the missing root.
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Contact\"")
				&& body.Contains("11111111-1111-1111-1111-111111111111")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
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
