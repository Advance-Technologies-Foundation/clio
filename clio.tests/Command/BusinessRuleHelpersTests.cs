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
}
