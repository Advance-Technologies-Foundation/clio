using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common;

/// <summary>Identifies an automatically updated local component.</summary>
public enum AutoUpdateTarget {
	/// <summary>The clio global tool.</summary>
	Clio,
	/// <summary>Installed Clio knowledge.</summary>
	Knowledge,
	/// <summary>The Creatio development toolkit.</summary>
	Toolkit
}

/// <summary>Controls one best-effort automatic update.</summary>
public sealed class AutoUpdatePolicy {
	/// <summary>Gets or sets whether the update runs automatically.</summary>
	[JsonProperty("enabled")]
	public bool Enabled { get; set; } = true;

	/// <summary>Gets or sets the interval between attempts, in minutes.</summary>
	[JsonProperty("frequency-minutes")]
	public int FrequencyMinutes { get; set; }

	/// <summary>Gets or sets the next scheduled attempt.</summary>
	[JsonProperty("next-run")]
	public DateTimeOffset NextRun { get; set; }
}

/// <summary>Contains independent schedules for clio, knowledge, and toolkit updates.</summary>
[JsonConverter(typeof(AutoUpdateSettingsConverter))]
public sealed class AutoUpdateSettings {
	/// <summary>Gets or sets the clio update schedule.</summary>
	[JsonProperty("clio")]
	public AutoUpdatePolicy Clio { get; set; } = CreatePolicy(480);

	/// <summary>Gets or sets the knowledge update schedule.</summary>
	[JsonProperty("knowledge")]
	public AutoUpdatePolicy Knowledge { get; set; } = CreatePolicy(60);

	/// <summary>Gets or sets the toolkit update schedule.</summary>
	[JsonProperty("toolkit")]
	public AutoUpdatePolicy Toolkit { get; set; } = CreatePolicy(60);

	[JsonIgnore]
	internal bool WasLegacyScalar { get; set; }

	private static AutoUpdatePolicy CreatePolicy(int frequencyMinutes) => new() {
		FrequencyMinutes = frequencyMinutes
	};
}

internal sealed class AutoUpdateSettingsConverter : JsonConverter<AutoUpdateSettings> {
	public override bool CanWrite => false;

	public override AutoUpdateSettings ReadJson(JsonReader reader, Type objectType,
		AutoUpdateSettings existingValue, bool hasExistingValue, JsonSerializer serializer) {
		JToken token = JToken.Load(reader);
		AutoUpdateSettings settings = new();
		if (token.Type == JTokenType.Boolean) {
			settings.Clio.Enabled = token.Value<bool>();
			settings.WasLegacyScalar = true;
		}
		else if (token.Type == JTokenType.Object) {
			serializer.Populate(token.CreateReader(), settings);
		}
		return settings;
	}

	public override void WriteJson(JsonWriter writer, AutoUpdateSettings value, JsonSerializer serializer) {
		throw new NotSupportedException();
	}
}
