namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

internal static class ResourceStringHelper {
	/// <summary>
	/// The culture clio authors new page resource values in. <c>update-page</c> and <c>sync-pages</c> write
	/// only this culture; other cultures are written per culture through <see cref="SetCultureValue(JArray, string, string)"/>.
	/// </summary>
	internal const string DefaultCultureName = "en-US";

	private const string ValuesPropertyName = "values";
	private const string CultureNamePropertyName = "cultureName";
	private const string ValuePropertyName = "value";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly Regex MacroResourceStringPattern = new(
		@"#ResourceString\(([^)]+)\)#",
		RegexOptions.Compiled,
		RegexTimeout);
	private static readonly Regex DollarResourceStringPattern = new(
		@"\$Resources\.Strings\.([A-Za-z0-9_]+)",
		RegexOptions.Compiled,
		RegexTimeout);
	private static readonly Regex CaptionBoundaryPattern = new(
		"([a-z])([A-Z])",
		RegexOptions.Compiled,
		RegexTimeout);

	public static HashSet<string> ExtractKeys(string body) {
		var keys = new HashSet<string>();
		if (string.IsNullOrEmpty(body)) {
			return keys;
		}
		foreach (Match match in MacroResourceStringPattern.Matches(body)) {
			keys.Add(match.Groups[1].Value);
		}
		foreach (Match match in DollarResourceStringPattern.Matches(body)) {
			keys.Add(match.Groups[1].Value);
		}
		return keys;
	}

	public static HashSet<string> GetExistingKeys(JArray localizableStrings) {
		var keys = new HashSet<string>();
		if (localizableStrings == null) {
			return keys;
		}
		foreach (JObject item in localizableStrings.Children<JObject>()) {
			string name = item["name"]?.ToString();
			if (!string.IsNullOrEmpty(name)) {
				keys.Add(name);
			}
		}
		return keys;
	}

	/// <summary>
	/// Reports whether a localizable-string <paramref name="key"/> referenced by a freshly-inserted
	/// view node will resolve to a caption at runtime. It mirrors the register-missing-body-key decision
	/// in <see cref="TryResolveResourceValue"/> and additionally treats DS-bound keys as resolving.
	/// </summary>
	/// <param name="key">Localizable-string key referenced by the body.</param>
	/// <param name="resources">Explicit resources passed to the save, or <c>null</c>.</param>
	/// <param name="dsBoundKeys">View-model attribute names bound to a data source, or <c>null</c>.</param>
	/// <returns><c>true</c> when the key resolves to a caption at runtime; otherwise <c>false</c>.</returns>
	public static bool WillResolve(
		string key,
		IReadOnlyDictionary<string, string> resources,
		IReadOnlySet<string> dsBoundKeys) {
		if (string.IsNullOrEmpty(key)) {
			return false;
		}
		if (resources != null && resources.ContainsKey(key)) {
			return true;
		}
		if (dsBoundKeys != null && dsBoundKeys.Contains(key)) {
			return true;
		}
		return IsUsrPrefixed(key);
	}

	/// <summary>
	/// Single definition of the "<c>Usr</c>-prefixed key clio auto-derives a caption for" test, shared by
	/// <see cref="WillResolve"/> and <see cref="TryResolveResourceValue"/> so the read-side verdict and the
	/// write-side registration cannot drift on the prefix comparison.
	/// </summary>
	private static bool IsUsrPrefixed(string key) => key.StartsWith("Usr", StringComparison.Ordinal);

	public static string DeriveCaption(string key) {
		string result = key;
		if (result.EndsWith("_caption")) {
			result = result[..^"_caption".Length];
		}
		if (result.StartsWith("Usr")) {
			result = result[3..];
		}
		result = CaptionBoundaryPattern.Replace(result, "$1 $2");
		result = result.Replace('_', ' ');
		return result.Trim();
	}

	private static JObject CreateLocalizableEntry(string key, string value) {
		var entry = new JObject {
			["uId"] = Guid.NewGuid().ToString(),
			["name"] = key,
			[ValuesPropertyName] = new JArray()
		};
		SetCultureValue(entry, DefaultCultureName, value);
		return entry;
	}

	/// <summary>
	/// Sets the value of one culture on a <c>localizableStrings</c> entry, creating the entry's
	/// <c>values</c> array when it is missing. Every other culture on the entry is left as it is.
	/// </summary>
	/// <param name="entry">A <c>localizableStrings</c> entry (<c>{uId, name, values}</c>).</param>
	/// <param name="culture">Culture name; matched case-insensitively against the stored names.</param>
	/// <param name="value">Value to store for <paramref name="culture"/>.</param>
	/// <returns><see langword="true"/> when the stored value changed; <see langword="false"/> when it was already equal.</returns>
	internal static bool SetCultureValue(JObject entry, string culture, string value) {
		if (entry[ValuesPropertyName] is not JArray values) {
			values = new JArray();
			entry[ValuesPropertyName] = values;
		}
		return SetCultureValue(values, culture, value);
	}

	/// <summary>
	/// Sets the value of one culture in a list of <c>{cultureName, value}</c> objects (an entry's <c>values</c>
	/// or a schema <c>caption</c>), appending a culture object when the culture is missing. Other culture
	/// objects and their order are not touched.
	/// </summary>
	/// <param name="cultureValues">The culture value list to modify.</param>
	/// <param name="culture">Culture name; matched case-insensitively against the stored names.</param>
	/// <param name="value">Value to store for <paramref name="culture"/>.</param>
	/// <returns><see langword="true"/> when the stored value changed; <see langword="false"/> when it was already equal.</returns>
	internal static bool SetCultureValue(JArray cultureValues, string culture, string value) {
		JObject cultureValue = FindCultureValue(cultureValues, culture);
		if (cultureValue == null) {
			cultureValue = new JObject { [CultureNamePropertyName] = culture };
			cultureValues.Add(cultureValue);
		} else if (cultureValue[ValuePropertyName]?.Type == JTokenType.String
			&& string.Equals(cultureValue[ValuePropertyName].ToString(), value, StringComparison.Ordinal)) {
			return false;
		}
		cultureValue[ValuePropertyName] = value;
		return true;
	}

	/// <summary>
	/// Reads the value of one culture from a list of <c>{cultureName, value}</c> objects.
	/// </summary>
	/// <param name="cultureValues">The culture value list, or <see langword="null"/>.</param>
	/// <param name="culture">Culture name; matched case-insensitively.</param>
	/// <returns>The stored value, or <see langword="null"/> when the culture has no value.</returns>
	internal static string GetCultureValue(JArray cultureValues, string culture) =>
		FindCultureValue(cultureValues, culture)?[ValuePropertyName]?.ToString();

	private static JObject FindCultureValue(JArray cultureValues, string culture) =>
		cultureValues?.Children<JObject>().FirstOrDefault(item =>
			string.Equals(item[CultureNamePropertyName]?.ToString(), culture, StringComparison.OrdinalIgnoreCase));

	public static (JArray cleaned, List<string> registered) CleanAndMerge(
		JArray localizableStrings,
		Dictionary<string, string> resources,
		HashSet<string> bodyKeys,
		IReadOnlySet<string> dsBoundKeys = null) {
		var result = new JArray();
		var existingKeys = new HashSet<string>();
		var registered = new List<string>();
		CopyExistingEntries(localizableStrings, resources, result, existingKeys);
		RegisterMissingBodyKeys(bodyKeys, resources, dsBoundKeys, existingKeys, result, registered);
		if (resources != null) {
			foreach (KeyValuePair<string, string> kvp in resources.Where(kvp =>
				         !existingKeys.Contains(kvp.Key) &&
				         !bodyKeys.Contains(kvp.Key))) {
				result.Add(CreateLocalizableEntry(kvp.Key, kvp.Value));
				registered.Add(kvp.Key);
			}
		}
		return (result, registered);
	}

	private static void CopyExistingEntries(
		JArray localizableStrings,
		IReadOnlyDictionary<string, string> resources,
		JArray result,
		ISet<string> existingKeys) {
		if (localizableStrings == null) {
			return;
		}
		foreach (JObject entry in localizableStrings.Children<JObject>()) {
			string name = entry["name"]?.ToString();
			if (string.IsNullOrEmpty(name)) {
				continue;
			}
			var copy = (JObject)entry.DeepClone();
			if (resources != null && resources.TryGetValue(name, out string value)) {
				SetCultureValue(copy, DefaultCultureName, value);
			}
			result.Add(copy);
			existingKeys.Add(name);
		}
	}

	private static void RegisterMissingBodyKeys(
		IEnumerable<string> bodyKeys,
		IReadOnlyDictionary<string, string> resources,
		IReadOnlySet<string> dsBoundKeys,
		ISet<string> existingKeys,
		JArray result,
		ICollection<string> registered) {
		foreach (string key in bodyKeys.Where(key => !existingKeys.Contains(key))) {
			if (!TryResolveResourceValue(resources, key, dsBoundKeys, out string value)) {
				continue;
			}
			result.Add(CreateLocalizableEntry(key, value));
			registered.Add(key);
		}
	}

	private static bool TryResolveResourceValue(
		IReadOnlyDictionary<string, string> resources,
		string key,
		IReadOnlySet<string> dsBoundKeys,
		out string value) {
		if (resources != null && resources.TryGetValue(key, out string explicitValue)) {
			value = explicitValue;
			return true;
		}
		// Skip auto-derivation for DS-bound attributes — the platform auto-provides their captions.
		if (dsBoundKeys != null && dsBoundKeys.Contains(key)) {
			value = null;
			return false;
		}
		if (IsUsrPrefixed(key)) {
			value = DeriveCaption(key);
			return true;
		}
		value = null;
		return false;
	}
}
