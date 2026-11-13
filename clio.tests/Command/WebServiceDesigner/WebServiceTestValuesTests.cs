using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceTestValuesTests {

	private static ServiceParameterDto Parameter(string name, string type = "Text", bool isArray = false,
		params ServiceParameterDto[] items) {
		var parameter = new ServiceParameterDto {
			UId = Guid.NewGuid(), Name = name, Type = 1, Path = name, DataValueTypeName = type, IsArray = isArray,
			DefValue = new ServiceParameterValueDto()
		};
		foreach (ServiceParameterDto item in items) {
			parameter.ItemProperties.Items[item.UId.ToString()] = item;
		}
		return parameter;
	}

	/// <summary>
	/// A method with a scalar, an array of scalars, an array of objects whose items hold a scalar array and an inner
	/// array of objects, and a single Object parameter Studio no longer creates.
	/// </summary>
	private static ServiceMethodDto Method() {
		var method = new ServiceMethodDto { Name = "UsrOp" };
		foreach (ServiceParameterDto parameter in new[] {
			Parameter("UsrA"),
			Parameter("UsrTags", isArray: true),
			Parameter("UsrLines", "Object", true,
				Parameter("UsrSku"), Parameter("UsrQty", "Integer"), Parameter("UsrNotes", isArray: true),
				Parameter("UsrSubs", "Object", true, Parameter("UsrNote"))),
			Parameter("UsrShape", "Object", false, Parameter("UsrX"))
		}) {
			method.Request.Parameters.Items[parameter.UId.ToString()] = parameter;
		}
		return method;
	}

	private static Dictionary<string, JsonElement> Values(string json) =>
		JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

	private static string Wire(IReadOnlyList<ServiceCallParameter> parameters) => JsonSerializer.Serialize(parameters);

	[Test]
	[Description("Scalar values are sent as text: a number or boolean as its JSON text, and null is left out so the platform fills the default.")]
	public void Build_Should_Send_Scalars_As_Text_And_Leave_Out_Null() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""{"UsrA": 1.5, "UsrTags": null}""");

		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), values);

		// Assert
		Wire(call.Parameters).Should().Be("""[{"code":"UsrA","value":"1.5"}]""",
			because: "the platform's test call takes text, and a scalar carries no nested member");
		call.SentCodes.Should().BeEquivalentTo(["UsrA"], because: "a null value sends nothing");
	}

	[Test]
	[Description("An array of scalars is sent as a JSON array of texts in value, an empty array included, as Studio's test request sends it.")]
	public void Build_Should_Send_A_Scalar_Array_In_Value() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""{"UsrTags": ["red", 2, true]}""");

		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), values);
		WebServiceTestCall empty = WebServiceTestValues.Build(Method(), Values("""{"UsrTags": []}"""));

		// Assert
		Wire(call.Parameters).Should().Be("""[{"code":"UsrTags","value":["red","2","true"]}]""",
			because: "the platform renders one item per element of value and converts each text to the item type");
		Wire(empty.Parameters).Should().Be("""[{"code":"UsrTags","value":[]}]""",
			because: "an empty array is sent as an empty array, not left out");
	}

	[Test]
	[Description("An array of objects is sent in nested, one list of field values per item, with a scalar array and an inner array of objects inside an item, and every code given a value is reported.")]
	public void Build_Should_Send_An_Object_Array_In_Nested() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""
			{"UsrLines": [
				{"UsrSku": "S1", "UsrQty": 2, "UsrNotes": ["n1"], "UsrSubs": [{"UsrNote": "deep"}]},
				{"UsrSku": "S2", "UsrQty": null}
			]}
			""");

		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), values);

		// Assert
		Wire(call.Parameters).Should().Be("""[{"code":"UsrLines","nested":[[{"code":"UsrSku","value":"S1"},"""
			+ """{"code":"UsrQty","value":"2"},{"code":"UsrNotes","value":["n1"]},"""
			+ """{"code":"UsrSubs","nested":[[{"code":"UsrNote","value":"deep"}]]}],[{"code":"UsrSku","value":"S2"}]]}]""",
			because: "the platform builds each item from its list of field values, recursively, and skips a field left out");
		call.SentCodes.Should().BeEquivalentTo(["UsrLines", "UsrSku", "UsrQty", "UsrNotes", "UsrSubs", "UsrNote"],
			because: "the masking needs every array the call filled, at any depth");
	}

	[Test]
	[Description("Every misshapen value and unknown code is refused in one message that names where it is: a scalar given an array, an array given a text or a single object, a bad array item, an unknown or repeated field, and a single Object parameter.")]
	public void Build_Should_Refuse_Misshapen_Values_At_Every_Level() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""
			{"UsrA": [1], "UsrTags": "ab", "UsrShape": {"UsrX": "1"}, "UsrZ": "1",
			 "UsrLines": [{"UsrSku": "S1", "UsrSkuu": "x", "UsrSubs": [{"UsrNote": {}}]}, "S2"]}
			""");
		Dictionary<string, JsonElement> single = Values("""{"UsrLines": {"UsrSku": "S1"}, "UsrTags": ["a", null]}""");

		// Act
		Action act = () => WebServiceTestValues.Build(Method(), values);
		Action actSingle = () => WebServiceTestValues.Build(Method(), single);

		// Assert
		act.Should().Throw<ArgumentException>(because: "nothing is sent while any value is wrong")
			.Which.Message.Should().ContainAll(new[] {
				"parameters: 'UsrZ' is not a top-level request parameter of method 'UsrOp'; valid codes: UsrA, UsrLines, UsrShape, UsrTags.",
				"parameters.UsrA: takes one text, number or boolean value, not an array.",
				"parameters.UsrTags: is an array of Text",
				"one item per character",
				"parameters.UsrShape: a single Object parameter cannot be given a value",
				"parameters.UsrLines[0]: 'UsrSkuu' is not a field of 'UsrLines' items; valid codes: UsrNotes, UsrQty, UsrSku, UsrSubs.",
				"parameters.UsrLines[0].UsrSubs[0].UsrNote: takes one text, number or boolean value, not an object.",
				"parameters.UsrLines[1]: an item of an array of objects is a JSON object by field code, not a text."
			}, "each problem is named with its location, so the agent fixes all of them in one round");
		actSingle.Should().Throw<ArgumentException>(because: "a single object is not an array of objects")
			.Which.Message.Should().ContainAll(new[] { "wrap a single item in an array", "use \"\" for an empty item" },
				"the refusal says how to send the value instead");
	}

	[Test]
	[Description("A field given twice in one item is refused, because the platform fails the whole call on a repeated code.")]
	public void Build_Should_Refuse_A_Field_Given_Twice() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""{"UsrLines": [{"UsrSku": "S1", "UsrSku": "S2"}]}""");

		// Act
		Action act = () => WebServiceTestValues.Build(Method(), values);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform answers a repeated code with an error and sends nothing")
			.WithMessage("*parameters.UsrLines[0]: 'UsrSku' is given twice*", because: "the refusal names the field");
	}

	[Test]
	[Description("No values build an empty call that reports no code as sent.")]
	public void Build_Should_Accept_No_Values() {
		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), null);

		// Assert
		call.Parameters.Should().BeEmpty(because: "the platform fills every parameter from its default");
		call.SentCodes.Should().BeEmpty(because: "the call gave nothing a value");
	}

	[Test]
	[Description("A code is matched case-sensitively, as the platform looks it up, so a code in another case is refused as unknown.")]
	public void Build_Should_Match_Codes_Case_Sensitively() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""{"UsrLines": [{"usrsku": "S1"}]}""");

		// Act
		Action act = () => WebServiceTestValues.Build(Method(), values);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform would drop a field in another case silently")
			.WithMessage("*'usrsku' is not a field of 'UsrLines' items*", because: "the refusal names the field as given");
	}

	[Test]
	[Description("A call sends one parameter per given top-level code, in the order given.")]
	public void Build_Should_Keep_The_Given_Order() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""{"UsrTags": ["x"], "UsrA": "a"}""");

		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), values);

		// Assert
		call.Parameters.Select(p => p.Code).Should().Equal(["UsrTags", "UsrA"],
			because: "the order of the request does not change what the platform builds, so it is kept as given");
	}
}
