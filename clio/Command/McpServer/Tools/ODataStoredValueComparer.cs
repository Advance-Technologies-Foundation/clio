using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Tools;

/// <summary>How a value read back after an OData write relates to the value that was sent.</summary>
internal enum ODataValueMatch {

	/// <summary>The stored value is the sent value, literally or in another form its type allows.</summary>
	Same,

	/// <summary>The stored value is another value.</summary>
	Different,

	/// <summary>
	/// The stored value is the sent value only under an assumption the comparer cannot check: the column type is
	/// unknown, or the server's time zone decides it. The field is not reported as compared.
	/// </summary>
	Unconfirmed
}

/// <summary>
/// Decides whether a value read back after an OData write is the value that was sent.
/// </summary>
/// <remarks>
/// A literal comparison would report the same value as changed: Creatio returns a GUID in its own letter case,
/// a date-time in UTC (<c>...+02:00</c> sent, <c>...Z</c> stored) and a number in its own form (<c>1</c> sent,
/// <c>1.0</c> stored). The field's Edm type from <c>$metadata</c> decides the comparison. Without a type, a
/// literal match or a different JSON kind still decides; a match that holds only in another form (a GUID in
/// another letter case, the same instant written another way) is <see cref="ODataValueMatch.Unconfirmed"/>,
/// because the same text in a string column would be a changed value.
/// <para>A date-only literal (<c>2024-01-01</c>) sent to a date-time column is stored as midnight in the SERVER's
/// time zone (<c>2023-12-31T22:00:00Z</c> on a UTC+02:00 stand, see
/// docs/knowledge/McpServer/odata-datetime-literals-need-an-explicit-zone.md). The read-back does not know that
/// zone, so a stored instant that is midnight in SOME zone is <see cref="ODataValueMatch.Unconfirmed"/>, never
/// <see cref="ODataValueMatch.Same"/>: noon UTC is midnight at UTC+12:00 and a rule's noon alike.</para>
/// <para>A null written to a column that holds no null (text, number, boolean, lookup) reads back as its empty
/// default (<c>""</c>, <c>0</c>, <c>false</c>, the empty GUID); that is the cleared state and matches the sent
/// null. The empty GUID matches only a GUID column: in a text column it is a value someone chose.</para>
/// </remarks>
internal static class ODataStoredValueComparer {

	private static readonly Regex IsoDate = new(@"^\d{4}-\d{2}-\d{2}$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	/// <summary>The widest time-zone offset (UTC+14:00 / UTC-12:00 round up to 14 hours).</summary>
	private static readonly TimeSpan MaxZoneOffset = TimeSpan.FromHours(14);

	/// <summary>Every time-zone offset in use is a whole number of quarter hours.</summary>
	private static readonly TimeSpan ZoneOffsetStep = TimeSpan.FromMinutes(15);

	private static readonly Regex IsoDateTime = new(
		@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	/// <summary>Parses in UTC, so the result never depends on the time zone of the machine running clio.</summary>
	private const DateTimeStyles UtcStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

	/// <summary>Whether a field of <paramref name="edmType"/> can be read back and compared. Binary data cannot.</summary>
	/// <param name="edmType">The field's Edm type, or <see langword="null"/> when unknown.</param>
	internal static bool IsComparable(string? edmType) =>
		edmType is not ("Edm.Stream" or "Edm.Binary");

	/// <summary>How <paramref name="stored"/> relates to the value <paramref name="sent"/> was.</summary>
	/// <param name="sent">The sent value.</param>
	/// <param name="stored">The value read back.</param>
	/// <param name="edmType">The field's Edm type, or <see langword="null"/> when unknown.</param>
	internal static ODataValueMatch Match(JsonElement sent, JsonElement stored, string? edmType) {
		if (sent.ValueKind == JsonValueKind.Null) {
			return stored.ValueKind == JsonValueKind.Null ? ODataValueMatch.Same : MatchClearedDefault(stored, edmType);
		}
		if (stored.ValueKind == JsonValueKind.Null) {
			return ODataValueMatch.Different;
		}
		if ((sent.ValueKind == JsonValueKind.Number || stored.ValueKind == JsonValueKind.Number)
			&& TryGetDouble(sent, out double sentDouble) && TryGetDouble(stored, out double storedDouble)) {
			return SameNumber(sent, stored, sentDouble, storedDouble) ? ODataValueMatch.Same : ODataValueMatch.Different;
		}
		if (sent.ValueKind == JsonValueKind.String && stored.ValueKind == JsonValueKind.String) {
			return MatchStrings(sent.GetString()!, stored.GetString()!, edmType);
		}
		if (sent.ValueKind is JsonValueKind.True or JsonValueKind.False) {
			return Of(sent.ValueKind == stored.ValueKind);
		}
		return Of(string.Equals(sent.GetRawText(), stored.GetRawText(), StringComparison.Ordinal));
	}

	private static ODataValueMatch Of(bool same) => same ? ODataValueMatch.Same : ODataValueMatch.Different;

	private static ODataValueMatch MatchStrings(string sent, string stored, string? edmType) {
		if (string.Equals(sent, stored, StringComparison.Ordinal)) {
			return ODataValueMatch.Same;
		}
		switch (edmType) {
			case "Edm.Guid":
				return Of(Guid.TryParse(sent, out Guid sentGuid) && Guid.TryParse(stored, out Guid storedGuid)
					&& sentGuid == storedGuid);
			case "Edm.DateTimeOffset":
				if (AreSameInstant(sent, stored)) {
					return ODataValueMatch.Same;
				}
				return IsDateOnlyStoredAsMidnightInSomeZone(sent, stored)
					? ODataValueMatch.Unconfirmed
					: ODataValueMatch.Different;
			case "Edm.Date":
				return Of(DateTime.TryParse(sent, CultureInfo.InvariantCulture, UtcStyles, out DateTime sentDate)
					&& DateTime.TryParse(stored, CultureInfo.InvariantCulture, UtcStyles, out DateTime storedDate)
					&& sentDate.Date == storedDate.Date);
			case "Edm.TimeOfDay":
				return Of(TimeSpan.TryParse(sent, CultureInfo.InvariantCulture, out TimeSpan sentTime)
					&& TimeSpan.TryParse(stored, CultureInfo.InvariantCulture, out TimeSpan storedTime)
					&& sentTime == storedTime);
			case null:
				// No type: another form of the same GUID or instant is a match only if the column is not text.
				if (Guid.TryParseExact(sent, "D", out Guid a) && Guid.TryParseExact(stored, "D", out Guid b)) {
					return a == b ? ODataValueMatch.Unconfirmed : ODataValueMatch.Different;
				}
				return IsoDateTime.IsMatch(stored)
					&& (IsDateOnlyStoredAsMidnightInSomeZone(sent, stored)
						|| (IsoDateTime.IsMatch(sent) && AreSameInstant(sent, stored)))
					? ODataValueMatch.Unconfirmed
					: ODataValueMatch.Different;
			default:
				return ODataValueMatch.Different;
		}
	}

	/// <summary>
	/// Whether <paramref name="stored"/> is midnight of the sent date in some time zone: within the widest offset
	/// of it, and off it by a whole number of quarter hours.
	/// </summary>
	private static bool IsDateOnlyStoredAsMidnightInSomeZone(string sent, string stored) {
		if (!IsoDate.IsMatch(sent)
			|| !DateTime.TryParseExact(sent, "yyyy-MM-dd", CultureInfo.InvariantCulture, UtcStyles, out DateTime date)
			|| !DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
				out DateTimeOffset instant)) {
			return false;
		}
		TimeSpan offset = instant.UtcDateTime - date;
		return offset.Duration() <= MaxZoneOffset && offset.Ticks % ZoneOffsetStep.Ticks == 0;
	}

	/// <summary>
	/// How a stored value relates to a sent null: the column's empty default is the cleared state. The empty GUID
	/// is that state only for a GUID column; without a type it is unconfirmed.
	/// </summary>
	private static ODataValueMatch MatchClearedDefault(JsonElement stored, string? edmType) => stored.ValueKind switch {
		JsonValueKind.String when stored.GetString() is { Length: 0 } => ODataValueMatch.Same,
		JsonValueKind.String when Guid.TryParse(stored.GetString(), out Guid guid) && guid == Guid.Empty =>
			edmType switch {
				"Edm.Guid" => ODataValueMatch.Same,
				null => ODataValueMatch.Unconfirmed,
				_ => ODataValueMatch.Different
			},
		JsonValueKind.Number => Of(stored.TryGetDouble(out double number) && number == 0),
		JsonValueKind.False => ODataValueMatch.Same,
		_ => ODataValueMatch.Different
	};

	private static bool AreSameInstant(string sent, string stored) =>
		DateTimeOffset.TryParse(sent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset a)
		&& DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset b)
		&& a.UtcDateTime == b.UtcDateTime;

	/// <summary>
	/// Two numbers are the same when both their double and - where both fit it - their decimal values are equal.
	/// The decimal alone rounds a value below its precision to zero (<c>1e-30</c>), the double alone loses digits
	/// a decimal column keeps.
	/// </summary>
	private static bool SameNumber(JsonElement sent, JsonElement stored, double sentDouble, double storedDouble) =>
		sentDouble.Equals(storedDouble)
		&& (!TryGetDecimal(sent, out decimal sentDecimal) || !TryGetDecimal(stored, out decimal storedDecimal)
			|| sentDecimal == storedDecimal);

	private static bool TryGetDouble(JsonElement value, out double number) {
		number = 0;
		return value.ValueKind switch {
			JsonValueKind.Number => value.TryGetDouble(out number) && double.IsFinite(number),
			JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
				out number) && double.IsFinite(number),
			_ => false
		};
	}

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
