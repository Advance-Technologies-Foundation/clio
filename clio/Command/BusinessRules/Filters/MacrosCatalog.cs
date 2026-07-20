using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.BusinessRules.Filters.Esq;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Maps friendly macros names (mirroring devkit ɵQueryMacrosType members) to their wire values,
/// and classifies each macros by the column data value type it applies to and whether it needs an argument.
/// </summary>
internal static class MacrosCatalog {

	internal enum MacrosColumnKind {
		/// <summary>Date/DateTime/Time columns.</summary>
		DateTime,
		/// <summary>Lookup/Guid columns (CurrentUser, primary-column macros).</summary>
		Lookup
	}

	private sealed record MacrosInfo(EsqQueryMacrosType Type, MacrosColumnKind Kind, bool RequiresArgument);

	private static readonly IReadOnlyDictionary<string, MacrosInfo> ByName =
		new[] {
			Date(EsqQueryMacrosType.Yesterday),
			Date(EsqQueryMacrosType.Today),
			Date(EsqQueryMacrosType.Tomorrow),
			Date(EsqQueryMacrosType.PreviousWeek),
			Date(EsqQueryMacrosType.CurrentWeek),
			Date(EsqQueryMacrosType.NextWeek),
			Date(EsqQueryMacrosType.PreviousMonth),
			Date(EsqQueryMacrosType.CurrentMonth),
			Date(EsqQueryMacrosType.NextMonth),
			Date(EsqQueryMacrosType.PreviousQuarter),
			Date(EsqQueryMacrosType.CurrentQuarter),
			Date(EsqQueryMacrosType.NextQuarter),
			Date(EsqQueryMacrosType.PreviousHalfYear),
			Date(EsqQueryMacrosType.CurrentHalfYear),
			Date(EsqQueryMacrosType.NextHalfYear),
			Date(EsqQueryMacrosType.PreviousYear),
			Date(EsqQueryMacrosType.CurrentYear),
			Date(EsqQueryMacrosType.NextYear),
			Date(EsqQueryMacrosType.PreviousHour),
			Date(EsqQueryMacrosType.CurrentHour),
			Date(EsqQueryMacrosType.NextHour),
			Date(EsqQueryMacrosType.DayOfYearToday),
			DateArg(EsqQueryMacrosType.NextNDays),
			DateArg(EsqQueryMacrosType.PreviousNDays),
			DateArg(EsqQueryMacrosType.NextNHours),
			DateArg(EsqQueryMacrosType.PreviousNHours),
			DateArg(EsqQueryMacrosType.DayOfYearTodayPlusDaysOffset),
			DateArg(EsqQueryMacrosType.NextNDaysOfYear),
			DateArg(EsqQueryMacrosType.PreviousNDaysOfYear),
			Lookup(EsqQueryMacrosType.CurrentUser),
			Lookup(EsqQueryMacrosType.CurrentUserContact),
			Lookup(EsqQueryMacrosType.PrimaryColumn),
			Lookup(EsqQueryMacrosType.PrimaryDisplayColumn),
			Lookup(EsqQueryMacrosType.PrimaryImageColumn),
			Lookup(EsqQueryMacrosType.PrimaryColorColumn)
		}.ToDictionary(info => info.Type.ToString(), info => info, StringComparer.OrdinalIgnoreCase);

	private static MacrosInfo Date(EsqQueryMacrosType type) => new(type, MacrosColumnKind.DateTime, false);
	private static MacrosInfo DateArg(EsqQueryMacrosType type) => new(type, MacrosColumnKind.DateTime, true);
	private static MacrosInfo Lookup(EsqQueryMacrosType type) => new(type, MacrosColumnKind.Lookup, false);

	internal static IEnumerable<string> KnownNames => ByName.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

	internal static bool TryResolve(string name, out EsqQueryMacrosType type, out MacrosColumnKind kind, out bool requiresArgument) {
		if (!string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out MacrosInfo? info)) {
			type = info.Type;
			kind = info.Kind;
			requiresArgument = info.RequiresArgument;
			return true;
		}

		type = EsqQueryMacrosType.None;
		kind = MacrosColumnKind.DateTime;
		requiresArgument = false;
		return false;
	}

	internal static bool TryResolveName(int macrosTypeValue, out string name, out bool requiresArgument) {
		foreach (MacrosInfo info in ByName.Values) {
			if ((int)info.Type == macrosTypeValue) {
				name = info.Type.ToString();
				requiresArgument = info.RequiresArgument;
				return true;
			}
		}

		name = string.Empty;
		requiresArgument = false;
		return false;
	}
}
