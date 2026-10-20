using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Single owner of the unknown-field rule for MCP column payloads: the operations of
/// <c>update-entity-schema</c>, the columns of <c>create-entity-schema</c>, the arguments of
/// <c>modify-entity-schema-column</c>, and the same payloads embedded in <c>sync-schemas</c>.
/// </summary>
/// <remarks>
/// MCP binding silently discards a JSON field that matches no <see cref="JsonPropertyNameAttribute"/>, so a typo
/// such as <c>requird</c> was saved as a column without the flag. The records capture those fields in a
/// <see cref="JsonExtensionDataAttribute"/> bag and every caller rejects a non-empty bag through this class
/// before anything is sent to Creatio. The wording matches the CLI <c>update-entity-schema</c> refusal
/// (ENG-101526) so both surfaces name the field and the nearest known field the same way.
/// </remarks>
internal static class ColumnPayloadFieldContract {

	private static readonly ConcurrentDictionary<Type, string[]> KnownFieldsByType = new();

	/// <summary>
	/// Returns the refusal for the first payload that carries an unknown field, or <see langword="null"/> when
	/// none does.
	/// </summary>
	/// <typeparam name="TRecord">The record the payloads bind to; its JSON field names are the suggestion candidates.</typeparam>
	/// <param name="payloads">The bound payloads, in the order the caller sent them; <see langword="null"/> means none.</param>
	/// <param name="extensionData">Selects a payload's overflow bag.</param>
	/// <param name="label">Builds the caller-facing name of the payload at an index, e.g. <c>Operation payload at index 1</c>.</param>
	/// <param name="suggestionService">Finds the nearest known field for the hint.</param>
	internal static string? FindUnknownFieldError<TRecord>(
		IEnumerable<TRecord>? payloads,
		Func<TRecord, IReadOnlyDictionary<string, JsonElement>?> extensionData,
		Func<int, string> label,
		IOptionSuggestionService suggestionService) {
		int index = 0;
		foreach (TRecord payload in payloads ?? []) {
			string? unknownField = payload is null ? null : extensionData(payload)?.Keys.FirstOrDefault();
			if (unknownField is not null) {
				string suggestion = suggestionService.SuggestName(unknownField, GetKnownFields(typeof(TRecord)));
				return UpdateEntitySchemaCommand.FormatUnknownFieldError(label(index), unknownField, suggestion);
			}
			index++;
		}
		return null;
	}

	/// <summary>
	/// Returns the JSON field names <paramref name="recordType"/> binds, inherited ones included.
	/// </summary>
	internal static IReadOnlyList<string> GetKnownFields(Type recordType) =>
		KnownFieldsByType.GetOrAdd(recordType, static type => type
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
			.Where(name => !string.IsNullOrEmpty(name))
			.Select(name => name!)
			.Distinct(StringComparer.Ordinal)
			.ToArray());
}
