using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
	/// <returns>One outcome per record, keyed by its trimmed Id (case-insensitive).</returns>
	IReadOnlyDictionary<string, ODataWriteVerification> VerifyRecords(IApplicationClient client,
		IServiceUrlBuilder urlBuilder, string entity, IReadOnlyList<ODataSentRecord> records,
		IReadOnlyDictionary<string, string>? propertyTypes, Func<int> remainingBudgetMs);
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

	/// <inheritdoc />
	public ODataWriteVerification VerifyRecord(IApplicationClient client, IServiceUrlBuilder urlBuilder,
		string entity, string id, JsonElement sent, IReadOnlyDictionary<string, string>? propertyTypes) {
		IReadOnlyList<string> fields = ComparableFields(sent, propertyTypes);
		if (fields.Count == 0) {
			return new ODataWriteVerification(true, [], null);
		}
		string url = urlBuilder.Build(ODataKeyFormatter.KeyPath(entity, id))
			+ "?$select=" + Uri.EscapeDataString(string.Join(",", fields));
		if (!TryRead(client, url, ReadBackTimeoutMs, out JsonElement root, out string failure)) {
			return ODataWriteVerification.Unverified(failure);
		}
		return Compare(sent, root, fields, propertyTypes);
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<string, ODataWriteVerification> VerifyRecords(IApplicationClient client,
		IServiceUrlBuilder urlBuilder, string entity, IReadOnlyList<ODataSentRecord> records,
		IReadOnlyDictionary<string, string>? propertyTypes, Func<int> remainingBudgetMs) {
		Dictionary<string, ODataWriteVerification> outcomes = new(StringComparer.OrdinalIgnoreCase);
		string collectionUrl = urlBuilder.Build(ODataKeyFormatter.CollectionPath(entity));
		// The Id goes into $filter verbatim, so only a GUID is read back; anything else cannot address a record.
		List<ODataSentRecord> addressable = [];
		foreach (ODataSentRecord record in records) {
			if (ODataKeyFormatter.IsGuid(record.Id?.Trim() ?? string.Empty)) {
				addressable.Add(record with { Id = record.Id!.Trim() });
			} else if (record.Id is not null) {
				outcomes[record.Id] = ODataWriteVerification.Unverified(UnreadableAnswer);
			}
		}
		IEnumerable<List<ODataSentRecord>> chunks = Chunk(addressable, propertyTypes,
			out IReadOnlyList<ODataSentRecord> fieldless);
		foreach (List<ODataSentRecord> chunk in chunks) {
			int remaining = remainingBudgetMs();
			if (remaining < MinReadBudgetMs) {
				chunk.ForEach(record => outcomes[record.Id] = ODataWriteVerification.Unverified(BudgetSpent));
				continue;
			}
			string url = collectionUrl + "?" + BuildChunkQuery(chunk, propertyTypes);
			if (!TryRead(client, url, Math.Min(ReadBackTimeoutMs, remaining), out JsonElement root, out string failure)) {
				chunk.ForEach(record => outcomes[record.Id] = ODataWriteVerification.Unverified(failure));
				continue;
			}
			Dictionary<Guid, JsonElement> stored = StoredById(root);
			foreach (ODataSentRecord record in chunk) {
				outcomes[record.Id] = Guid.TryParse(record.Id, out Guid key) && stored.TryGetValue(key, out JsonElement row)
					? Compare(record.Sent, row, ComparableFields(record.Sent, propertyTypes), propertyTypes)
					: ODataWriteVerification.Unverified(NotReturned);
			}
		}
		foreach (ODataSentRecord record in fieldless) {
			outcomes[record.Id] = new ODataWriteVerification(true, [], null);
		}
		return outcomes;
	}

	/// <summary>
	/// The sent fields a read-back can compare: plain property names (an <c>@odata.bind</c> annotation or a
	/// navigation path is skipped) that are not binary, in sent order.
	/// </summary>
	private static IReadOnlyList<string> ComparableFields(JsonElement sent,
		IReadOnlyDictionary<string, string>? propertyTypes) =>
		sent.ValueKind != JsonValueKind.Object
			? []
			: sent.EnumerateObject()
				.Select(property => property.Name)
				.Where(name => ODataKeyFormatter.IsSimpleIdentifier(name)
					&& ODataStoredValueComparer.IsComparable(TypeOf(name, propertyTypes)))
				.Distinct(StringComparer.Ordinal)
				.ToList();

	private static string? TypeOf(string field, IReadOnlyDictionary<string, string>? propertyTypes) =>
		propertyTypes is not null && propertyTypes.TryGetValue(field, out string? type) ? type : null;

	/// <summary>Splits the records into chunks whose query fits <see cref="MaxQueryLength"/>.</summary>
	private static IEnumerable<List<ODataSentRecord>> Chunk(IReadOnlyList<ODataSentRecord> records,
		IReadOnlyDictionary<string, string>? propertyTypes, out IReadOnlyList<ODataSentRecord> fieldless) {
		List<ODataSentRecord> withFields = [];
		List<ODataSentRecord> withoutFields = [];
		foreach (ODataSentRecord record in records) {
			(ComparableFields(record.Sent, propertyTypes).Count > 0 ? withFields : withoutFields).Add(record);
		}
		fieldless = withoutFields;
		List<List<ODataSentRecord>> chunks = [];
		List<ODataSentRecord> current = [];
		foreach (ODataSentRecord record in withFields) {
			List<ODataSentRecord> candidate = [.. current, record];
			if (current.Count > 0 && BuildChunkQuery(candidate, propertyTypes).Length > MaxQueryLength) {
				chunks.Add(current);
				current = [record];
			} else {
				current = candidate;
			}
		}
		if (current.Count > 0) {
			chunks.Add(current);
		}
		return chunks;
	}

	/// <summary>
	/// <c>$filter</c> on the chunk's Ids plus a <c>$select</c> of the union of their fields and <c>Id</c>; the
	/// <c>$select</c> is left out when it alone would crowd the Ids out of the query, so every column is read.
	/// </summary>
	private static string BuildChunkQuery(IReadOnlyList<ODataSentRecord> chunk,
		IReadOnlyDictionary<string, string>? propertyTypes) {
		string filter = string.Join(" or ", chunk.Select(record => $"Id eq {record.Id.Trim()}"));
		StringBuilder query = new StringBuilder("$filter=").Append(Uri.EscapeDataString(filter));
		IEnumerable<string> fields = chunk.SelectMany(record => ComparableFields(record.Sent, propertyTypes));
		string select = Uri.EscapeDataString(string.Join(",", new[] { "Id" }.Concat(fields).Distinct(StringComparer.Ordinal)));
		if (select.Length <= MaxSelectLength) {
			query.Append("&$select=").Append(select);
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
			failure = "the read-back after the write failed ("
				+ SensitiveErrorTextRedactor.Redact(ex.Message) + "), so the stored values were not compared.";
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
	private static ODataWriteVerification Compare(JsonElement sent, JsonElement stored, IReadOnlyList<string> fields,
		IReadOnlyDictionary<string, string>? propertyTypes) {
		List<ODataFieldDivergence> diverged = [];
		List<string> notReturned = [];
		foreach (string field in fields) {
			JsonElement sentValue = sent.GetProperty(field);
			if (!TryGetProperty(stored, field, out JsonElement storedValue)) {
				notReturned.Add(field);
				continue;
			}
			if (!ODataStoredValueComparer.AreEquivalent(sentValue, storedValue, TypeOf(field, propertyTypes))) {
				diverged.Add(new ODataFieldDivergence(field, sentValue.Clone(), storedValue.Clone()));
			}
		}
		string? note = notReturned.Count == 0
			? null
			: "The read-back did not return " + string.Join(", ", notReturned.Select(field => $"'{field}'"))
				+ ", so " + (notReturned.Count == 1 ? "it was" : "they were") + " not compared.";
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
