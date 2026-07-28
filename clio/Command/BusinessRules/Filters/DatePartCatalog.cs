using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.BusinessRules.Filters.Esq;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Maps friendly <c>datePart</c> names (mirroring devkit ɵDatePartType members) to their wire value and
/// classifies the value kind each part compares against: an Integer for calendar/clock parts
/// (Day/Week/Month/Year/Weekday/Hour) or a Time-of-day value for HourMinute.
/// </summary>
internal static class DatePartCatalog {

	internal enum DatePartValueKind {
		/// <summary>Day/Week/Month/Year/Weekday/Hour — compared to an Integer parameter.</summary>
		Integer,
		/// <summary>HourMinute — compared to a Time-of-day (HH:mm[:ss]) parameter.</summary>
		Time
	}

	private sealed record DatePartInfo(EsqDatePartType Type, DatePartValueKind ValueKind);

	private static readonly IReadOnlyDictionary<string, DatePartInfo> ByName =
		new Dictionary<string, DatePartInfo>(StringComparer.OrdinalIgnoreCase) {
			["Day"] = new(EsqDatePartType.Day, DatePartValueKind.Integer),
			["Week"] = new(EsqDatePartType.Week, DatePartValueKind.Integer),
			["Month"] = new(EsqDatePartType.Month, DatePartValueKind.Integer),
			["Year"] = new(EsqDatePartType.Year, DatePartValueKind.Integer),
			["Weekday"] = new(EsqDatePartType.Weekday, DatePartValueKind.Integer),
			["Hour"] = new(EsqDatePartType.Hour, DatePartValueKind.Integer),
			["HourMinute"] = new(EsqDatePartType.HourMinute, DatePartValueKind.Time),
			// alias: "Time" reads naturally for "at 11:06 AM" prompts and maps to the same HourMinute part.
			["Time"] = new(EsqDatePartType.HourMinute, DatePartValueKind.Time)
		};

	internal static IEnumerable<string> KnownNames =>
		ByName.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

	internal static bool TryResolve(string name, out EsqDatePartType type, out DatePartValueKind valueKind) {
		if (!string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out DatePartInfo? info)) {
			type = info.Type;
			valueKind = info.ValueKind;
			return true;
		}

		type = EsqDatePartType.None;
		valueKind = DatePartValueKind.Integer;
		return false;
	}

	internal static bool TryResolveName(int datePartTypeValue, out string name, out DatePartValueKind valueKind) {
		foreach (KeyValuePair<string, DatePartInfo> entry in ByName) {
			if ((int)entry.Value.Type == datePartTypeValue
				&& string.Equals(entry.Key, entry.Value.Type.ToString(), StringComparison.OrdinalIgnoreCase)) {
				name = entry.Key;
				valueKind = entry.Value.ValueKind;
				return true;
			}
		}

		name = string.Empty;
		valueKind = DatePartValueKind.Integer;
		return false;
	}
}
