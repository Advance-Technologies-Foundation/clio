using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Hermetic coverage for the per-detail typeDefinitions closure filter.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class TypeReferenceClosureTests {
	[Test]
	[Description("Closure walk pulls types named in inputs/outputs through `type` strings and recurses through their fields — transitive depth ≥ 2 is honoured.")]
	public void Resolve_Walks_Transitive_Closure_From_Inputs() {
		IReadOnlyDictionary<string, JsonElement> inputs = ParseDict("""
		{
		  "primary": { "type": "TopLevel" }
		}
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{
		  "TopLevel":   { "fields": { "inner": { "type": "Middle" } } },
		  "Middle":     { "fields": { "leaf":  { "type": "Leaf" } } },
		  "Leaf":       { "type": "string" },
		  "Unrelated":  { "type": "string" }
		}
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: inputs, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: global);

		resolved.Should().NotBeNull();
		resolved!.Should().ContainKey("TopLevel", because: "the BFS starts from the input's `type` string");
		resolved.Should().ContainKey("Middle", because: "TopLevel.fields.inner.type pulls Middle in (depth 1)");
		resolved.Should().ContainKey("Leaf", because: "Middle.fields.leaf.type pulls Leaf in (depth 2)");
		resolved.Should().NotContainKey("Unrelated",
			because: "globals that no transitively reachable type names must be filtered out");
	}

	[Test]
	[Description("Per-component typeDefinitions are surfaced verbatim even when no input/output references them — the producer already curated the per-component bag.")]
	public void Resolve_Surfaces_Per_Component_Typedefs_Verbatim() {
		IReadOnlyDictionary<string, JsonElement> perComponent = ParseDict("""
		{ "Curated": { "type": "string", "values": ["a", "b"] } }
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: null, outputs: null,
			perComponentTypeDefinitions: perComponent, globalTypeDefinitions: null);

		resolved.Should().NotBeNull();
		resolved!.Should().ContainKey("Curated");
	}

	[Test]
	[Description("A per-component typedef can reference a global type — closure must pull the global through.")]
	public void Resolve_Follows_References_From_Per_Component_Into_Global() {
		IReadOnlyDictionary<string, JsonElement> perComponent = ParseDict("""
		{ "Local": { "fields": { "child": { "type": "GlobalDep" } } } }
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{
		  "GlobalDep": { "type": "string" },
		  "Unrelated": { "type": "string" }
		}
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: null, outputs: null,
			perComponentTypeDefinitions: perComponent, globalTypeDefinitions: global);

		resolved.Should().NotBeNull();
		resolved!.Should().ContainKey("Local");
		resolved.Should().ContainKey("GlobalDep");
		resolved.Should().NotContainKey("Unrelated");
	}

	[Test]
	[Description("Tokens that resolve to neither bag are built-ins (string, Record, Promise, …) and must be silently skipped, not crash.")]
	public void Resolve_Skips_Built_In_Identifiers() {
		IReadOnlyDictionary<string, JsonElement> inputs = ParseDict("""
		{
		  "data":     { "type": "Record" },
		  "ready":    { "type": "Promise<boolean>" },
		  "explicit": { "type": "Known" }
		}
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{ "Known": { "type": "string" } }
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: inputs, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: global);

		resolved.Should().NotBeNull();
		resolved!.Should().ContainKey("Known");
		resolved.Keys.Should().HaveCount(1,
			because: "Record/Promise/boolean have no definition in the registry and must be silently dropped");
	}

	[Test]
	[Description("Union type strings like 'string | ButtonIcon | ButtonAnimatedIcon' must contribute every PascalCase identifier — AI sees every alternative resolved.")]
	public void Resolve_Tokenises_Union_Type_Strings() {
		IReadOnlyDictionary<string, JsonElement> inputs = ParseDict("""
		{ "icon": { "type": "string | ButtonIcon | ButtonAnimatedIcon" } }
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{
		  "ButtonIcon":         { "type": "string", "values": ["close-icon"] },
		  "ButtonAnimatedIcon": { "fields": { "name": { "type": "string" } } }
		}
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: inputs, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: global);

		resolved!.Should().ContainKey("ButtonIcon");
		resolved.Should().ContainKey("ButtonAnimatedIcon");
	}

	[Test]
	[Description("Record-shaped schemas reference their key/value types through `keyType`/`valueType` strings, not `type` — the closure must follow them, else a Record-valued input names types it never defines.")]
	public void Resolve_Follows_KeyType_And_ValueType_References() {
		IReadOnlyDictionary<string, JsonElement> inputs = ParseDict("""
		{
		  "parameters": { "type": "Record", "keyType": "BrandedKey", "valueType": "string | Payload | PayloadItem[]" }
		}
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{
		  "BrandedKey":  { "type": "string" },
		  "Payload":     { "fields": { "inner": { "type": "Nested" } } },
		  "PayloadItem": { "type": "string" },
		  "Nested":      { "type": "string" },
		  "Unrelated":   { "type": "string" }
		}
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: inputs, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: global);

		resolved.Should().NotBeNull();
		resolved!.Should().ContainKey("BrandedKey", because: "a `keyType` string is a type reference, not payload");
		resolved.Should().ContainKey("Payload", because: "every PascalCase member of a `valueType` union is a type reference");
		resolved.Should().ContainKey("PayloadItem", because: "an array-suffixed `valueType` member must tokenise to its element type");
		resolved.Should().ContainKey("Nested", because: "types reached via `valueType` contribute their own inner references transitively");
		resolved.Should().NotContainKey("Unrelated",
			because: "following keyType/valueType must not weaken the closure filter");
	}

	[Test]
	[Description("A typedef whose field references another type only through `valueType` (the RequestBindingConfig.params shape) must still pull that type in — the wiring chain is broken at exactly this hop otherwise.")]
	public void Resolve_Follows_ValueType_References_Inside_Typedefs() {
		IReadOnlyDictionary<string, JsonElement> inputs = ParseDict("""
		{ "binding": { "type": "Wiring" } }
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{
		  "Wiring":      { "fields": { "params": { "type": "Record", "keyType": "string", "valueType": "WiringValue" } } },
		  "WiringValue": { "type": "string" },
		  "Unrelated":   { "type": "string" }
		}
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: inputs, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: global);

		resolved.Should().NotBeNull();
		resolved!.Should().ContainKey("Wiring");
		resolved.Should().ContainKey("WiringValue",
			because: "the only reference to WiringValue lives in a `valueType` string inside the Wiring typedef");
		resolved.Should().NotContainKey("Unrelated");
	}

	[Test]
	[Description("`values` payload arrays must not be misread as type names — 'close-icon' is a literal allowed value, not a type identifier.")]
	public void Resolve_Does_Not_Tokenise_Values_Payload() {
		IReadOnlyDictionary<string, JsonElement> inputs = ParseDict("""
		{ "icon": { "type": "string", "values": ["CloseIcon", "EditIcon"] } }
		""");
		IReadOnlyDictionary<string, JsonElement> global = ParseDict("""
		{
		  "CloseIcon": { "type": "string" },
		  "EditIcon":  { "type": "string" }
		}
		""");

		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: inputs, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: global);

		// 'values' carries allowed strings, not type refs. Even though the strings
		// happen to be PascalCase, the closure must not pull them in.
		resolved.Should().BeNull(
			because: "no `type` string referenced a producer-defined type — the response must drop the typeDefinitions block entirely");
	}

	[Test]
	[Description("Both bags empty → null (so JsonIgnore strips the block from the wire shape).")]
	public void Resolve_Returns_Null_When_Both_Bags_Empty() {
		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			inputs: null, outputs: null,
			perComponentTypeDefinitions: null, globalTypeDefinitions: null);

		resolved.Should().BeNull();
	}

	private static IReadOnlyDictionary<string, JsonElement> ParseDict(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		Dictionary<string, JsonElement> result = new(System.StringComparer.Ordinal);
		foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
			result[property.Name] = property.Value.Clone();
		}
		return result;
	}
}
