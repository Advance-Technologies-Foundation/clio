using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Common.Telemetry;

/// <summary>
/// Product measurement request accepted by the MCP telemetry tool.
/// </summary>
public sealed record MeasurementRequest(
	[property: JsonPropertyName("session_id")] string SessionId,
	[property: JsonPropertyName("event_name")] string EventName,
	[property: JsonPropertyName("coding_agent")] string CodingAgent,
	[property: JsonPropertyName("skill_version")] string SkillVersion,
	[property: JsonPropertyName("plugin_version")] string PluginVersion,
	[property: JsonPropertyName("duration_ms")] long? DurationMs = null,
	[property: JsonPropertyName("telemetry_consent")] string TelemetryConsent = null
) {
	[JsonExtensionData]
	public Dictionary<string, System.Text.Json.JsonElement> ExtensionData { get; init; }
}

/// <summary>
/// Result returned from the measurement service and MCP tool.
/// </summary>
public sealed record MeasurementResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("event_id")] string EventId = null,
	[property: JsonPropertyName("error")] MeasurementError Error = null
);

/// <summary>
/// Local product telemetry consent status.
/// </summary>
public sealed record MeasurementConsentResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("telemetry_consent")] string TelemetryConsent
);

/// <summary>
/// Structured measurement validation or persistence error.
/// </summary>
public sealed record MeasurementError(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message
);

internal sealed record ConsentState(
	[property: JsonPropertyName("telemetry_consent")] string TelemetryConsent,
	[property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt
);

internal sealed record MeasurementSessionState(
	[property: JsonPropertyName("session_id")] string SessionId,
	[property: JsonPropertyName("events")] Dictionary<string, DateTimeOffset> Events
);

internal sealed record OpenTelemetryLogEvent(
	[property: JsonPropertyName("time_unix_nano")] long TimeUnixNano,
	[property: JsonPropertyName("severity_text")] string SeverityText,
	[property: JsonPropertyName("body")] OpenTelemetryValue Body,
	[property: JsonPropertyName("attributes")] IReadOnlyList<OpenTelemetryAttribute> Attributes
);

internal sealed record OpenTelemetryAttribute(
	[property: JsonPropertyName("key")] string Key,
	[property: JsonPropertyName("value")] OpenTelemetryValue Value
);

internal sealed record OpenTelemetryValue(
	[property: JsonPropertyName("string_value")] string StringValue = null,
	[property: JsonPropertyName("int_value")] long? IntValue = null
);
