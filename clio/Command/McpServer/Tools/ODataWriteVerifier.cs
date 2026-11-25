using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Reads written OData records back and reports every sent field Creatio stored with a different value.
/// </summary>
/// <remarks>
/// An entity rule can replace a value during the save: Creatio accepts the write, answers it as a success and
/// stores something else (ENG-107611 / clio#1995: <c>ForecastSheet.ForecastEntityId</c> follows the sheet's
/// hierarchy). The write answer cannot show it - a PATCH is answered with 204 and no body - so the stored values
/// are read with a GET right after the write. The read runs once, immediately: a value changed later by an
/// asynchronous business process is not seen. A failed read never fails the write; it is reported as
/// <see cref="ODataWriteVerification.Verified"/> <see langword="false"/> with the reason.
/// </remarks>
public interface IODataWriteVerifier {

	/// <summary>Reads one record by key and compares the fields of <paramref name="sent"/> with the stored values.</summary>
	/// <param name="client">The environment-scoped client the write used.</param>
	/// <param name="urlBuilder">The URL builder of the same environment snapshot.</param>
	/// <param name="entity">The OData entity set name.</param>
	/// <param name="id">The record GUID.</param>
	/// <param name="sent">The JSON object of field/value pairs that was written.</param>
	/// <param name="propertyTypes">The entity's property name to Edm type map, or <see langword="null"/> when unknown.</param>
	/// <returns>The outcome of the read-back.</returns>
	ODataWriteVerification VerifyRecord(IApplicationClient client, IServiceUrlBuilder urlBuilder, string entity,
		string id, JsonElement sent, IReadOnlyDictionary<string, string>? propertyTypes);

	/// <summary>
	/// Reads several records of one entity by their Ids, in chunks that fit the URL and the remaining time budget,
	/// and compares each record's sent fields with the stored values.
	/// </summary>
	/// <param name="client">The environment-scoped client the writes used.</param>
	/// <param name="urlBuilder">The URL builder of the same environment snapshot.</param>
	/// <param name="entity">The OData entity set name.</param>
	/// <param name="records">The written records: each Id with the JSON object that was sent for it.</param>
	/// <param name="propertyTypes">The entity's property name to Edm type map, or <see langword="null"/> when unknown.</param>
	/// <param name="remainingBudgetMs">Milliseconds left for the whole call; a chunk is not read below one second.</param>
	/// <param name="cancellationToken">Caller token; once cancelled, no further chunk is read.</param>
	/// <returns>One outcome per record, keyed by its trimmed Id (case-insensitive).</returns>
	IReadOnlyDictionary<string, ODataWriteVerification> VerifyRecords(IApplicationClient client,
		IServiceUrlBuilder urlBuilder, string entity, IReadOnlyList<ODataSentRecord> records,
		IReadOnlyDictionary<string, string>? propertyTypes, Func<int> remainingBudgetMs,
		CancellationToken cancellationToken = default);
}

/// <summary>A written record: its Id and the JSON object that was sent for it.</summary>
/// <param name="Id">The record GUID.</param>
/// <param name="Sent">The JSON object of field/value pairs that was written.</param>
public sealed record ODataSentRecord(string Id, JsonElement Sent);

/// <summary>One sent field whose stored value differs from the sent one.</summary>
/// <param name="Field">The field name as sent.</param>
/// <param name="Sent">The value that was sent.</param>
/// <param name="Stored">The value Creatio returned for it after the write.</param>
public sealed record ODataFieldDivergence(
	[property: JsonPropertyName("field")]
	[property: Description("The field name as sent.")]
	string Field,
	[property: JsonPropertyName("sent")]
	[property: Description("The value that was sent.")]
	JsonElement Sent,
	[property: JsonPropertyName("stored")]
	[property: Description("The value Creatio stored, read back right after the write.")]
	JsonElement Stored);

/// <summary>Outcome of reading a written record back.</summary>
/// <param name="Verified">Whether the stored values were read and compared.</param>
/// <param name="Diverged">The sent fields stored with a different value; empty when every compared field matched.</param>
/// <param name="Note">Why the record was not verified, or which sent fields were not compared; otherwise <see langword="null"/>.</param>
public sealed record ODataWriteVerification(bool Verified, IReadOnlyList<ODataFieldDivergence> Diverged, string? Note) {

	/// <summary>An unverified outcome with its reason.</summary>
	/// <param name="reason">Why the stored values were not compared.</param>
	public static ODataWriteVerification Unverified(string reason) => new(false, [], reason);

	/// <summary>A verified outcome for a write that sent no field at all, so nothing could diverge.</summary>
	public static ODataWriteVerification NothingToCompare { get; } = new(true, [], null);

	/// <summary>An unverified outcome for a read-back that threw; the exception text is redacted.</summary>
	/// <param name="exception">What the read-back threw.</param>
	public static ODataWriteVerification ReadBackFailed(Exception exception) =>
		Unverified("the read-back after the write failed (" + SensitiveErrorTextRedactor.Redact(exception.Message)
			+ "), so the stored values were not compared.");

	/// <summary>The divergences for a response member: <see langword="null"/> when there are none, so the member is omitted.</summary>
	public IReadOnlyList<ODataFieldDivergence>? DivergedOrNull => Diverged.Count == 0 ? null : Diverged;

	/// <summary>The warning for a write whose read-back found divergences, or <see langword="null"/> when there are none.</summary>
	public string? DivergenceWarning => Diverged.Count == 0
		? null
		: "Creatio accepted the write but stored a different value for "
			+ string.Join(", ", Diverged.Select(divergence => $"'{divergence.Field}'"))
			+ " (see 'diverged'): an entity rule most likely replaced it during the save. Re-sending the same write "
			+ "will not change it; decide whether the stored value is acceptable or what else the rule depends on.";

	/// <summary>The warning for a batch in which <paramref name="rows"/> created rows diverged.</summary>
	/// <param name="rows">The number of created rows with at least one divergence.</param>
	public static string BatchDivergenceWarning(int rows) =>
		$"{rows} created row(s) were stored with a different value than sent for at least one field (see each row's "
		+ "'diverged'): an entity rule most likely replaced it during the save. Re-sending such a row does not change "
		+ "the stored value and creates a duplicate.";
}

/// <inheritdoc />
public sealed class ODataWriteVerifier : IODataWriteVerifier {

	/// <summary>Timeout of one read-back GET. The write already happened, so a slow read only costs the check.</summary>
	internal const int ReadBackTimeoutMs = 10_000;

	/// <summary>Smallest remaining budget a chunk is still read with.</summary>
	internal const int MinReadBudgetMs = 1_000;

	/// <summary>
	/// Longest query string a chunk builds. IIS refuses a query string over 2048 characters by default
	/// (<c>maxQueryString</c>), so chunks stay under it with room for the path.
	/// </summary>
	internal const int MaxQueryLength = 1_800;

	/// <summary>A <c>$select</c> longer than this is dropped from a batched read, so that ids still fit the query.</summary>
	private const int MaxSelectLength = MaxQueryLength / 2;

	private const string UnreadableAnswer =
		"the read-back after the write returned no usable answer, so the stored values were not compared.";

	private const string ServerError =
		"the read-back after the write returned an error (for example the record is hidden by access rights), "
		+ "so the stored values were not compared.";

	private const string NotReturned =
		"the read-back after the write did not return this record (for example it is hidden by access rights), "
		+ "so the stored values were not compared.";

	private const string BudgetSpent =
		"the batch time budget was spent before this record could be read back, so the stored values were not compared.";

	private const string Cancelled =
		"the call was cancelled before this record could be read back, so the stored values were not compared.";

	private const string NoRecordId =
		"Creatio did not return a GUID Id for the created record, so it could not be read back.";

	/// <inheritdoc />
	public ODataWriteVerification VerifyRecord(IApplicationClient client, IServiceUrlBuilder urlBuilder,
		string entity, string id, JsonElement sent, IReadOnlyDictionary<string, string>? propertyTypes) {
		try {
			FieldPlan plan = PlanFields(sent, propertyTypes);
			if (plan.Compared.Count == 0) {
				return plan.WithoutComparableFields();
			}
			string url = urlBuilder.Build(ODataKeyFormatter.KeyPath(entity, id))
				+ "?$select=" + Uri.EscapeDataString(string.Join(",", plan.Compared));
			return TryRead(client, url, ReadBackTimeoutMs, out JsonElement root, out string failure)
				? Compare(sent, root, plan, propertyTypes)
				: ODataWriteVerification.Unverified(failure);
		} catch (Exception ex) {
			// The write already happened: nothing on the read-back path may surface as a failed write.
			return ODataWriteVerification.ReadBackFailed(ex);
		}
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<string, ODataWriteVerification> VerifyRecords(IApplicationClient client,
		IServiceUrlBuilder urlBuilder, string entity, IReadOnlyList<ODataSentRecord> records,
		IReadOnlyDictionary<string, string>? propertyTypes, Func<int> remainingBudgetMs,
		CancellationToken cancellationToken = default) {
		Dictionary<string, ODataWriteVerification> outcomes = new(StringComparer.OrdinalIgnoreCase);
		try {
			VerifyRecordsCore(client, urlBuilder, entity, records, propertyTypes, remainingBudgetMs, cancellationToken,
				outcomes);
		} catch (Exception ex) {
			ODataWriteVerification failed = ODataWriteVerification.ReadBackFailed(ex);
			foreach (ODataSentRecord record in records.Where(record => record.Id is not null)) {
				outcomes.TryAdd(record.Id.Trim(), failed);
			}
		}
		return outcomes;
	}

	/// <summary>The sent fields a read-back compares, and the sent fields it cannot compare.</summary>
	private sealed record FieldPlan(IReadOnlyList<string> Compared, IReadOnlyList<string> Skipped) {

		/// <summary>The note naming the skipped fields, or <see langword="null"/> when every sent field is compared.</summary>
		public string? SkippedNote => Skipped.Count == 0
			? null
			: string.Join(", ", Skipped.Select(field => $"'{field}'"))
				+ (Skipped.Count == 1 ? " was" : " were")
				+ " not compared: a lookup binding (@odata.bind), a navigation path or a binary column cannot be read back.";

		/// <summary>The outcome when no sent field can be compared: unverified with the reason, unless nothing was sent.</summary>
		public ODataWriteVerification WithoutComparableFields() =>
			Skipped.Count == 0 ? ODataWriteVerification.NothingToCompare : ODataWriteVerification.Unverified(SkippedNote!);
	}

	/// <summary>A record to read back, with its fields worked out once.</summary>
	private sealed record PendingRecord(string Id, JsonElement Sent, FieldPlan Plan);

	private static void VerifyRecordsCore(IApplicationClient client, IServiceUrlBuilder urlBuilder, string entity,
		IReadOnlyList<ODataSentRecord> records, IReadOnlyDictionary<string, string>? propertyTypes,
		Func<int> remainingBudgetMs, CancellationToken cancellationToken,
		Dictionary<string, ODataWriteVerification> outcomes) {
		List<PendingRecord> pending = [];
		foreach (ODataSentRecord record in records) {
			string id = record.Id?.Trim() ?? string.Empty;
			FieldPlan plan = PlanFields(record.Sent, propertyTypes);
			// The Id goes into $filter verbatim, so only a GUID is read back; anything else cannot address a record.
			if (!ODataKeyFormatter.IsGuid(id)) {
				outcomes[id] = ODataWriteVerification.Unverified(NoRecordId);
			} else if (plan.Compared.Count == 0) {
				outcomes[id] = plan.WithoutComparableFields();
			} else {
				pending.Add(new PendingRecord(id, record.Sent, plan));
			}
		}
		string collectionUrl = urlBuilder.Build(ODataKeyFormatter.CollectionPath(entity));
		foreach (List<PendingRecord> chunk in Chunk(pending)) {
			int remaining = remainingBudgetMs();
			if (cancellationToken.IsCancellationRequested || remaining < MinReadBudgetMs) {
				string reason = cancellationToken.IsCancellationRequested ? Cancelled : BudgetSpent;
				chunk.ForEach(record => outcomes[record.Id] = ODataWriteVerification.Unverified(reason));
				continue;
			}
			string url = collectionUrl + "?" + BuildChunkQuery(chunk);
			if (!TryRead(client, url, Math.Min(ReadBackTimeoutMs, remaining), out JsonElement root, out string failure)) {
				chunk.ForEach(record => outcomes[record.Id] = ODataWriteVerification.Unverified(failure));
				continue;
			}
			Dictionary<Guid, JsonElement> stored = StoredById(root);
			foreach (PendingRecord record in chunk) {
				outcomes[record.Id] = stored.TryGetValue(Guid.Parse(record.Id), out JsonElement row)
					? Compare(record.Sent, row, record.Plan, propertyTypes)
					: ODataWriteVerification.Unverified(NotReturned);
			}
		}
	}

	/// <summary>
	/// Splits the sent fields, in sent order, into those a read-back can compare - plain property names that are
	/// not binary - and the rest: an <c>@odata.bind</c> annotation, a navigation path or a binary column.
	/// </summary>
	private static FieldPlan PlanFields(JsonElement sent, IReadOnlyDictionary<string, string>? propertyTypes) {
		if (sent.ValueKind != JsonValueKind.Object) {
			return new FieldPlan([], []);
		}
		List<string> compared = [];
		List<string> skipped = [];
		foreach (string name in sent.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal)) {
			(ODataKeyFormatter.IsSimpleIdentifier(name) && ODataStoredValueComparer.IsComparable(TypeOf(name, propertyTypes))
				? compared
				: skipped).Add(name);
		}
		return new FieldPlan(compared, skipped);
	}

	private static string? TypeOf(string field, IReadOnlyDictionary<string, string>? propertyTypes) =>
		propertyTypes is not null && propertyTypes.TryGetValue(field, out string? type) ? type : null;

	private const string FilterPrefix = "$filter=";
	private const string SelectPrefix = "&$select=";
	private const string IdTerm = "Id eq ";
	private const string OrSeparator = " or ";
	private const string SelectSeparator = ",";

	/// <summary>
	/// Splits the records into chunks whose query (see <see cref="BuildChunkQuery"/>) fits
	/// <see cref="MaxQueryLength"/>, tracking the escaped length as records are added instead of rebuilding it.
	/// </summary>
	private static IEnumerable<List<PendingRecord>> Chunk(IReadOnlyList<PendingRecord> records) {
		List<PendingRecord> current = [];
		HashSet<string> selected = new(StringComparer.Ordinal) { "Id" };
		int filterLength = 0;
		int selectLength = EscapedLength("Id");
		foreach (PendingRecord record in records) {
			int idLength = EscapedLength(IdTerm + record.Id) + (current.Count == 0 ? 0 : EscapedLength(OrSeparator));
			int addedSelect = SelectLengthOf(record.Plan.Compared.Where(field => !selected.Contains(field)));
			if (current.Count > 0
				&& QueryLength(filterLength + idLength, selectLength + addedSelect) > MaxQueryLength) {
				yield return current;
				current = [];
				selected = new HashSet<string>(StringComparer.Ordinal) { "Id" };
				filterLength = 0;
				selectLength = EscapedLength("Id");
				idLength = EscapedLength(IdTerm + record.Id);
				addedSelect = SelectLengthOf(record.Plan.Compared);
			}
			current.Add(record);
			selected.UnionWith(record.Plan.Compared);
			filterLength += idLength;
			selectLength += addedSelect;
		}
		if (current.Count > 0) {
			yield return current;
		}
	}

	private static int EscapedLength(string text) => Uri.EscapeDataString(text).Length;

	private static int SelectLengthOf(IEnumerable<string> fields) =>
		fields.Distinct(StringComparer.Ordinal).Sum(field => EscapedLength(SelectSeparator) + EscapedLength(field));

	private static int QueryLength(int filterLength, int selectLength) =>
		FilterPrefix.Length + filterLength + (selectLength <= MaxSelectLength ? SelectPrefix.Length + selectLength : 0);

	/// <summary>
	/// <c>$filter</c> on the chunk's Ids plus a <c>$select</c> of the union of their fields and <c>Id</c>; the
	/// <c>$select</c> is left out when it alone would crowd the Ids out of the query, so every column is read.
	/// </summary>
	private static string BuildChunkQuery(IReadOnlyList<PendingRecord> chunk) {
		string filter = string.Join(OrSeparator, chunk.Select(record => IdTerm + record.Id));
		StringBuilder query = new StringBuilder(FilterPrefix).Append(Uri.EscapeDataString(filter));
		string select = Uri.EscapeDataString(string.Join(SelectSeparator,
			new[] { "Id" }.Concat(chunk.SelectMany(record => record.Plan.Compared)).Distinct(StringComparer.Ordinal)));
		if (select.Length <= MaxSelectLength) {
			query.Append(SelectPrefix).Append(select);
		}
		return query.ToString();
	}

	/// <summary>Sends one read-back GET and returns its JSON root, or the reason it is unusable.</summary>
	private static bool TryRead(IApplicationClient client, string url, int timeoutMs, out JsonElement root,
		out string failure) {
		root = default;
		failure = null!;
		string body;
		try {
			body = client.ExecuteGetRequest(url, timeoutMs, 1);
		} catch (Exception ex) {
			failure = ODataWriteVerification.ReadBackFailed(ex).Note!;
			return false;
		}
		if (string.IsNullOrWhiteSpace(body)) {
			failure = UnreadableAnswer;
			return false;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			// The server's error text is not echoed: it is server-authored prose, and the record identity is enough
			// for the caller to read the record itself.
			if (document.RootElement.ValueKind != JsonValueKind.Object
				|| CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload, out _)) {
				failure = ServerError;
				return false;
			}
			root = document.RootElement.Clone();
			return true;
		} catch (JsonException) {
			failure = UnreadableAnswer;
			return false;
		}
	}

	/// <summary>The rows of a collection answer, keyed by their Id.</summary>
	private static Dictionary<Guid, JsonElement> StoredById(JsonElement root) {
		Dictionary<Guid, JsonElement> rows = [];
		if (!root.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Array) {
			return rows;
		}
		foreach (JsonElement row in value.EnumerateArray()) {
			if (row.ValueKind == JsonValueKind.Object && TryGetProperty(row, "Id", out JsonElement id)
				&& id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out Guid key)) {
				rows[key] = row;
			}
		}
		return rows;
	}

	/// <summary>Compares each sent field with its stored value.</summary>
	private static ODataWriteVerification Compare(JsonElement sent, JsonElement stored, FieldPlan plan,
		IReadOnlyDictionary<string, string>? propertyTypes) {
		List<ODataFieldDivergence> diverged = [];
		List<string> notReturned = [];
		foreach (string field in plan.Compared) {
			JsonElement sentValue = sent.GetProperty(field);
			if (!TryGetProperty(stored, field, out JsonElement storedValue)) {
				notReturned.Add(field);
				continue;
			}
			if (!ODataStoredValueComparer.AreEquivalent(sentValue, storedValue, TypeOf(field, propertyTypes))) {
				diverged.Add(new ODataFieldDivergence(field, sentValue.Clone(), storedValue.Clone()));
			}
		}
		string? notReturnedNote = notReturned.Count == 0
			? null
			: "The read-back did not return " + string.Join(", ", notReturned.Select(field => $"'{field}'"))
				+ ", so " + (notReturned.Count == 1 ? "it was" : "they were") + " not compared.";
		string?[] notes = [notReturnedNote, plan.SkippedNote];
		string? note = notes.Any(part => part is not null) ? string.Join(" ", notes.Where(part => part is not null)) : null;
		return new ODataWriteVerification(true, diverged, note);
	}

	/// <summary>Reads a property by its exact name, then by a case-insensitive match.</summary>
	private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
		if (element.TryGetProperty(name, out value)) {
			return true;
		}
		foreach (JsonProperty property in element.EnumerateObject()) {
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
				value = property.Value;
				return true;
			}
		}
		value = default;
		return false;
	}
}
