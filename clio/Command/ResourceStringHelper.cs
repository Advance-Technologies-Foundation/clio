namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

internal static class ResourceStringHelper {
	private static readonly Regex ResourceStringPattern = new(
		@"#ResourceString\(([^)]+)\)#",
		RegexOptions.Compiled);

	public static HashSet<string> ExtractKeys(string body) {
		var keys = new HashSet<string>();
		if (string.IsNullOrEmpty(body)) {
			return keys;
		}
		foreach (Match match in ResourceStringPattern.Matches(body)) {
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

	public static string DeriveCaption(string key) {
		string result = key;
		if (result.EndsWith("_caption")) {
			result = result[..^"_caption".Length];
		}
		if (result.StartsWith("Usr")) {
			result = result[3..];
		}
		result = Regex.Replace(result, "([a-z])([A-Z])", "$1 $2");
		result = result.Replace('_', ' ');
		return result.Trim();
	}

	private static JObject CreateLocalizableEntry(string key, string value) {
		return new JObject {
			["uId"] = Guid.NewGuid().ToString(),
			["name"] = key,
			["values"] = new JArray {
				new JObject {
					["cultureName"] = "en-US",
					["value"] = value
				}
			}
		};
	}

	public static (JArray cleaned, List<string> registered) CleanAndMerge(
		JArray localizableStrings,
		Dictionary<string, string> resources,
		HashSet<string> bodyKeys) {
		var result = new JArray();
		var existingCustomKeys = new HashSet<string>();
		if (localizableStrings != null) {
			foreach (JObject entry in localizableStrings.Children<JObject>()) {
				string name = entry["name"]?.ToString();
				if (name != null && name.StartsWith("Usr")) {
					result.Add(entry);
					existingCustomKeys.Add(name);
				}
			}
		}
		var registered = new List<string>();
		foreach (string key in bodyKeys) {
			if (existingCustomKeys.Contains(key)) {
				continue;
			}
			string value;
			if (resources != null && resources.TryGetValue(key, out string explicitValue)) {
				value = explicitValue;
			} else if (key.StartsWith("Usr")) {
				value = DeriveCaption(key);
			} else {
				continue;
			}
			result.Add(CreateLocalizableEntry(key, value));
			registered.Add(key);
		}
		if (resources != null) {
			foreach (var kvp in resources) {
				if (!existingCustomKeys.Contains(kvp.Key) && !bodyKeys.Contains(kvp.Key)) {
					result.Add(CreateLocalizableEntry(kvp.Key, kvp.Value));
					registered.Add(kvp.Key);
				}
			}
		}
		return (result, registered);
	}

	public static (JArray merged, List<string> registered) MergeResources(
		JArray localizableStrings,
		Dictionary<string, string> resources,
		HashSet<string> bodyKeys) {
		var existing = GetExistingKeys(localizableStrings);
		var result = localizableStrings != null
			? new JArray(localizableStrings)
			: new JArray();
		var registered = new List<string>();
		var missing = bodyKeys.Except(existing).ToList();
		foreach (string key in missing) {
			string value;
			if (resources != null && resources.TryGetValue(key, out string explicitValue)) {
				value = explicitValue;
			} else if (key.StartsWith("Usr")) {
				value = DeriveCaption(key);
			} else {
				continue;
			}
			result.Add(CreateLocalizableEntry(key, value));
			registered.Add(key);
		}
		return (result, registered);
	}
}
