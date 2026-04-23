using System;
using System.Text.Json;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleHelpersTests {
	[TestCase("Date", "\"2025-01-15\"", 2025, 1, 15, 0, 0, 0)]
	[TestCase("DateTime", "\"2025-01-15T13:45:30+02:00\"", 2025, 1, 15, 11, 45, 30)]
	[TestCase("Time", "\"13:45:30+02:00\"", 1, 1, 1, 11, 45, 30)]
	[Category("Unit")]
	[Description("Normalizes Date DateTime and Time constants into UTC DateTime values before metadata serialization.")]
	public void TryConvertTemporalConstant_Should_Normalize_Supported_Temporal_Constants(
		string dataValueTypeName,
		string json,
		int year,
		int month,
		int day,
		int hour,
		int minute,
		int second) {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);

		// Act
		bool converted = BusinessRuleHelpers.TryConvertTemporalConstant(element, dataValueTypeName, out DateTime normalizedValue);

		// Assert
		converted.Should().BeTrue(
			because: $"{dataValueTypeName} constants should normalize before business-rule metadata serialization");
		normalizedValue.Should().Be(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
			because: $"{dataValueTypeName} constants should become stable UTC DateTime values");
	}

	[TestCase("Date", "\"2025-01-15T13:45:30Z\"")]
	[TestCase("DateTime", "\"2025-01-15T13:45:30\"")]
	[TestCase("Time", "\"13:45:30\"")]
	[TestCase("DateTime", "5")]
	[TestCase("Time", "\"not-a-time\"")]
	[Category("Unit")]
	[Description("Rejects invalid temporal constants during normalization.")]
	public void TryConvertTemporalConstant_Should_Reject_Invalid_Temporal_Constants(
		string dataValueTypeName,
		string json) {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);

		// Act
		bool converted = BusinessRuleHelpers.TryConvertTemporalConstant(element, dataValueTypeName, out DateTime normalizedValue);

		// Assert
		converted.Should().BeFalse(
			because: "invalid temporal constants should be rejected before add-on metadata is generated");
		normalizedValue.Should().Be(default,
			because: "failed normalization should not return a partially parsed value");
	}

	[TestCase("5", 5L)]
	[TestCase("5.5", "5.5")]
	[Category("Unit")]
	[Description("Converts supported numeric constants into Int64 or Decimal values for business-rule metadata.")]
	public void TryConvertSupportedNumericConstant_Should_Convert_Supported_Numbers(
		string json,
		object expectedValue) {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);

		// Act
		bool converted = BusinessRuleHelpers.TryConvertSupportedNumericConstant(element, out object? numericValue);

		// Assert
		converted.Should().BeTrue(
			because: "business-rule numeric constants should accept values that can be represented as Int64 or Decimal");
		numericValue.Should().NotBeNull(
			because: "supported numeric constants should return a converted CLR value");
		if (expectedValue is long expectedInt64) {
			numericValue.Should().Be(expectedInt64,
				because: "integral numeric constants should stay integral after conversion");
		} else {
			numericValue.Should().Be(decimal.Parse((string)expectedValue),
				because: "fractional numeric constants should be represented as Decimal values");
		}
	}

	[TestCase("1e100")]
	[Category("Unit")]
	[Description("Rejects numeric constants that cannot be represented as Int64 or Decimal.")]
	public void TryConvertSupportedNumericConstant_Should_Reject_Out_Of_Range_Numbers(string json) {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);

		// Act
		bool converted = BusinessRuleHelpers.TryConvertSupportedNumericConstant(element, out object? numericValue);

		// Assert
		converted.Should().BeFalse(
			because: "values outside the supported numeric storage range should be rejected instead of stringified");
		numericValue.Should().BeNull(
			because: "failed numeric conversion should not return a fallback string or partial value");
	}

	[TestCase("1e100", "Money")]
	[Category("Unit")]
	[Description("Throws when the business-rule converter is asked to serialize unsupported numeric constants.")]
	public void ConvertJsonElement_Should_Reject_Unsupported_Numeric_Constants(
		string json,
		string dataValueTypeName) {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);

		// Act
		Action act = () => BusinessRuleHelpers.ConvertJsonElement(element, dataValueTypeName);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"Numeric constant '*' is not supported for data value type '{dataValueTypeName}'.",
				because: "business-rule numeric conversion should fail fast instead of stringifying unsupported values");
	}
}
