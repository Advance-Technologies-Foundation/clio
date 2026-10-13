using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Common.EntitySchema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// Finds the records an action created in a set of watched entities, by diffing each entity before and after it.
/// </summary>
/// <remarks>
/// The platform keeps no list of the records a process run created: <c>SysProcessEntity</c> is empty unless a
/// process opts in, <c>SysProcessElementLog</c> carries no record ids, and an element's data row is deleted when the
/// element completes. A before/after diff is therefore the only general source, and it reports any record created
/// in the window - by this action, by what it triggered, or by another user at the same moment. The baseline is
/// the newest existing CreatedOn, so a row dated in the future hides what the action creates until that time.
/// </remarks>
public interface ICreatedRecordWatcher {

	/// <summary>Captures, per entity, the newest CreatedOn and the ids at that instant, before the action.</summary>
	/// <param name="entities">Entity schema names to watch.</param>
	/// <param name="timeoutMs">Bound of each request, in milliseconds.</param>
	/// <param name="stopAt">An entity not reached by this instant is left unwatched, with an error saying so.</param>
	/// <returns>The baseline; an entity that could not be read carries <see cref="EntityWatch.Error"/>.</returns>
	WatchBaseline Capture(IReadOnlyList<string> entities, int timeoutMs, DateTimeOffset? stopAt = null);

	/// <summary>Reads the records created after <paramref name="baseline"/>.</summary>
	/// <param name="baseline">What <see cref="Capture"/> returned.</param>
	/// <param name="timeoutMs">Bound of each request, in milliseconds.</param>
	/// <param name="stopAt">No request runs past this instant; an entity not read by then is reported unread.</param>
	/// <returns>The created records and a warning per entity that could not be read in full.</returns>
	WatchOutcome Collect(WatchBaseline baseline, int timeoutMs, DateTimeOffset? stopAt = null);
}

internal sealed class CreatedRecordWatcher(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IRuntimeEntitySchemaReader schemaReader)
	: ICreatedRecordWatcher {

	/// <summary>Most records reported per entity; more is reported as a truncation warning.</summary>
	internal const int MaxCreatedPerEntity = 100;

	// Ids sharing the newest CreatedOn before the action. A bulk import can stamp more; the entity is then not watched,
	// because a pre-existing row past the cap would be reported as created - and a cleanup would delete it.
	private const int MaxKnownIds = 500;

	private const string CreatedOnColumn = "CreatedOn";
	private const string ExpressionTypeKey = "expressionType";
	private const string ColumnPathKey = "columnPath";
	// Aliased so it can never collide with a title column called CreatedBy.
	private const string CreatedByAlias = "ClioCreatedBy";

	// Terrasoft FunctionType.Macros and QueryMacrosType.CurrentUserContact.
	private const int MacrosFunctionType = 1;
	private const int CurrentUserContactMacro = 2;
	private const int DateTimeDataValueType = 7;
	private const int GreaterOrEqual = 8;
	private const int OrderAscending = 1;
	private const int OrderDescending = 2;

	private static readonly Regex EntityName = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z",
		RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

	/// <summary>Whether <paramref name="name"/> is a plain entity schema name.</summary>
	internal static bool IsValidEntityName(string name) => name is not null && EntityName.IsMatch(name);

	/// <inheritdoc />
	public WatchBaseline Capture(IReadOnlyList<string> entities, int timeoutMs, DateTimeOffset? stopAt = null) {
		// Read first and inside the same budget, so it cannot push the launch past the capture bound.
		string currentUserContactId = ReadCurrentUserContactId(Budget(timeoutMs, stopAt));
		List<EntityWatch> watches = [];
		// Schema names resolve case-insensitively, so "Activity" and "activity" are one table, watched once.
		foreach (string entity in entities.Distinct(StringComparer.OrdinalIgnoreCase)) {
			watches.Add(stopAt is not null && DateTimeOffset.UtcNow >= stopAt
				? new EntityWatch { Entity = entity, Error = "the baseline read ran out of time before the launch" }
				: CaptureOne(entity, timeoutMs, stopAt));
		}
		return new WatchBaseline { Entities = watches, CurrentUserContactId = currentUserContactId };
	}

	// Each request gets timeoutMs, but never past stopAt.
	internal static int Budget(int timeoutMs, DateTimeOffset? stopAt) {
		int bound = Math.Max(1, timeoutMs);
		return stopAt is null
			? bound
			: (int)Math.Clamp((stopAt.Value - DateTimeOffset.UtcNow).TotalMilliseconds, 1, bound);
	}

	/// <inheritdoc />
	public WatchOutcome Collect(WatchBaseline baseline, int timeoutMs, DateTimeOffset? stopAt = null) {
		WatchOutcome outcome = new();
		foreach (EntityWatch watch in baseline.Entities) {
			if (watch.Error is not null) {
				outcome.Warnings.Add($"'{watch.Entity}' was not watched: {watch.Error}");
				continue;
			}
			if (stopAt is not null && DateTimeOffset.UtcNow >= stopAt) {
				outcome.Warnings.Add($"'{watch.Entity}' was not read after the run: the response deadline was reached, "
					+ "so what the run created in it is not reported.");
				continue;
			}
			try {
				JArray rows = Select(BuildRowsQuery(watch, MaxCreatedPerEntity + watch.KnownIds.Count + 1, true),
					Budget(timeoutMs, stopAt));
				List<CreatedRecord> created = rows
					.Where(row => !watch.KnownIds.Contains(row["Id"]?.ToString() ?? string.Empty))
					.Select(row => new CreatedRecord(watch.Entity, row["Id"]?.ToString(), ReadTitle(row, watch.TitleColumn)) {
						CreatedOn = row[CreatedOnColumn]?.ToString(),
						CreatedById = ReadLookupId(row[CreatedByAlias])
					})
					.ToList();
				if (created.Count > MaxCreatedPerEntity) {
					outcome.Warnings.Add($"'{watch.Entity}' had more than {MaxCreatedPerEntity} new records; only the "
						+ $"first {MaxCreatedPerEntity} are listed.");
					created = created.Take(MaxCreatedPerEntity).ToList();
				}
				outcome.Created.AddRange(created);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				outcome.Warnings.Add($"'{watch.Entity}' could not be read after the run: "
					+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name));
			}
		}
		return outcome;
	}

	private EntityWatch CaptureOne(string entity, int timeoutMs, DateTimeOffset? stopAt) {
		if (!IsValidEntityName(entity)) {
			return new EntityWatch { Entity = entity, Error = "not a plain entity schema name" };
		}
		try {
			RuntimeEntitySchemaResult schema = schemaReader.GetByName(entity, Budget(timeoutMs, stopAt));
			string titleColumn = schema.PrimaryDisplayColumnName;
			HashSet<string> references = schema.Columns
				.Select(column => column.ReferenceSchemaName)
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.ToHashSet(StringComparer.Ordinal);
			string newest = Select(BuildNewestQuery(entity), Budget(timeoutMs, stopAt)).FirstOrDefault()?[CreatedOnColumn]
				?.ToString();
			// An empty value is no lower bound: comparing against it could report every existing row as created.
			newest = string.IsNullOrEmpty(newest) ? null : newest;
			HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);
			if (newest is not null) {
				EntityWatch atNewest = new() { Entity = entity, TitleColumn = titleColumn, Baseline = newest };
				JArray atNewestRows = Select(BuildRowsQuery(atNewest, MaxKnownIds + 1, false), Budget(timeoutMs, stopAt));
				if (atNewestRows.Count > MaxKnownIds) {
					return new EntityWatch {
						Entity = entity,
						Error = $"more than {MaxKnownIds} existing rows share its newest CreatedOn, so new rows cannot be "
							+ "told apart from them"
					};
				}
				foreach (JToken row in atNewestRows) {
					known.Add(row["Id"]?.ToString() ?? string.Empty);
				}
			}
			return new EntityWatch {
				Entity = entity,
				TitleColumn = titleColumn,
				References = references,
				Baseline = newest,
				KnownIds = known
			};
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return new EntityWatch {
				Entity = entity,
				Error = UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name
			};
		}
	}

	// Rows with no CreatedOn are left out: PostgreSQL sorts NULL first in a descending order, which would make the
	// baseline empty instead of the newest timestamp.
	internal static JObject BuildNewestQuery(string entity) {
		JObject columns = new() {
			[CreatedOnColumn] = new JObject {
				["expression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = CreatedOnColumn },
				["orderDirection"] = OrderDescending,
				["orderPosition"] = 0
			}
		};
		return ClassicEntitySchemaQuery.Query(entity, columns,
			ClassicEntitySchemaQuery.Group(("hasCreatedOn", CreatedOnIsNotNull())), 1);
	}

	// filterType 2 (IsNullFilter), comparisonType 2 (IsNotNull).
	private static JObject CreatedOnIsNotNull() => new() {
		["filterType"] = 2, ["comparisonType"] = 2, ["isNull"] = false, ["isEnabled"] = true,
		["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = CreatedOnColumn }
	};

	// Rows at or after the baseline (every row when the table was empty), oldest first.
	internal static JObject BuildRowsQuery(EntityWatch watch, int rowCount, bool withTitle) {
		JObject columns = new() {
			["Id"] = ClassicEntitySchemaQuery.Column("Id"),
			[CreatedOnColumn] = new JObject {
				["expression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = CreatedOnColumn },
				["orderDirection"] = OrderAscending,
				["orderPosition"] = 0
			}
		};
		if (withTitle) {
			columns[CreatedByAlias] = ClassicEntitySchemaQuery.Column("CreatedBy");
		}
		if (withTitle && !string.IsNullOrWhiteSpace(watch.TitleColumn)
				&& !string.Equals(watch.TitleColumn, "Id", StringComparison.Ordinal)
				&& !string.Equals(watch.TitleColumn, CreatedOnColumn, StringComparison.Ordinal)) {
			columns[watch.TitleColumn] = ClassicEntitySchemaQuery.Column(watch.TitleColumn);
		}
		// A row without CreatedOn predates the run (the platform stamps every row it inserts), so it is never reported.
		JObject filters = watch.Baseline is null
			? ClassicEntitySchemaQuery.Group(("hasCreatedOn", CreatedOnIsNotNull()))
			: ClassicEntitySchemaQuery.Group(("hasCreatedOn", CreatedOnIsNotNull()),
				("sinceBaseline", AtOrAfter(watch.Baseline)));
		return ClassicEntitySchemaQuery.Query(watch.Entity, columns, filters, rowCount);
	}

	// The value DataService returned, sent back as a quoted JSON date: DataService reads and writes DateTime
	// parameters in the session time zone, so the round trip compares like with like. An unquoted value is refused.
	private static JObject AtOrAfter(string baseline) => new() {
		["filterType"] = 1, ["comparisonType"] = GreaterOrEqual, ["isEnabled"] = true,
		["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = CreatedOnColumn },
		["rightExpression"] = new JObject {
			[ExpressionTypeKey] = 2,
			["parameter"] = new JObject {
				["dataValueType"] = DateTimeDataValueType,
				["value"] = JsonConvert.SerializeObject(baseline)
			}
		}
	};

	// Best effort: without it a cleanup deletes nothing, which is the safe failure.
	private string ReadCurrentUserContactId(int timeoutMs) {
		try {
			JObject filter = new() {
				["filterType"] = 1, ["comparisonType"] = 3, ["isEnabled"] = true,
				["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Id" },
				["rightExpression"] = new JObject {
					[ExpressionTypeKey] = 1, ["functionType"] = MacrosFunctionType, ["macrosType"] = CurrentUserContactMacro
				}
			};
			JArray rows = Select(ClassicEntitySchemaQuery.Query("Contact",
				new JObject { ["Id"] = ClassicEntitySchemaQuery.Column("Id") },
				ClassicEntitySchemaQuery.Group(("me", filter)), 1), timeoutMs);
			return rows.FirstOrDefault()?["Id"]?.ToString();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return null;
		}
	}

	private static string ReadLookupId(JToken value) => value switch {
		JObject lookup => lookup["value"]?.ToString(),
		null => null,
		_ => value.ToString()
	};

	private static string ReadTitle(JToken row, string titleColumn) {
		if (string.IsNullOrWhiteSpace(titleColumn)) {
			return null;
		}
		JToken value = row[titleColumn];
		return value switch {
			null => null,
			JObject lookup => lookup["displayValue"]?.ToString(),
			_ => value.ToString()
		};
	}

	private JArray Select(JObject query, int timeoutMs) {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string json = applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None),
			Math.Max(1, timeoutMs), maxAttempts: 1);
		return DataServiceSelectResponse.ReadRowsVerbatim(json);
	}
}
