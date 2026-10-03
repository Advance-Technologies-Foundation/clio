using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Clio.Common.Studio;

/// <summary>Resolves declared inputs as JSON values, never executable text.</summary>
public static class StudioInputs {
	private static readonly Regex Placeholder = new(@"(\$?)\$\{([a-zA-Z][a-zA-Z0-9_]*)\}", RegexOptions.CultureInvariant);

	/// <summary>Returns resolved values and the complete missing-input inventory.</summary>
	public static (JObject Values, JArray Missing) Resolve(JObject profile, JObject supplied, JObject previous, bool generate) {
		JObject definitions = profile["inputs"] as JObject ?? new();
		JObject values = new();
		JArray missing = [];
		foreach (JProperty property in supplied.Properties()) {
			if (definitions[property.Name] is null) throw new ArgumentException("Inputs file contains an undeclared input name.");
		}
		foreach (JProperty property in definitions.Properties()) {
			if (!Regex.IsMatch(property.Name, "^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant) || property.Value is not JObject definition) {
				throw new ArgumentException("Each input must have a valid name and a definition object.");
			}
			bool required = definition.Value<bool?>("required") == true;
			JToken value = new[] { supplied[property.Name], definition["value"], previous[property.Name] }
				.FirstOrDefault(v => v is not null && v.Type != JTokenType.Null &&
					!(required && v.Type == JTokenType.String && string.IsNullOrWhiteSpace(v.Value<string>())));
			string generator = definition.Value<string>("generate");
			if (generator is not null && generator is not ("password" or "uuid" or "rsa-private-key")) throw new ArgumentException("Unsupported Studio input generator.");
			if (value is null && generator is not null) {
				if (!generate) continue;
				value = generator switch {
					"password" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
					"uuid" => Guid.NewGuid().ToString(),
					_ => PrivateKey()
				};
			}
			if (value is not null && value.Type != JTokenType.Null) {
				if (value is not JValue) throw new ArgumentException("Studio input values must be scalar JSON values.");
				values[property.Name] = value.DeepClone();
			} else if (definition.Value<bool?>("required") == true) {
				missing.Add(new JObject { ["name"] = property.Name, ["description"] = definition.Value<string>("description") ?? "Required deployment input", ["secret"] = definition.Value<bool?>("secret") ?? false });
			}
		}
		return (values, missing);
	}

	/// <summary>Expands declared placeholders while preserving JSON scalar types.</summary>
	public static JToken Expand(JToken token, JObject values) {
		if (token is JObject obj) {
			JObject result = new();
			foreach (JProperty p in obj.Properties()) result[p.Name] = Expand(p.Value, values);
			return result;
		}
		if (token is JArray array) return new JArray(array.Select(child => Expand(child, values)));
		if (token.Type != JTokenType.String) return token.DeepClone();
		string text = token.Value<string>();
		Match whole = Placeholder.Match(text);
		if (whole.Success && whole.Length == text.Length && whole.Groups[1].Length == 0) return Required(values, whole.Groups[2].Value).DeepClone();
		return Placeholder.Replace(text, match => match.Groups[1].Length > 0 ? match.Value[1..] : Required(values, match.Groups[2].Value).ToString());
	}

	private static JToken Required(JObject values, string name) => values[name] ?? throw new ArgumentException($"Deployment references unresolved input {name}.");
	private static string PrivateKey() { using RSA rsa = RSA.Create(2048); return rsa.ExportRSAPrivateKeyPem(); }
}
