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

	private const int Body = 1;
	private const int Query = 3;

	private static ServiceParameterDto Parameter(string name, string type = "Text", bool isArray = false,
		params ServiceParameterDto[] items) {
		var parameter = new ServiceParameterDto {
			UId = Guid.NewGuid(), Name = name, Type = Body, Path = name, DataValueTypeName = type, IsArray = isArray,
			DefValue = new ServiceParameterValueDto()
		};
		foreach (ServiceParameterDto item in items) {
			parameter.ItemProperties.Items[item.UId.ToString()] = item;
		}
		return parameter;
	}

	/// <summary>
	/// A method with two scalars, an array of scalars, an array of objects whose items hold a scalar array and an inner
	/// array of objects, a single Object parameter Studio no longer creates, and a query parameter marked as an array.
	/// </summary>
	private static ServiceMethodDto Method() {
		ServiceParameterDto ids = Parameter("UsrIds", isArray: true);
		ids.Type = Query;
		return MethodWith(
			Parameter("UsrA"),
			Parameter("UsrB"),
			Parameter("UsrTags", isArray: true),
			Parameter("UsrLines", "Object", true,
				Parameter("UsrSku"), Parameter("UsrQty", "Integer"), Parameter("UsrNotes", isArray: true),
				Parameter("UsrSubs", "Object", true, Parameter("UsrNote"))),
			Parameter("UsrShape", "Object", false, Parameter("UsrX")),
			ids);
	}

	private static ServiceMethodDto MethodWith(params ServiceParameterDto[] parameters) {
		var method = new ServiceMethodDto { Name = "UsrOp" };
		foreach (ServiceParameterDto parameter in parameters) {
			method.Request.Parameters.Items[parameter.UId.ToString()] = parameter;
		}
		return method;
	}

	private static Dictionary<string, JsonElement> Values(string json) =>
		JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

	private static string Wire(IReadOnlyList<ServiceCallParameter> parameters) => JsonSerializer.Serialize(parameters);

	[Test]
	[Description("A scalar is sent as text, a number or boolean as its JSON text; null leaves a scalar and an array out, so the platform fills the scalar's default and sends no array.")]
	public void Build_Should_Send_Scalars_As_Text_And_Leave_Out_Null() {
		// Arrange
		Dictionary<string, JsonElement> values = Values("""{"UsrA": 1.5, "UsrB": null, "UsrTags": null}""");

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
		WebServiceTestCall empty = WebServiceTestValues.Build(Method(), Values("""{"UsrLines": []}"""));

		// Assert
		Wire(call.Parameters).Should().Be("""[{"code":"UsrLines","nested":[[{"code":"UsrSku","value":"S1"},"""
			+ """{"code":"UsrQty","value":"2"},{"code":"UsrNotes","value":["n1"]},"""
			+ """{"code":"UsrSubs","nested":[[{"code":"UsrNote","value":"deep"}]]}],[{"code":"UsrSku","value":"S2"}]]}]""",
			because: "the platform builds each item from its list of field values, recursively, and skips a field left out");
		call.SentCodes.Should().BeEquivalentTo(["UsrLines", "UsrSku", "UsrQty", "UsrNotes", "UsrSubs", "UsrNote"],
			because: "the masking needs every array the call filled, at any depth");
		Wire(empty.Parameters).Should().Be("""[{"code":"UsrLines","nested":[]}]""",
			because: "an empty array of objects goes out empty, which the platform renders as an empty array");
	}

	[Test]
	[Description("A path, query, header or cookie value is one text whatever its array flag says, because the platform writes it as text: a list goes as one text, and a JSON array is refused.")]
	public void Build_Should_Send_A_Value_Outside_The_Body_As_One_Text() {
		// Arrange
		Dictionary<string, JsonElement> text = Values("""{"UsrIds": "1,2"}""");
		Dictionary<string, JsonElement> array = Values("""{"UsrIds": ["1", "2"]}""");

		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), text);
		Action act = () => WebServiceTestValues.Build(Method(), array);

		// Assert
		Wire(call.Parameters).Should().Be("""[{"code":"UsrIds","value":"1,2"}]""",
			because: "the platform sends a query value as written, so a list goes as one text");
		act.Should().Throw<ArgumentException>(because: "the platform would send a JSON array as System.Object[]")
			.WithMessage("parameters.UsrIds: takes one text, number or boolean value, not an array (a path, query, header "
				+ "or cookie value is sent as written; give a list as one text, e.g. \"1,2\").",
				because: "the refusal says how to send a list there");
	}

	[Test]
	[Description("Every misshapen value and unknown code is refused in one message that names where it is: a scalar given an array, an array given a text or a single object, a bad array item, an unknown field, a value two arrays deep, a null array item and a single Object parameter.")]
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
			.Which.Message.Should().StartWith(
				"parameters: 'UsrZ' is not a top-level request parameter of method 'UsrOp'; valid codes: UsrA, UsrB, UsrIds, "
				+ "UsrLines, UsrShape, UsrTags.", "the unknown codes come first, with every valid code")
			.And.ContainAll(new[] {
				"parameters.UsrA: takes one text, number or boolean value, not an array.",
				"parameters.UsrTags: is an array of Text",
				"one item per character",
				"parameters.UsrShape: a single Object parameter cannot be given a value",
				"parameters.UsrLines[0]: 'UsrSkuu' is not a field of 'UsrLines' items; valid codes: UsrNotes, UsrQty, UsrSku, UsrSubs.",
				"parameters.UsrLines[0].UsrSubs[0].UsrNote: takes one text, number or boolean value, not an object.",
				"parameters.UsrLines[1]: an item of an array of objects is a JSON object by field code, not a text."
			}, "each problem is named with its location, so the agent fixes all of them in one round");
		actSingle.Should().Throw<ArgumentException>(because: "a single object is not an array of objects")
			.Which.Message.Should().ContainAll(new[] {
				"parameters.UsrLines: is an array of objects; send a JSON array of objects by field code, such as "
					+ "[{\"UsrNotes\": \"...\"}], not an object (wrap a single item in an array).",
				"use \"\" for an empty item"
			}, "the refusal says how to send the value instead");
	}

	[TestCase("""{"UsrLines": "S1"}""", "parameters.UsrLines: is an array of objects", "not a text.",
		Description = "a text for an array of objects")]
	[TestCase("""{"UsrLines": 5}""", "parameters.UsrLines: is an array of objects", "not a number.",
		Description = "a number for an array of objects")]
	[TestCase("""{"UsrTags": 5}""", "parameters.UsrTags: is an array of Text", "not a number.",
		Description = "a number for an array of scalars")]
	[TestCase("""{"UsrTags": {"a": 1}}""", "parameters.UsrTags: is an array of Text", "not an object.",
		Description = "an object for an array of scalars")]
	[TestCase("""{"UsrTags": [["a"]]}""", "parameters.UsrTags[0]: an item of an array of Text", "not an array.",
		Description = "a nested array in an array of scalars")]
	[Description("A value of the wrong kind for an array is refused with the kind it got, and only a text for a body array mentions the per-character split.")]
	public void Build_Should_Name_The_Kind_A_Misshapen_Array_Got(string json, string start, string end) {
		// Arrange
		Dictionary<string, JsonElement> values = Values(json);

		// Act
		Action act = () => WebServiceTestValues.Build(Method(), values);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the value cannot be sent as the parameter's shape")
			.Which.Message.Should().StartWith(start, because: "the refusal names the parameter")
			.And.EndWith(end, because: "the refusal names what was given")
			.And.NotContain("per character", because: "only a text for a body array is split into characters");
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
	[Description("No values build an empty call that reports no code as sent, and any value for a method without request parameters is refused with no valid code.")]
	public void Build_Should_Accept_No_Values_And_Refuse_Values_For_A_Method_Without_Parameters() {
		// Arrange
		ServiceMethodDto bare = MethodWith();

		// Act
		WebServiceTestCall call = WebServiceTestValues.Build(Method(), null);
		Action act = () => WebServiceTestValues.Build(bare, Values("""{"UsrA": "1"}"""));

		// Assert
		call.Parameters.Should().BeEmpty(because: "the platform fills every scalar from its default");
		call.SentCodes.Should().BeEmpty(because: "the call gave nothing a value");
		act.Should().Throw<ArgumentException>(because: "the method has nothing to fill")
			.WithMessage("*valid codes: none.", because: "the refusal says the method takes no value");
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
	[Description("More array items than a test call takes, counted at every depth, are refused before anything is checked or sent.")]
	public void Build_Should_Refuse_Too_Many_Array_Items() {
		// Arrange
		string notes = string.Join(",", Enumerable.Repeat("\"n\"", WebServiceTestValues.MaxArrayItems));
		Dictionary<string, JsonElement> values = Values($$"""{"UsrLines": [{"UsrNotes": [{{notes}}]}]}""");

		// Act
		Action act = () => WebServiceTestValues.Build(Method(), values);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the whole list would be posted to the platform and the external API")
			.WithMessage($"parameters: {WebServiceTestValues.MaxArrayItems + 1} array items in all; a test call takes at most "
				+ $"{WebServiceTestValues.MaxArrayItems}.", because: "the item and its notes are counted together");
	}
}
