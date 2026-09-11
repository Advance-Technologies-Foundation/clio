using System;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Wire-format regressions for <see cref="DataBindingValueConverter"/>.
/// </summary>
/// <remarks>
/// These live in the Command module, next to the converter itself, so a converter-only change cannot pass
/// the prescribed Command test filter without running them. The type-map and localization assertions that
/// belong to <c>DataValueTypeMap</c> stay in the ProcessModel fixture.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
[Category("Unit")]
internal sealed class DataBindingValueConverterTests {
	private const int ColorRuntimeDataValueType = 18;
	private const string ColorColumnName = "UsrColor";

	private static DataBindingValueConverter CreateConverter() => new(Substitute.For<IFileSystem>());

	[Test]
	[Description("A Color value still crosses the data-binding wire as its hex literal, even though its CLR type is System.Drawing.Color.")]
	public void ConvertValue_Should_Pass_Through_The_Hex_Literal_For_A_Color_Column() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(ColorRuntimeDataValueType);
		DataBindingValueConverter converter = CreateConverter();
		JsonNode valueNode = JsonValue.Create("#FF6900");

		// Act
		object converted = converter.ConvertValue(valueNode, colorUId, ColorColumnName, allowEmptyString: false);

		// Assert
		converted.Should().Be("#FF6900",
			because: "the binding row carries the literal verbatim; keeping the native CLR mapping must not "
				+ "break the wire format");
	}

	[Test]
	[Description("A numeric wire value for a Color column is rejected instead of being stringified into an invalid Color.")]
	public void ConvertValue_Should_Reject_A_Numeric_Value_For_A_Color_Column() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(ColorRuntimeDataValueType);
		DataBindingValueConverter converter = CreateConverter();
		JsonNode valueNode = JsonValue.Create(123);

		// Act
		Action act = () => converter.ConvertValue(valueNode, colorUId, ColorColumnName, allowEmptyString: false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a valid Color arrives as a hex string; turning 123 into \"123\" would ship an invalid "
				+ "Color to the platform instead of failing type validation")
			.WithMessage($"*{ColorColumnName}*");
	}

	[Test]
	[Description("An object wire value for a Color column is rejected instead of being serialized as JSON text.")]
	public void ConvertValue_Should_Reject_An_Object_Value_For_A_Color_Column() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(ColorRuntimeDataValueType);
		DataBindingValueConverter converter = CreateConverter();
		JsonNode valueNode = new JsonObject {
			["r"] = 0,
			["g"] = 157,
			["b"] = 227
		};

		// Act
		Action act = () => converter.ConvertValue(valueNode, colorUId, ColorColumnName, allowEmptyString: false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "serializing the object would send {\"r\":0,\"g\":157,\"b\":227} as the Color wire value; "
				+ "the type-validation error is the correct outcome")
			.WithMessage($"*{ColorColumnName}*");
	}
}
