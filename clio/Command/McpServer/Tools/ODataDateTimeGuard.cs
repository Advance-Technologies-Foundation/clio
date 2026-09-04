using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Value-level guard for the OData write tools (<see cref="ODataUpdateTool"/>,
/// <see cref="ODataCreateTool"/>): it refuses a date-time literal that carries no UTC designator
/// and no time-zone offset, before the request leaves the process.
/// <para>
/// Creatio exposes every temporal column as <c>Edm.DateTimeOffset</c>, whose literal form requires a
/// zone. What the platform does with a zone-less literal is build-dependent and never safe: the stand
/// this was reproduced on (Creatio 10.1.725, .NET Framework) rejects the whole request with the opaque
/// text "The request is invalid.", while the build reported in GitHub issue #1369 accepted the request,
/// answered <c>success:true</c> and stored <c>DateTime.MinValue</c> - a silent partial data loss on an
/// otherwise successful write.
/// </para>
/// <para>
/// clio does NOT repair the literal by appending <c>Z</c>: the caller's intended zone is unknown, so an
/// assumed UTC would replace a visible failure with an invisible, plausible-looking wrong instant. The
/// call is rejected instead and the caller re-sends one explicit value.
/// </para>
/// </summary>
internal static class ODataDateTimeGuard {

	/// <summary>
	/// An ISO-8601 date-time with a time component and NO zone suffix - the exact shape the platform
	/// mishandles. Anchored on purpose: a value that already ends in <c>Z</c> or <c>+hh:mm</c>/<c>-hh:mm</c>
	/// does not match, and neither does a date-only <c>2024-01-01</c> (see the remarks on
	/// <see cref="FindZoneLessDateTime(JsonElement, IReadOnlyDictionary{string, string})"/>).
	/// </summary>
	private static readonly Regex ZoneLessDateTimePattern = new(
		@"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?$",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	/// <summary>The Edm types Creatio uses for temporal columns.</summary>
	private static readonly HashSet<string> TemporalEdmTypes = new(StringComparer.Ordinal) {
		"Edm.DateTimeOffset", "Edm.DateTime", "Edm.Date"
	};

	/// <summary>
	/// True when the payload carries at least one top-level string value shaped like a zone-less
	/// date-time. It lets a caller decide whether the entity's Edm types are worth fetching at all: the
	/// service-root CSDL is a multi-megabyte document (about 3 MB on a stock Creatio 10.1), and a payload
	/// with no date-shaped value can never be refused by
	/// <see cref="FindZoneLessDateTime(JsonElement, IReadOnlyDictionary{string, string})"/>, whatever the
	/// types say.
	/// </summary>
	/// <param name="payload">The data/row object as supplied by the caller.</param>
	internal static bool HasZoneLessCandidate(JsonElement payload) {
		if (payload.ValueKind != JsonValueKind.Object) {
			return false;
		}
		foreach (JsonProperty property in payload.EnumerateObject()) {
			if (property.Value.ValueKind == JsonValueKind.String
				&& property.Value.GetString() is { Length: > 0 } value
				&& ZoneLessDateTimePattern.IsMatch(value)) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Inspects the top-level string values of one write payload and returns the caller-facing refusal
	/// for the first field whose value is a zone-less date-time, or <see langword="null"/> when the
	/// payload may be sent.
	/// </summary>
	/// <remarks>
	/// Only top-level string values are examined. <c>null</c>, empty strings, numbers, booleans, nested
	/// objects and arrays are passed through untouched, and so is any string that is not the anchored
	/// zone-less date-time shape.
	/// <para>
	/// A date-only <c>"2024-01-01"</c> is deliberately NOT rejected. Creatio publishes date columns as
	/// <c>Edm.DateTimeOffset</c> as well, so the type cannot distinguish a date column from a timestamp
	/// column, and the platform accepts a date-only literal and stores it in the server's local zone
	/// rather than losing it. Rejecting it would break the ordinary <c>BirthDate</c>-style write for a
	/// shift, not a loss.
	/// </para>
	/// </remarks>
	/// <param name="payload">The data/row object as supplied by the caller.</param>
	/// <param name="propertyTypes">
	/// The entity's property name to Edm type map, or <see langword="null"/> when the service metadata
	/// could not be read. A field that is present in the map and NOT temporal is skipped, which keeps a
	/// text column able to hold a date-shaped string; a field the map does not cover - and every field
	/// when the map is absent - falls back to the shape alone, so an unreadable metadata endpoint never
	/// silently disables the guard.
	/// </param>
	internal static string FindZoneLessDateTime(
		JsonElement payload, IReadOnlyDictionary<string, string> propertyTypes) {
		if (payload.ValueKind != JsonValueKind.Object) {
			return null;
		}
		foreach (JsonProperty property in payload.EnumerateObject()) {
			if (property.Value.ValueKind != JsonValueKind.String) {
				continue;
			}
			string value = property.Value.GetString();
			if (string.IsNullOrEmpty(value) || !ZoneLessDateTimePattern.IsMatch(value)) {
				continue;
			}
			if (propertyTypes is not null
				&& propertyTypes.TryGetValue(property.Name, out string edmType)
				&& !TemporalEdmTypes.Contains(edmType)) {
				continue;
			}
			return BuildMessage(property.Name, value);
		}
		return null;
	}

	/// <summary>
	/// Builds the refusal text: it names the field and the value, says why the literal cannot be sent,
	/// gives both accepted forms, and states that clio does not guess the zone.
	/// </summary>
	private static string BuildMessage(string field, string value) =>
		$"data field '{field}' carries the date-time value '{value}', which has no UTC designator and no "
		+ "time-zone offset. Creatio publishes date-time columns as Edm.DateTimeOffset, whose literal form "
		+ "requires a zone: depending on the platform build such a value is either rejected outright or "
		+ "silently stored as 0001-01-01T00:00:00Z while the call still reports success (GitHub issue #1369). "
		+ "Send the instant explicitly - '2024-01-01T04:00:00Z' for UTC, or '2024-01-01T04:00:00+02:00' for a "
		+ "local offset. clio does not append 'Z' for you, because the zone you meant cannot be guessed. "
		+ "Nothing was written.";
}
