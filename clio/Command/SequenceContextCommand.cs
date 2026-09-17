using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for bounded, read-only sequence discovery.</summary>
[Verb("get-sequence-context", HelpText = "Read effective sequence fields, lookups and configuration choices")]
public sealed class SequenceContextOptions : RemoteCommandOptions {
	/// <summary>Optional sequence definition to inspect.</summary>
	[Option("sequence-id", HelpText = "Optional sequence record ID to inspect")]
	public Guid? SequenceId { get; set; }
}

/// <summary>Reads the sequence context from the current environment.</summary>
public interface ISequenceContextReader {
	/// <summary>Returns metadata and bounded choices; failed sections remain explicit.</summary>
	SequenceContextResult Read(Guid? sequenceId);
}

/// <summary>One bounded section of sequence discovery.</summary>
public sealed record SequenceContextSection(
	[property: JsonPropertyName("state")] string State,
	[property: JsonPropertyName("data")] object? Data,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>Environment-derived context; availability does not imply write permission.</summary>
public sealed record SequenceContextResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("availability")] string Availability,
	[property: JsonPropertyName("sections")] IReadOnlyDictionary<string, SequenceContextSection> Sections,
	[property: JsonPropertyName("limitations")] IReadOnlyList<string> Limitations);

/// <summary>Shares sequence discovery between the CLI and MCP.</summary>
public sealed class SequenceContextCommand(ISequenceContextReader reader, ILogger logger)
	: Command<SequenceContextOptions> {
	/// <summary>Reads context without writing records or configuration.</summary>
	public SequenceContextResult Read(SequenceContextOptions options) => reader.Read(options.SequenceId);

	/// <inheritdoc />
	public override int Execute(SequenceContextOptions options) {
		try {
			SequenceContextResult result = Read(options);
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message));
			return 1;
		}
	}
}

/// <summary>Reuses merged schema metadata and read-only DataService queries.</summary>
public sealed class SequenceContextReader(
	IRemoteEntitySchemaColumnManager schemas,
	IApplicationClient client,
	IServiceUrlBuilder urls) : ISequenceContextReader {
	internal const int RowLimit = 100;
	internal const int ResponseByteLimit = 200_000;
	private static readonly string[] SchemaNames = [
		"Sequence", "SequenceStep", "SequenceParticipant", "SequenceRuleset", "DeliverySchedule", "DeliveryScheduleSlot"
	];
	private static readonly string[] ChoiceSchemas = [
		"SequenceStatus", "SequenceType", "SeqIntervalType", "SeqRecipientReplyBehavior", "SequenceStepType", "SequenceStepPostponeTimeUnits", "SequenceAudienceStage",
		"SeqParticipantStatus", "SeqEnrollmentEligibility", "SeqEmailMode", "SeqEmailThreadingMode",
		"ActivityPriority", "SequenceStepAction", "DayOfWeek", "SequenceRuleset", "DeliverySchedule"
	];

	/// <inheritdoc />
	public SequenceContextResult Read(Guid? sequenceId) {
		if (sequenceId == Guid.Empty) {
			throw new ArgumentException("sequence-id must be a non-empty UUID.");
		}
		Dictionary<string, SequenceContextSection> sections = new(StringComparer.Ordinal);
		SequenceContextSection catalog = ReadRows("SysSchema", ["Id"], "Name", "Sequence", 1);
		sections["schema-presence"] = catalog;
		if (catalog.State == "failed") {
			return Result("unknown", sections);
		}
		if (((JsonElement[])catalog.Data!).Length == 0) {
			return Result("absent", sections);
		}
		sections["schema-presence"] = catalog with { State = "complete" };
		foreach (string schema in SchemaNames) {
			try {
				EntitySchemaPropertiesInfo metadata = schemas.GetSchemaProperties(new() {
					SchemaName = schema, RuntimeReadTimeoutMilliseconds = 10_000
				});
				var columns = metadata?.Columns;
				if (columns is null || columns.Count == 0) {
					sections["schema:" + schema] = new("failed", null, "Effective schema columns are unavailable.");
					continue;
				}
				sections["schema:" + schema] = new(columns.Count > RowLimit ? "truncated" : "complete",
					columns.Take(RowLimit).Select(column => new {
						name = column.Name, type = column.Type, required = column.Required,
						reference = column.ReferenceSchemaName
					}).ToArray());
			} catch (Exception exception) {
				sections["schema:" + schema] = Failure(exception);
			}
		}
		foreach (string schema in ChoiceSchemas) {
			sections["choices:" + schema] = ReadRows(schema, ["Id", "Name"]);
		}
		if (sequenceId.HasValue) {
			sections["sequence"] = ReadRows("Sequence", ["Id", "Name", "Status", "Ruleset", "DeliverySchedule"],
				"Id", sequenceId.Value, 1);
			sections["steps"] = ReadRows("SequenceStep", ["Id", "Name", "Index", "Type", "Postpone", "Measurement"],
				"Sequence", sequenceId.Value);
			RequireVisibleRows(sections, "steps", "No visible sequence steps were found.");
			InspectPrerequisites(sections);
		}
		SequenceContextResult result = Result("present", sections);
		if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result)) > ResponseByteLimit) {
			return new(false, "present", new Dictionary<string, SequenceContextSection> {
				["context"] = new("failed", null, "Context exceeds the response budget; use targeted schema and ESQ reads.")
			}, result.Limitations);
		}
		return result;
	}

	private SequenceContextSection ReadRows(string schema, string[] columns,
		string? filterColumn = null, object? filterValue = null, int limit = RowLimit) {
		try {
			var filters = filterColumn is null
				? Array.Empty<SelectQueryHelper.SelectQueryFilterDefinition>()
				: new[] { new SelectQueryHelper.SelectQueryFilterDefinition(filterColumn, filterValue!,
					filterValue is Guid ? 0 : 1) };
			object query = SelectQueryHelper.BuildSelectQuery(schema,
				columns.Select(column => new SelectQueryHelper.SelectQueryColumnDefinition(column, column)).ToArray(),
				filters, rowCount: limit + 1);
			JsonNode request = JsonSerializer.SerializeToNode(query)!;
			if (schema == "SequenceStep") {
				request["columns"]!["items"]!["Index"]!["orderDirection"] = 1;
				request["columns"]!["items"]!["Index"]!["orderPosition"] = 0;
			}
			string json = client.ExecutePostRequest(urls.Build(ServiceUrlBuilder.KnownRoute.Select),
				request.ToJsonString(), 10_000, 1, 1);
			if (Encoding.UTF8.GetByteCount(json) > ResponseByteLimit) {
				return new("failed", null, "DataService response exceeds the response budget.");
			}
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("success", out JsonElement success) || success.ValueKind != JsonValueKind.True
				|| !root.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array) {
				return new("failed", null, "DataService read failed. Check schema availability and read permissions; do not infer absence.");
			}
			JsonElement[] values = rows.EnumerateArray().Take(limit).Select(row => row.Clone()).ToArray();
			if (values.Any(row => columns.Any(column => !row.TryGetProperty(column, out _)))) {
				return new("failed", null, "DataService omitted a requested field; check effective schema compatibility.");
			}
			return new(rows.GetArrayLength() > limit ? "truncated" : "complete", values);
		} catch (Exception exception) {
			return Failure(exception);
		}
	}

	private void InspectPrerequisites(Dictionary<string, SequenceContextSection> sections) {
		if (sections["sequence"].Data is not JsonElement[] records) {
			return;
		}
		if (records.Length == 0) {
			sections["sequence"] = new("missing", records, "Sequence not found or not visible to the current user.");
			return;
		}
		foreach (string field in new[] { "Ruleset", "DeliverySchedule" }) {
			JsonElement value = records[0].GetProperty(field);
			if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out JsonElement id)) {
				value = id;
			}
			if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out Guid reference)
				|| reference == Guid.Empty) {
				sections["prerequisite:" + field] = new("missing", null, field + " must be configured.");
				continue;
			}
			if (field == "Ruleset") {
				sections["selected-ruleset"] = ReadRows("SequenceRuleset",
					["Id", "Name", "EnrollmentEligibility", "MaxActiveParticipantsPerUser", "MaxAddsPerUserPer24Hours",
					 "ParticAddedToSequence", "ParticFirstOutreachCompleted", "ParticReplies", "ParticEmailBounces", "ParticOptsOut"],
					"Id", reference, 1);
				RequireVisibleRows(sections, "selected-ruleset", "Selected ruleset was not found or is not visible.");
			} else {
				sections["selected-schedule"] = ReadRows("DeliverySchedule",
					["Id", "Name", "DefaultTimeZone", "UseProspectTimezone", "UseSenderTimezone", "HolidaysByCountry"],
					"Id", reference, 1);
				RequireVisibleRows(sections, "selected-schedule", "Selected schedule was not found or is not visible.");
				sections["schedule-slots"] = ReadRows("DeliveryScheduleSlot",
					["Id", "DayOfWeek", "TimeFrom", "TimeTo"], "DeliverySchedule", reference);
				if (sections["schedule-slots"].Data is JsonElement[] { Length: 0 }) {
					sections["schedule-slots"] = new("missing", Array.Empty<JsonElement>(), "Configure delivery slots.");
				}
			}
		}
	}

	private static void RequireVisibleRows(Dictionary<string, SequenceContextSection> sections, string key, string message) {
		if (sections[key].State == "complete" && sections[key].Data is JsonElement[] { Length: 0 }) {
			sections[key] = sections[key] with { State = "missing", Error = message };
		}
	}

	private static SequenceContextSection Failure(Exception exception) =>
		new("failed", null, SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message));

	private static SequenceContextResult Result(string availability,
		IReadOnlyDictionary<string, SequenceContextSection> sections) => new(
		availability == "present" && sections.Values.All(section => section.State == "complete"), availability, sections,
		["Metadata presence does not prove permission to activate, enroll or send email.",
		 "Required fields describe schema metadata, not dynamic rules or defaults.",
		 "Choices are capped at 100 rows; use targeted execute-esq reads for truncated sections.",
		 "Use native sequence enrollment; do not create Active participants or their activities manually."]);
}
