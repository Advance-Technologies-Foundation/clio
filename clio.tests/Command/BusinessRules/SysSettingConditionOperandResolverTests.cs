using System;
using System.Collections.Generic;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Property("Module", "Command")]
public sealed class SysSettingConditionOperandResolverTests {

	private static BusinessRule RuleWithSetting(string settingCode) =>
		new(
			"caption",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysSetting", sysSettingName: settingCode),
						"is-filled-in")
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

	private static ISysSettingsManager ManagerReturning(
		string code, (string ValueTypeName, string? ReferenceSchemaName)? result) {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetSysSettingTypeByCode(code).Returns(result);
		return manager;
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a Boolean sys-setting operand to the Boolean business-rule data value type.")]
	public void Resolve_Should_Map_Boolean_Setting() {
		// Arrange
		ISysSettingsManager manager = ManagerReturning("UseNewShell", ("Boolean", null));
		SysSettingConditionOperandResolver sut = new(manager);

		// Act
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> map = sut.Resolve(RuleWithSetting("UseNewShell"));

		// Assert
		map["UseNewShell"].DataValueTypeName.Should().Be("Boolean",
			because: "a Boolean setting must resolve to the Boolean data value type");
		map["UseNewShell"].ReferenceSchemaName.Should().BeNull(
			because: "a scalar setting has no reference schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Normalizes the sys-setting-only numeric aliases (Decimal->Float, Currency->Money) to canonical data value types.")]
	[TestCase("Decimal", "Float")]
	[TestCase("Currency", "Money")]
	public void Resolve_Should_Normalize_Numeric_Aliases(string settingType, string expected) {
		// Arrange
		ISysSettingsManager manager = ManagerReturning("MySetting", (settingType, null));
		SysSettingConditionOperandResolver sut = new(manager);

		// Act
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> map = sut.Resolve(RuleWithSetting("MySetting"));

		// Assert
		map["MySetting"].DataValueTypeName.Should().Be(expected,
			because: "sys-setting numeric aliases must map onto the canonical business-rule data value type");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a Lookup sys-setting operand to Lookup carrying its reference schema name.")]
	public void Resolve_Should_Map_Lookup_Setting_With_Reference_Schema() {
		// Arrange
		ISysSettingsManager manager = ManagerReturning("DefaultOfficeCountry", ("Lookup", "Country"));
		SysSettingConditionOperandResolver sut = new(manager);

		// Act
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> map = sut.Resolve(RuleWithSetting("DefaultOfficeCountry"));

		// Assert
		map["DefaultOfficeCountry"].DataValueTypeName.Should().Be("Lookup",
			because: "a Lookup setting must resolve to the Lookup data value type");
		map["DefaultOfficeCountry"].ReferenceSchemaName.Should().Be("Country",
			because: "a Lookup operand must carry its reference schema for comparison compatibility");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a Lookup sys-setting whose reference schema could not be resolved on the environment.")]
	public void Resolve_Should_Throw_For_Lookup_Without_Reference_Schema() {
		// Arrange
		ISysSettingsManager manager = ManagerReturning("BrokenLookup", ("Lookup", null));
		SysSettingConditionOperandResolver sut = new(manager);

		// Act
		Action act = () => sut.Resolve(RuleWithSetting("BrokenLookup"));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*reference schema could not be resolved*",
				because: "a Lookup operand with no reference schema cannot be compared and must be rejected with a clear reason");
	}

	[TestCase("SecureText", "SecureText")]
	[TestCase("securetext", "SecureText")]
	[TestCase("Binary", "Binary")]
	[TestCase("BINARY", "Binary")]
	[Category("Unit")]
	[Description("Rejects Binary and SecureText settings (case-insensitively) as condition operands, each with its own reason.")]
	public void Resolve_Should_Reject_Unsupported_Setting_Types(string settingType, string expectedWord) {
		// Arrange
		ISysSettingsManager manager = ManagerReturning("Sensitive", (settingType, null));
		SysSettingConditionOperandResolver sut = new(manager);

		// Act
		Action act = () => sut.Resolve(RuleWithSetting("Sensitive"));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage($"*{expectedWord}*",
				because: "the SecureText/Binary rejection must fire regardless of casing and name the offending type so a secret is never embedded in a rule");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when a referenced system setting does not exist on the target environment.")]
	public void Resolve_Should_Throw_For_Unknown_Setting() {
		// Arrange
		ISysSettingsManager manager = ManagerReturning("Missing", null);
		SysSettingConditionOperandResolver sut = new(manager);

		// Act
		Action act = () => sut.Resolve(RuleWithSetting("Missing"));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*does not exist*",
				because: "an unknown setting cannot be typed and must surface a clear resolution error");
	}

	[Test]
	[Category("Unit")]
	[Description("Performs no environment lookup and returns an empty map when the rule references no SysSetting operand.")]
	public void Resolve_Should_Return_Empty_When_No_SysSetting_Operand() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingConditionOperandResolver sut = new(manager);
		BusinessRule rule = new(
			"caption",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status"),
						"is-filled-in")
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> map = sut.Resolve(rule);

		// Assert
		map.Should().BeEmpty(because: "a rule without a SysSetting operand needs no resolution");
		manager.DidNotReceiveWithAnyArgs().GetSysSettingTypeByCode(default!);
	}
}
