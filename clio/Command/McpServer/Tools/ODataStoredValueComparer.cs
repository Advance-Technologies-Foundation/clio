using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Decides whether a value read back after an OData write is the value that was sent.
/// </summary>
/// <remarks>
/// A literal comparison would report the same value as changed: Creatio returns a GUID in its own letter case,
/// a date-time in UTC (<c>...+02:00</c> sent, <c>...Z</c> stored) and a number in its own form (<c>1</c> sent,
/// <c>1.0</c> stored). The field's Edm type from <c>$metadata</c> decides the comparison; without a type map the
/// shape of the two values does, conservatively - an unrecognised shape is compared exactly.
/// <para>A date-only literal (<c>2024-01-01</c>) sent to a date-time column is stored as midnight in the SERVER's
/// time zone (<c>2023-12-31T22:00:00Z</c> on a UTC+02:00 stand, see
/// docs/knowledge/McpServer/odata-datetime-literals-need-an-explicit-zone.md), so it matches any stored instant
/// within the span of time-zone offsets around that date.</para>
/// <para>A null written to a column that holds no null (text, number, boolean) reads back as its empty default
/// (<c>""</c>, <c>0</c>, <c>false</c>); that is the cleared state and matches the sent null.</para>
/// </remarks>
internal static class ODataStoredValueComparer {

	private static readonly Regex IsoDate = new(@"^\d{4}-\d{2}-\d{2}$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	/// <summary>The widest time-zone offset (UTC+14:00 / UTC-12:00 round up to 14 hours).</summary>
	private static readonly TimeSpan MaxZoneOffset = TimeSpan.FromHours(14);

	private static readonly Regex IsoDateTime = new(
		@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	/// <summary>Whether a field of <paramref name="edmType"/> can be read back and compared. Binary data cannot.</summary>
	/// <param name="edmType">The field's Edm type, or <see langword="null"/> when unknown.</param>
	internal static bool IsComparable(string? edmType) =>
		edmType is not ("Edm.Stream" or "Edm.Binary");

	/// <summary>Whether <paramref name="stored"/> is the value <paramref name="sent"/> was.</summary>
	/// <param name="sent">The sent value.</param>
	/// <param name="stored">The value read back.</param>
	/// <param name="edmType">The field's Edm type, or <see langword="null"/> when unknown.</param>
	internal static bool AreEquivalent(JsonElement sent, JsonElement stored, string? edmType) {
		if (sent.ValueKind == JsonValueKind.Null) {
			return stored.ValueKind == JsonValueKind.Null || IsEmptyDefault(stored);
		}
		if (stored.ValueKind == JsonValueKind.Null) {
			return false;
		}
		if (TryGetDecimal(sent, out decimal sentNumber) && TryGetDecimal(stored, out decimal storedNumber)
			&& (sent.ValueKind == JsonValueKind.Number || stored.ValueKind == JsonValueKind.Number)) {
			return sentNumber == storedNumber;
		}
		if (sent.ValueKind == JsonValueKind.String && stored.ValueKind == JsonValueKind.String) {
			return AreEquivalentStrings(sent.GetString()!, stored.GetString()!, edmType);
		}
		if (sent.ValueKind is JsonValueKind.True or JsonValueKind.False) {
			return sent.ValueKind == stored.ValueKind;
		}
		return string.Equals(sent.GetRawText(), stored.GetRawText(), StringComparison.Ordinal);
	}

	private static bool AreEquivalentStrings(string sent, string stored, string? edmType) {
		if (string.Equals(sent, stored, StringComparison.Ordinal)) {
			return true;
		}
		switch (edmType) {
			case "Edm.Guid":
				return Guid.TryParse(sent, out Guid sentGuid) && Guid.TryParse(stored, out Guid storedGuid)
					&& sentGuid == storedGuid;
			case "Edm.DateTimeOffset":
				return IsDateOnlyStoredAsMidnight(sent, stored) || AreSameInstant(sent, stored);
			case "Edm.Date":
				return DateTime.TryParse(sent, CultureInfo.InvariantCulture, UtcStyles, out DateTime sentDate)
					&& DateTime.TryParse(stored, CultureInfo.InvariantCulture, UtcStyles, out DateTime storedDate)
					&& sentDate.Date == storedDate.Date;
			case "Edm.TimeOfDay":
				return TimeSpan.TryParse(sent, CultureInfo.InvariantCulture, out TimeSpan sentTime)
					&& TimeSpan.TryParse(stored, CultureInfo.InvariantCulture, out TimeSpan storedTime)
					&& sentTime == storedTime;
			case null:
				// No type map: only shapes that cannot be anything else are normalised.
				if (Guid.TryParseExact(sent, "D", out Guid a) && Guid.TryParseExact(stored, "D", out Guid b)) {
					return a == b;
				}
				return IsoDateTime.IsMatch(stored)
					&& (IsDateOnlyStoredAsMidnight(sent, stored) || (IsoDateTime.IsMatch(sent) && AreSameInstant(sent, stored)));
			default:
				return false;
		}
	}

	private static bool IsDateOnlyStoredAsMidnight(string sent, string stored) =>
		IsoDate.IsMatch(sent)
		&& DateTime.TryParseExact(sent, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime date)
		&& DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset instant)
		&& (instant.UtcDateTime - date).Duration() <= MaxZoneOffset;

	/// <summary>Parses in UTC, so the result never depends on the time zone of the machine running clio.</summary>
	private const DateTimeStyles UtcStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

	/// <summary>
	/// Whether <paramref name="stored"/> is the value a column without a null takes when null is written: an empty
	/// text, zero, false or the empty GUID. Clearing such a column with null reads back as that value, which is the
	/// cleared state, not a value a rule chose.
	/// </summary>
	private static bool IsEmptyDefault(JsonElement stored) => stored.ValueKind switch {
		JsonValueKind.String => stored.GetString() is { } text
			&& (text.Length == 0 || (Guid.TryParse(text, out Guid guid) && guid == Guid.Empty)),
		JsonValueKind.Number => stored.TryGetDecimal(out decimal number) && number == 0,
		JsonValueKind.False => true,
		_ => false
	};

	private static bool AreSameInstant(string sent, string stored) =>
		DateTimeOffset.TryParse(sent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset a)
		&& DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset b)
		&& a.UtcDateTime == b.UtcDateTime;

	private static bool TryGetDecimal(JsonElement value, out decimal number) {
		number = 0;
		return value.ValueKind switch {
			JsonValueKind.Number => value.TryGetDecimal(out number),
			JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
				out number),
			_ => false
		};
	}
}
