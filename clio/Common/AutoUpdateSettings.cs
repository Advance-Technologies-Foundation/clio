using System;
using System.Collections.Generic;
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

	/// <summary>
	/// Policy members the running clio build does not know, carried so a save preserves them.
	/// </summary>
	[JsonExtensionData]
	[System.Text.Json.Serialization.JsonIgnore]
	public IDictionary<string, JToken> AdditionalData { get; set; }

	[System.Runtime.Serialization.OnDeserialized]
	private void RemoveDeclaredOverflowMembers(System.Runtime.Serialization.StreamingContext context) =>
		JsonOverflowMembers.RemoveDeclaredMembers(this, AdditionalData);

	/// <summary>Gets or sets the next scheduled attempt, or <see langword="null"/> when none is scheduled.</summary>
	/// <remarks>
	/// Nullable so an unscheduled policy is OMITTED from appsettings.json rather than written as the
	/// default <see cref="DateTimeOffset"/>. A reader (including an older clio) that finds no
	/// <c>next-run</c> treats the policy as due, which is what "never ran yet" means.
	/// </remarks>
	[JsonProperty("next-run", NullValueHandling = NullValueHandling.Ignore)]
	public DateTimeOffset? NextRun { get; set; }
}

/// <summary>Contains independent schedules for clio, knowledge, and toolkit updates.</summary>
[JsonConverter(typeof(AutoUpdateSettingsConverter))]
public sealed class AutoUpdateSettings {
	/// <summary>
	/// Autoupdate members the running clio build does not know, carried so a save preserves them.
	/// </summary>
	[JsonExtensionData]
	[System.Text.Json.Serialization.JsonIgnore]
	public IDictionary<string, JToken> AdditionalData { get; set; }

	[System.Runtime.Serialization.OnDeserialized]
	private void RemoveDeclaredOverflowMembers(System.Runtime.Serialization.StreamingContext context) =>
		JsonOverflowMembers.RemoveDeclaredMembers(this, AdditionalData);

	/// <summary>Gets or sets the clio update schedule.</summary>
	[JsonProperty("clio")]
	public AutoUpdatePolicy Clio { get; set; } = CreatePolicy(480, false);

	/// <summary>Gets or sets the knowledge update schedule.</summary>
	[JsonProperty("knowledge")]
	public AutoUpdatePolicy Knowledge { get; set; } = CreatePolicy(60);

	/// <summary>Gets or sets the toolkit update schedule.</summary>
	[JsonProperty("toolkit")]
	public AutoUpdatePolicy Toolkit { get; set; } = CreatePolicy(60, false);

	private static AutoUpdatePolicy CreatePolicy(int frequencyMinutes, bool enabled = true) => new() {
		Enabled = enabled,
		FrequencyMinutes = frequencyMinutes
	};
}

internal sealed class AutoUpdateSettingsConverter : JsonConverter<AutoUpdateSettings> {
	public override bool CanWrite => false;

	public override AutoUpdateSettings ReadJson(JsonReader reader, Type objectType,
		AutoUpdateSettings existingValue, bool hasExistingValue, JsonSerializer serializer) {
		// Year-0001 defaults used to be rewritten as "0001-01-01T01:24:00+01:24": JToken.Load parses a
		// timestamp with the default DateParseHandling.DateTime, which yields a DateTime the serializer
		// then converts to the LOCAL offset - and for year 0001 that offset is the zone's historical local
		// mean time. Keeping timestamps as strings here lets Populate bind DateTimeOffset directly and
		// preserves the offset the file actually carries.
		// Captured BEFORE the token is loaded: it is the path of the autoupdate member itself, and it is
		// what makes a bind failure inside this section report as "autoupdate.clio.enabled" rather than as
		// the rootless "clio.enabled" a fresh token reader would produce. The settings bootstrap names that
		// path to the user (issue #1462), so a path that omits the section is a worse diagnostic.
		string outerPath = reader.Path;
		DateParseHandling previousDateParseHandling = reader.DateParseHandling;
		JToken token;
		try {
			reader.DateParseHandling = DateParseHandling.None;
			token = JToken.Load(reader);
		}
		finally {
			// The reader is the caller's, and it keeps reading the rest of appsettings.json after this
			// converter returns.
			reader.DateParseHandling = previousDateParseHandling;
		}
		AutoUpdateSettings settings = new();
		if (token.Type == JTokenType.Boolean) {
			settings.Clio.Enabled = token.Value<bool>();
		}
		else if (token.Type == JTokenType.Object) {
			using JTokenReader tokenReader = new(token, outerPath);
			serializer.Populate(tokenReader, settings);
		}
		return settings;
	}

	public override void WriteJson(JsonWriter writer, AutoUpdateSettings value, JsonSerializer serializer) {
		throw new NotSupportedException();
	}
}
