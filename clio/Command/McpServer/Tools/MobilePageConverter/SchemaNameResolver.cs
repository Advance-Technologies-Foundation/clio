using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Batched <c>SysSchema</c> UId→NAME resolver — the one place every by-UId schema-name lookup in this probe
/// goes through, whether it resolves many UIds in one call (<c>ResolveCandidateNames</c>, which previously ran
/// the chunked <see cref="ClassicEntitySchemaQuery.BuildSelectSchemaNamesByUId"/> select directly) or a single
/// UId (the existing-mobile-page checks, which previously ran
/// <c>PageSchemaMetadataHelper.QuerySysSchemaRowByUId</c> — a different query answering the same question).
/// </summary>
internal static class SchemaNameResolver {

	/// <summary>
	/// Distinguishes "no such row" from "a row exists but its name is empty or invalid" — the distinction the
	/// batched candidate-resolution note relies on to tell a dangling reference (a UId the add-on still names
	/// but the object no longer exists) apart from an object that simply has nothing offerable.
	/// </summary>
	internal enum Status { RowMissing, NameInvalid, Resolved }

	/// <param name="Status">Which of the three outcomes this UId settled into.</param>
	/// <param name="Name">
	/// The resolved schema name when <paramref name="Status"/> is <see cref="Status.Resolved"/>;
	/// <see langword="null"/> otherwise.
	/// </param>
	internal readonly record struct Result(Status Status, string Name);

	/// <summary>
	/// Resolves every UId in <paramref name="uIds"/> to its <c>SysSchema</c> name in one chunked select.
	/// The row cap requests <see cref="MobileActionTargetProbe.RowsPerNameHeadroom"/> rows per distinct UId,
	/// not a bare 1:1 count: a <c>SysSchema</c> UId is not guaranteed row-unique the way a fresh single-row
	/// lookup would be — like a Name, a UId can carry a base row plus a replacing layer per package — and an
	/// unordered result caps arbitrarily, so a chunk with even one duplicated UId could otherwise push a
	/// sibling UId's row out of the window and misreport it as <see cref="Status.RowMissing"/>. This mirrors
	/// the identical fix already applied to the by-Name read in
	/// <see cref="MobileActionTargetProbe.ReadEntitySchemaRows"/>. Unlike that read, no explicit ordering is
	/// needed here: every row sharing a UId names the same schema, so whichever row survives the cap carries
	/// the same <c>Name</c>. The query is built locally (<see cref="BuildSelectNamesByUIdWithHeadroom"/>)
	/// rather than through <see cref="ClassicEntitySchemaQuery.BuildSelectSchemaNamesByUId"/> so this probe's
	/// headroom decision cannot change that shared helper's row cap for its other two callers (a section
	/// module, a detail's edit card), which still assume one row per distinct UId. Propagates a
	/// transport/query failure to the caller rather than swallowing it: a caller batching many UIds in one
	/// call needs the actual exception to compose its own degradation note, which this resolver has no way
	/// to phrase on its callers' behalf.
	/// </summary>
	internal static IReadOnlyDictionary<Guid, Result> ResolveNames(
		MobileActionTargetProbe.ProbeContext context, IReadOnlyList<Guid> uIds) {
		var results = new Dictionary<Guid, Result>();
		if (uIds.Count == 0) {
			return results;
		}
		string[] distinct = [.. uIds.Select(u => u.ToString()).Distinct(StringComparer.OrdinalIgnoreCase)];
		var nameByUId = new Dictionary<Guid, string>();
		foreach (IReadOnlyList<string> chunk in Chunk(distinct)) {
			int requestedRows = chunk.Count * MobileActionTargetProbe.RowsPerNameHeadroom;
			JArray rows = ClassicEntitySchemaQuery.Select(
				context.Client, context.UrlBuilder, BuildSelectNamesByUIdWithHeadroom(chunk, requestedRows));
			foreach (JToken row in rows) {
				if (Guid.TryParse(row["UId"]?.ToString(), out Guid rowUId)) {
					nameByUId[rowUId] = row["Name"]?.ToString();
				}
			}
		}
		foreach (Guid uId in uIds.Distinct()) {
			results[uId] = ResolveFromRow(nameByUId, uId);
		}
		return results;
	}

	/// <summary>
	/// Byte-identical column set and <c>In</c>-filter shape to
	/// <see cref="ClassicEntitySchemaQuery.BuildSelectSchemaNamesByUId"/>, with an explicit <paramref
	/// name="rowCount"/> instead of that helper's implicit "one row per distinct UId" assumption. Kept as a
	/// local copy — scoped to this mobile-converter probe only — rather than widening the shared helper's
	/// signature, so this row-cap decision can never affect its other two, unrelated callers.
	/// </summary>
	private static JObject BuildSelectNamesByUIdWithHeadroom(IReadOnlyCollection<string> uIds, int rowCount) =>
		ClassicEntitySchemaQuery.Query(
			"SysSchema",
			new JObject {
				["UId"] = ClassicEntitySchemaQuery.Column("UId"), ["Name"] = ClassicEntitySchemaQuery.Column("Name")
			},
			ClassicEntitySchemaQuery.Group(("byUId", ClassicEntitySchemaQuery.InFilter("UId", uIds, 0))),
			rowCount);

	/// <summary>Settles one UId's <see cref="Result"/> from the batched <c>SysSchema</c> read.</summary>
	private static Result ResolveFromRow(IReadOnlyDictionary<Guid, string> nameByUId, Guid uId) {
		if (!nameByUId.TryGetValue(uId, out string name)) {
			return new Result(Status.RowMissing, null);
		}
		return !string.IsNullOrEmpty(name) && PageSchemaMetadataHelper.IsValidSchemaName(name)
			? new Result(Status.Resolved, name)
			: new Result(Status.NameInvalid, null);
	}

	/// <summary>
	/// Single-UId convenience over <see cref="ResolveNames"/> for the two existing-mobile-page lookups. Never
	/// throws: a transport/query failure reads the same as <see cref="Status.RowMissing"/>, matching what
	/// <c>PageSchemaMetadataHelper.QuerySysSchemaRowByUId</c>'s error branch collapsed to before this resolver
	/// existed — both call sites only ever acted on "found a usable name" versus "did not", never on why.
	/// </summary>
	internal static Result ResolveName(MobileActionTargetProbe.ProbeContext context, Guid uId) {
		try {
			IReadOnlyDictionary<Guid, Result> results = ResolveNames(context, [uId]);
			return results.TryGetValue(uId, out Result result) ? result : new Result(Status.RowMissing, null);
		} catch (Exception) {
			return new Result(Status.RowMissing, null);
		}
	}

	/// <summary>
	/// Splits <paramref name="values"/> into ESQ-safe batches. Every <c>IN</c> value costs a query parameter
	/// and MSSql caps a statement at 2100, so a page-driven value set must be chunked
	/// (<see cref="ClassicEntitySchemaQuery.InFilterChunkSize"/>) or the whole query throws.
	/// </summary>
	private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> values) {
		for (int start = 0; start < values.Count; start += ClassicEntitySchemaQuery.InFilterChunkSize) {
			yield return values
				.Skip(start)
				.Take(ClassicEntitySchemaQuery.InFilterChunkSize)
				.ToArray();
		}
	}
}
