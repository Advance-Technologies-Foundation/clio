using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Pre-write field validation for <see cref="ODataUpdateTool"/>. Creatio's OData v4 endpoint
/// accepts a PATCH body that names properties the entity type does not have and answers with
/// an empty 204-like body, writing nothing (GitHub #1212) - so a caller that trusts
/// <c>success:true</c> believes a write happened that never did. To keep that flag meaningful,
/// the supplied data fields are verified against the entity's OData type before the PATCH goes
/// out, reusing the same strictness the service already applies to <c>odata-read</c>
/// <c>$select</c>: a single-record GET with <c>$select=Id,<fields></c>. The service rejects
/// an unknown property with a named error ("Could not find a property named 'X' on type 'Y'"),
/// which becomes a named tool failure instead of a silent no-write.
/// </summary>
internal static class ODataFieldValidation {

	/// <summary>Probe request timeout, matching the write requests themselves.</summary>
	private const int ProbeTimeoutMs = 30_000;

	/// <summary>
	/// Matches the OData service's unknown-property fault:
	/// "Could not find a property named 'X' on type 'Y'."
	/// </summary>
	private static readonly Regex UnknownPropertyPattern = new(
		@"Could not find a property named '([^']*)'",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	/// <summary>
	/// Verifies that every <paramref name="dataKeys"/> field exists on the entity's OData type.
	/// Returns <c>null</c> when all fields are confirmed, or a failure response when a field is
	/// missing, a field name is malformed, or the probe itself could not be completed (in which
	/// case the update must not be reported as attempted). No PATCH is sent on any failure path.
	/// </summary>
	/// <param name="client">The environment-scoped application client.</param>
	/// <param name="urlBuilder">The environment-scoped URL builder.</param>
	/// <param name="entity">The OData entity set name (already validated by the caller).</param>
	/// <param name="id">The addressed record GUID (already validated by the caller).</param>
	/// <param name="dataKeys">The data field names to verify.</param>
	internal static ODataWriteResponse? ValidateDataFields(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity,
		string id,
		IReadOnlyList<string> dataKeys) {
		// A malformed field name would also corrupt the probe's $select list, so it is rejected
		// locally before any remote call - the same character rules odata-read applies to filter
		// fields (simple identifier segments joined by '/' for navigation paths).
		foreach (string key in dataKeys) {
			if (!ODataKeyFormatter.IsValidMemberPath(key)) {
				return ODataWriteResponse.Failure(
					$"data field '{key}' is not a valid OData field name (allowed: letters, digits, underscores, and '/' navigation separators). " +
					"No write was performed.");
			}
		}

		List<string> keys = dataKeys.Distinct(StringComparer.Ordinal).ToList();
		ProbeResult batch = Probe(client, urlBuilder, entity, id, keys);
		if (batch.Succeeded) {
			return null;
		}
		if (batch.ServerError is null) {
			// Empty or non-JSON probe body: the probe did not reach the OData pipeline intact,
			// so field existence is UNKNOWN - and unknown must not become "proceed to write".
			// The transport detail (an empty body, or the shared non-JSON diagnostic) names the
			// layer that failed so the caller can triage it the same way as a failed write.
			return ODataWriteResponse.Failure(
				$"The pre-write field probe for {entity}({id}) returned a response that could not be verified: {batch.UnverifiedDetail}. " +
				"No write was performed; check connectivity with odata-read and retry.");
		}

		// The service's $select validation reports only the FIRST unknown property. Probe each
		// remaining field individually so the caller learns every bad name in one round trip and
		// can fix them all in a single retry.
		string? firstUnknown = ExtractUnknownProperty(batch.ServerError);
		if (firstUnknown is null) {
			// The probe failed for a reason other than a missing property (record not found,
			// unregistered entity, ...): surface it, do not guess, do not write.
			return ODataWriteResponse.Failure(
				$"The pre-write field probe for {entity}({id}) failed, so the update was not performed: " +
				SensitiveErrorTextRedactor.Redact(batch.ServerError));
		}

		List<string> unknown = [firstUnknown];
		foreach (string key in keys.Where(k => k != firstUnknown)) {
			ProbeResult single = Probe(client, urlBuilder, entity, id, [key]);
			if (single.Succeeded) {
				continue;
			}
			if (single.ServerError is null) {
				return ODataWriteResponse.Failure(
					$"The pre-write field probe for '{key}' on {entity}({id}) returned a response that could not be verified: {single.UnverifiedDetail}. " +
					"No write was performed; retry.");
			}
			if (ExtractUnknownProperty(single.ServerError) is null) {
				return ODataWriteResponse.Failure(
					$"The pre-write field probe for '{key}' on {entity}({id}) failed, so the update was not performed: " +
					SensitiveErrorTextRedactor.Redact(single.ServerError));
			}
			unknown.Add(key);
		}

		return ODataWriteResponse.Failure(BuildUnknownFieldsMessage(entity, id, unknown));
	}

	/// <summary>
	/// Outcome of a single-record <c>$select</c> probe. Exactly one of the three signals is set:
	/// <see cref="Succeeded"/> (a clean JSON OData body confirms the keys),
	/// <see cref="ServerError"/> (a recognized Creatio error shape, e.g. the unknown-property
	/// fault), or <see cref="UnverifiedDetail"/> (an empty or non-JSON body that neither
	/// confirms nor explains anything).
	/// </summary>
	private sealed record ProbeResult(bool Succeeded, string? ServerError, string? UnverifiedDetail);

	/// <summary>
	/// GETs the addressed record with <c>$select=Id,<keys></c>. A JSON body without a
	/// recognized error shape confirms the keys exist; a recognized error shape is captured
	/// as <see cref="ProbeResult.ServerError"/>; an empty or non-JSON body is captured as
	/// <see cref="ProbeResult.UnverifiedDetail"/> (the shared non-JSON transport diagnostic for
	/// an unparseable body, a note for an empty one).
	/// </summary>
	private static ProbeResult Probe(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity,
		string id,
		IReadOnlyList<string> keys) {
		string selectList = "Id," + string.Join(",", keys);
		string path = $"{ODataKeyFormatter.KeyPath(entity, id)}?$select={Uri.EscapeDataString(selectList)}";
		string url = urlBuilder.Build(path);
		string body = client.ExecuteGetRequest(url, ProbeTimeoutMs);
		if (string.IsNullOrWhiteSpace(body)) {
			return new ProbeResult(false, null, "the probe response was empty.");
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			return ODataResponseError.TryDetect(doc.RootElement, out string serverError)
				? new ProbeResult(false, serverError, null)
				: new ProbeResult(true, null, null);
		} catch (JsonException) {
			return new ProbeResult(false, null, ODataResponseError.DescribeNonJsonResponse(body));
		}
	}

	/// <summary>Extracts the property name from the service's unknown-property fault, if any.</summary>
	private static string? ExtractUnknownProperty(string serverError) {
		Match match = UnknownPropertyPattern.Match(serverError);
		return match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
			? match.Groups[1].Value
			: null;
	}

	/// <summary>
	/// Builds the failure text for fields the OData type does not expose. It states that nothing
	/// was written, why the rejection happens, and where to go when the column DOES exist but is
	/// absent from $metadata (e.g. a Color column): execute-esq can read it, but odata-update
	/// cannot write it.
	/// </summary>
	private static string BuildUnknownFieldsMessage(string entity, string id, IReadOnlyList<string> unknown) {
		string list = string.Join(", ", unknown.Select(k => $"'{k}'"));
		return
			$"odata-update rejected: field(s) {list} do not exist on the OData type of {entity}, so nothing was written. " +
			"Every field in data must exist on the entity's OData type - the same strictness the OData service applies to odata-read $select. " +
			"If a column exists on the entity but is not exposed through OData (for example a Color column), it cannot be written via odata-update: " +
			"verify it with execute-esq and use a supported write path. Fix the field names and retry.";
	}
}