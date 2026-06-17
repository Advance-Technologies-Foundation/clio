using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Common.Telemetry;

/// <summary>
/// OTLP/HTTP JSON wire models for <c>POST /v1/logs</c> (ExportLogsServiceRequest).
/// Per the OTLP JSON encoding (proto3 JSON mapping), int64 fields serialize as JSON strings,
/// which is why <see cref="OtlpLogRecord.TimeUnixNano"/> and <see cref="OtlpAnyValue.IntValue"/>
/// are modeled as <see cref="string"/>.
/// </summary>
internal sealed record OtlpExportLogsServiceRequest(
	[property: JsonPropertyName("resourceLogs")] IReadOnlyList<OtlpResourceLogs> ResourceLogs
);

internal sealed record OtlpResourceLogs(
	[property: JsonPropertyName("resource")] OtlpResource Resource,
	[property: JsonPropertyName("scopeLogs")] IReadOnlyList<OtlpScopeLogs> ScopeLogs
);

internal sealed record OtlpResource(
	[property: JsonPropertyName("attributes")] IReadOnlyList<OtlpKeyValue> Attributes
);

internal sealed record OtlpScopeLogs(
	[property: JsonPropertyName("scope")] OtlpScope Scope,
	[property: JsonPropertyName("logRecords")] IReadOnlyList<OtlpLogRecord> LogRecords
);

internal sealed record OtlpScope(
	[property: JsonPropertyName("name")] string Name
);

internal sealed record OtlpLogRecord(
	[property: JsonPropertyName("timeUnixNano")] string TimeUnixNano,
	[property: JsonPropertyName("severityNumber")] int? SeverityNumber,
	[property: JsonPropertyName("severityText")] string SeverityText,
	[property: JsonPropertyName("attributes")] IReadOnlyList<OtlpKeyValue> Attributes,
	// OTLP LogRecord.event_name (proto field 12) — the single source of the event name on the wire.
	// clio sends it ONLY here (never as an attribute, and clio emits no log body): the edge collector
	// validates it via the dedicated field (OTTL log.event_name) and the ClickHouse exporter maps it
	// to the EventName column. See ADR adr-product-telemetry (single-source-of-truth decision).
	[property: JsonPropertyName("eventName")] string EventName
);

internal sealed record OtlpKeyValue(
	[property: JsonPropertyName("key")] string Key,
	[property: JsonPropertyName("value")] OtlpAnyValue Value
);

internal sealed record OtlpAnyValue(
	[property: JsonPropertyName("stringValue")] string StringValue = null,
	[property: JsonPropertyName("intValue")] string IntValue = null
);
