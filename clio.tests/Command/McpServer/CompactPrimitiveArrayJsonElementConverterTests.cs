using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Coverage for the indented-but-arrays-inline JsonElement renderer.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CompactPrimitiveArrayJsonElementConverterTests {
	private static readonly JsonSerializerOptions IndentedWithConverter = new() {
		WriteIndented = true,
		Converters = { new CompactPrimitiveArrayJsonElementConverter() }
	};

	[Test]
	[Description("Arrays of strings render on a single line inside an otherwise indented document — `values: [\"none\",\"default\"]` instead of one-element-per-line.")]
	public void Primitive_String_Array_Renders_On_One_Line() {
		JsonElement element = JsonDocument.Parse(
			"""{ "iconSize": { "type": "string", "default": "large", "values": ["none", "default", "extra-small"] } }""")
			.RootElement;

		string output = JsonSerializer.Serialize(element, IndentedWithConverter);

		output.Should().Contain("\"values\": [\"none\",\"default\",\"extra-small\"]",
			because: "primitive arrays must collapse onto one line — that is the whole point of this converter");
		output.Should().NotContain("\n    \"none\"",
			because: "the indented writer must not split primitive array elements across lines");
		// And the surrounding object remains indented.
		output.Should().Contain("\n  \"iconSize\":",
			because: "the outer object must keep the indented layout — only the primitive array collapses");
	}

	[Test]
	[Description("Number / bool / null elements count as primitives — the array still collapses.")]
	public void Mixed_Primitive_Array_Renders_On_One_Line() {
		JsonElement element = JsonDocument.Parse(
			"""{ "values": [1, 2.5, true, false, null] }""")
			.RootElement;

		string output = JsonSerializer.Serialize(element, IndentedWithConverter);

		output.Should().Contain("[1,2.5,true,false,null]",
			because: "every JsonValueKind that is not Object or Array is primitive enough to collapse");
	}

	[Test]
	[Description("Arrays that contain a nested object stay multi-line — collapsing would lose readability for structured items.")]
	public void Array_With_Objects_Stays_Multi_Line() {
		JsonElement element = JsonDocument.Parse(
			"""{ "items": [ { "name": "a" }, { "name": "b" } ] }""")
			.RootElement;

		string output = JsonSerializer.Serialize(element, IndentedWithConverter);

		output.Should().Contain("\"items\": [\n",
			because: "an array of objects must keep the indented per-item layout — only primitive bags collapse");
	}

	[Test]
	[Description("Nested objects keep their indentation — the converter only special-cases arrays, not object bodies.")]
	public void Nested_Objects_Stay_Indented() {
		JsonElement element = JsonDocument.Parse(
			"""{ "outer": { "inner": { "value": 1 } } }""")
			.RootElement;

		string output = JsonSerializer.Serialize(element, IndentedWithConverter);

		output.Should().Contain("\"outer\": {\n");
		output.Should().Contain("\"inner\": {\n");
	}

	[Test]
	[Description("Empty primitive array also collapses to `[]` (no newline inside) — degenerate but cheap to handle.")]
	public void Empty_Array_Renders_As_Brackets() {
		JsonElement element = JsonDocument.Parse("""{ "values": [] }""").RootElement;

		string output = JsonSerializer.Serialize(element, IndentedWithConverter);

		output.Should().Contain("\"values\": []",
			because: "an empty array is trivially primitive-only — no need to multi-line a pair of brackets");
	}

	[Test]
	[Description("Producer-supplied whitespace inside the primitive array is normalised away — `[\"a\", \"b\"]` becomes `[\"a\",\"b\"]`.")]
	public void Producer_Whitespace_In_Primitive_Array_Is_Normalised() {
		JsonElement element = JsonDocument.Parse(
			"""{ "values": ["a",   "b",      "c"] }""")
			.RootElement;

		string output = JsonSerializer.Serialize(element, IndentedWithConverter);

		output.Should().Contain("[\"a\",\"b\",\"c\"]",
			because: "re-serialisation drops the producer's intra-array spaces — predictable, diff-friendly output");
	}
}
