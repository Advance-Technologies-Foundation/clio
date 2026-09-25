using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>Which caller-supplied payload a process write carries.</summary>
public enum ProcessWritePayload {
	/// <summary>A <c>create-business-process</c> descriptor: the BuildProcess request object.</summary>
	CreateDescriptor,

	/// <summary>The <c>operations</c> array of <c>modify-business-process</c> or <c>modify-business-process-as-new-version</c>.</summary>
	ModifyOperations
}

/// <summary>A key CrtProcessBuilder does not accept, found in a caller's payload.</summary>
/// <param name="Path">Where it sits, e.g. <c>flows[0].lable</c> or <c>operations[2].elementUpdate.email.subjct</c>.</param>
/// <param name="Hint">The nearest valid key, or the valid keys at that level when none is near.</param>
public sealed record UnknownDescriptorKey(string Path, string Hint);

/// <summary>
/// Finds every key in a process write payload that CrtProcessBuilder's contracts do not declare - the keys its
/// deserializer drops in silence while the call reports success.
/// </summary>
/// <remarks>
/// The key set is <c>Command/ProcessModel/Schemas/process-builder-write-keys.schema.json</c>, a JSON Schema
/// generated from the <c>[DataContract]</c> sources inside the CrtProcessBuilder archive clio bundles and pinned
/// to them by <c>ProcessBuilderWriteKeySchemaTests</c>. See <c>spec/adr/adr-descriptor-strict-keys.md</c>.
/// Matching is CASE-SENSITIVE, because the server's is: <c>Label</c> is dropped exactly as <c>lable</c> is.
/// Values are not checked - a wrong value type is refused loudly by the server already.
/// </remarks>
public interface IProcessDescriptorKeyValidator {

	/// <summary>Walks the payload and returns every unknown key, in document order. Never throws.</summary>
	/// <param name="payload">The parsed descriptor object or operations array, exactly as it will be posted.</param>
	/// <param name="kind">Which payload it is.</param>
	/// <returns>The unknown keys; empty when every key is one the server accepts.</returns>
	IReadOnlyList<UnknownDescriptorKey> FindUnknownKeys(JsonNode payload, ProcessWritePayload kind);
}

/// <inheritdoc cref="IProcessDescriptorKeyValidator" />
public sealed class ProcessDescriptorKeyValidator : IProcessDescriptorKeyValidator {

	/// <summary>The embedded schema's manifest resource name.</summary>
	internal const string SchemaResourceName = "Clio.Command.ProcessModel.Schemas.process-builder-write-keys.schema.json";

	private const string DefinitionPrefix = "#/$defs/";

	// Parsed once per process: the schema is part of the assembly and never changes at run time.
	private static readonly Lazy<WriteKeySchema> Schema = new(() => WriteKeySchema.Parse(ReadSchemaText()));

	/// <inheritdoc />
	public IReadOnlyList<UnknownDescriptorKey> FindUnknownKeys(JsonNode payload, ProcessWritePayload kind) {
		List<UnknownDescriptorKey> unknown = [];
		WriteKeySchema schema = Schema.Value;
		if (kind == ProcessWritePayload.CreateDescriptor) {
			Walk(payload, schema.CreateDescriptorRoot, string.Empty, schema, unknown);
			return unknown;
		}
		if (payload is JsonArray operations) {
			for (int index = 0; index < operations.Count; index++) {
				Walk(operations[index], schema.ModifyOperationRoot, $"operations[{index}]", schema, unknown);
			}
		}
		return unknown;
	}

	/// <summary>The schema text shipped in this assembly. Exposed for the drift test.</summary>
	internal static string ReadSchemaText() {
		using Stream stream = typeof(ProcessDescriptorKeyValidator).Assembly.GetManifestResourceStream(SchemaResourceName)
			?? throw new InvalidOperationException($"Embedded resource '{SchemaResourceName}' is missing from clio.");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}

	// A value that is not an object where a contract is declared is left alone: that is a TYPE mistake, which
	// the server's deserializer refuses loudly, and inventing a key finding for it would describe the wrong
	// problem. JSON null is legal for every contract member.
	private static void Walk(JsonNode node, string contract, string path, WriteKeySchema schema,
			List<UnknownDescriptorKey> unknown) {
		if (node is not JsonObject obj || !schema.Contracts.TryGetValue(contract, out WriteContract declared)) {
			return;
		}
		foreach ((string key, JsonNode value) in obj) {
			string keyPath = path.Length == 0 ? key : $"{path}.{key}";
			if (!declared.Members.TryGetValue(key, out WriteMember member)) {
				unknown.Add(new UnknownDescriptorKey(keyPath, Hint(key, declared)));
				continue;
			}
			if (member.Contract is null) {
				continue;
			}
			if (!member.IsList) {
				Walk(value, member.Contract, keyPath, schema, unknown);
				continue;
			}
			if (value is JsonArray items) {
				for (int index = 0; index < items.Count; index++) {
					Walk(items[index], member.Contract, $"{keyPath}[{index}]", schema, unknown);
				}
			}
		}
	}

	// The hint the refusal carries. A key that differs from a valid one ONLY in case is the commonest mistake
	// and the one a reader least suspects (the server matches case-sensitively; most JSON tooling does not),
	// so it is named first and says why. Then the closest key by edit distance, bounded so a hint is never a
	// guess; else the valid keys at that level, which is what a caller needs to pick the right one.
	private static string Hint(string key, WriteContract declared) {
		string sameLetters = declared.Members.Keys
			.FirstOrDefault(valid => string.Equals(valid, key, StringComparison.OrdinalIgnoreCase));
		if (sameLetters is not null) {
			return $"did you mean '{sameLetters}'? Keys are case-sensitive";
		}
		int bound = key.Length <= 4 ? 1 : 2;
		string nearest = declared.Members.Keys
			.Select(valid => (Key: valid, Distance: EditDistance(key.ToLowerInvariant(), valid.ToLowerInvariant())))
			.Where(candidate => candidate.Distance <= bound)
			.OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
			.Select(candidate => candidate.Key)
			.FirstOrDefault();
		return nearest is not null
			? $"did you mean '{nearest}'?"
			: $"valid keys here: {string.Join(", ", declared.Members.Keys.OrderBy(valid => valid, StringComparer.Ordinal))}";
	}

	// Optimal string alignment distance: Levenshtein plus an adjacent transposition as ONE edit, so 'lable'
	// is one step from 'label' rather than two - the typo that measured the silent drop in the first place.
	private static int EditDistance(string left, string right) {
		int[,] distance = new int[left.Length + 1, right.Length + 1];
		for (int i = 0; i <= left.Length; i++) {
			distance[i, 0] = i;
		}
		for (int j = 0; j <= right.Length; j++) {
			distance[0, j] = j;
		}
		for (int i = 1; i <= left.Length; i++) {
			for (int j = 1; j <= right.Length; j++) {
				int cost = left[i - 1] == right[j - 1] ? 0 : 1;
				distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
					distance[i - 1, j - 1] + cost);
				if (i > 1 && j > 1 && left[i - 1] == right[j - 2] && left[i - 2] == right[j - 1]) {
					distance[i, j] = Math.Min(distance[i, j], distance[i - 2, j - 2] + 1);
				}
			}
		}
		return distance[left.Length, right.Length];
	}

	private sealed record WriteMember(string Contract, bool IsList);

	private sealed record WriteContract(IReadOnlyDictionary<string, WriteMember> Members);

	// Reads the subset of JSON Schema the generator emits and nothing more: `$defs` of objects with
	// `properties`, each property `{}` (a scalar), `{"$ref"}` (a contract) or `{"items":{"$ref"}}` (a list of
	// one). The file IS a standard JSON Schema - the tests evaluate it with a real validator - but interpreting
	// the general language here would buy nothing the walk needs.
	private sealed record WriteKeySchema(IReadOnlyDictionary<string, WriteContract> Contracts,
			string CreateDescriptorRoot, string ModifyOperationRoot) {

		internal static WriteKeySchema Parse(string text) {
			JsonObject document = JsonNode.Parse(text)?.AsObject()
				?? throw new InvalidOperationException("The process-builder key schema is empty.");
			Dictionary<string, WriteContract> contracts = new(StringComparer.Ordinal);
			foreach ((string name, JsonNode definition) in document["$defs"]!.AsObject()) {
				Dictionary<string, WriteMember> members = new(StringComparer.Ordinal);
				foreach ((string key, JsonNode property) in definition!["properties"]!.AsObject()) {
					string itemReference = property?["items"]?["$ref"]?.GetValue<string>();
					string reference = itemReference ?? property?["$ref"]?.GetValue<string>();
					members[key] = new WriteMember(StripPrefix(reference), itemReference is not null);
				}
				contracts[name] = new WriteContract(members);
			}
			return new WriteKeySchema(contracts, StripPrefix(document["$ref"]!.GetValue<string>()),
				StripPrefix(document["x-modifyOperation"]!.GetValue<string>()));
		}

		private static string StripPrefix(string reference) =>
			reference is null ? null
			: reference.StartsWith(DefinitionPrefix, StringComparison.Ordinal) ? reference[DefinitionPrefix.Length..]
			: throw new InvalidOperationException($"Unsupported reference '{reference}' in the process-builder key schema.");
	}
}
