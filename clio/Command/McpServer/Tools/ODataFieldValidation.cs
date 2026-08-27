using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Pre-write field validation for <see cref="ODataUpdateTool"/>. Creatio's OData v4 endpoint
/// accepts a PATCH body that names properties the entity type does not have and answers with
/// an empty 204-like body, writing nothing (GitHub #1212) - so a caller that trusts
/// <c>success:true</c> believes a write happened that never did. A second, value-level
/// variant of the same defect: a lookup (reference) column that DOES exist on the OData type
/// set to the empty GUID is silently dropped by the platform (the same PATCH then answers
/// success while the reference is untouched), so that value form is rejected up front with a
/// hint to send <c>null</c> instead. To keep the success flag meaningful, the supplied data
/// fields are verified before the PATCH goes out, against the entity's OData type as published
/// by the service's own metadata endpoint (<c>GET odata/{entity}/$metadata</c>, the same
/// document the service links from every <c>@odata.context</c> response): one fetch yields
/// both the full set of known property names and the set of lookup reference-ID properties.
/// When the metadata endpoint is unavailable (unsupported, empty, or non-XML body) the
/// validation degrades to probing each field through a single-record GET with
/// <c>$select=Id,<field></c>, reusing the strictness the service applies to
/// <c>odata-read</c> <c>$select</c>.
/// </summary>
internal static class ODataFieldValidation {

	/// <summary>Pre-write request timeout, matching the write requests themselves.</summary>
	internal const int RequestTimeoutMs = 30_000;

	/// <summary>
	/// Attempts for the pre-write requests: the metadata/probe GET is read-only and idempotent,
	/// so a bounded retry at the transport absorbs the flaky-stand class (an isolated 5xx or an
	/// empty body) without ever re-sending a write.
	/// </summary>
	internal const int TransientAttempts = 3;

	/// <summary>Delay between <see cref="TransientAttempts"/> pre-write attempts, in seconds.</summary>
	internal const int TransientDelaySec = 1;

	/// <summary>
	/// Cap on the per-field follow-up <c>$select</c> probes of the fallback path. The service
	/// reports only the FIRST unknown <c>$select</c> property, so every remaining field is
	/// re-probed individually; the cap bounds the worst case for a payload that is almost
	/// entirely bad names and the failure text then notes the list may be partial.
	/// </summary>
	private const int MaxFollowUpProbes = 10;

	/// <summary>
	/// A data field to validate: its name and the raw value the caller supplied (the value is
	/// needed for the lookup empty-GUID check, which is name-blind on its own).
	/// </summary>
	public sealed record DataField(string Name, JsonElement Value);

	/// <summary>
	/// Verifies the supplied <paramref name="fields"/> against the entity's OData type before a
	/// write: every field name must exist on the type, and a lookup reference field must not be
	/// set to the empty GUID (the platform drops that value silently; <c>null</c> clears the
	/// reference). Returns <c>null</c> when every field is confirmed, or a failure response when
	/// a field is missing, a value form is unsupported, a field name is malformed, or the
	/// validation itself could not be completed (in which case the update must not be reported as
	/// attempted). No PATCH is sent on any failure path.
	/// </summary>
	/// <param name="client">The environment-scoped application client.</param>
	/// <param name="urlBuilder">The environment-scoped URL builder.</param>
	/// <param name="entity">The OData entity set name (already validated by the caller).</param>
	/// <param name="id">The addressed record GUID (already validated by the caller).</param>
	/// <param name="fields">The data fields (name and value) to verify.</param>
	internal static ODataWriteResponse? ValidateDataFields(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity,
		string id,
		IReadOnlyList<DataField> fields) {
		// A malformed field name would also corrupt a fallback $select list, so it is rejected
		// locally before any remote call - the same character rules odata-read applies to filter
		// fields (simple identifier segments joined by '/' for navigation paths).
		string? malformed = fields.Select(field => field.Name)
			.FirstOrDefault(key => !ODataKeyFormatter.IsValidMemberPath(key));
		if (malformed is not null) {
			return ODataWriteResponse.Failure(
				$"data field '{malformed}' is not a valid OData field name (allowed: letters, digits, underscores, and '/' navigation separators). " +
				"No write was performed.");
		}

		List<string> keys = fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).ToList();

		EntityMetadata metadata = FetchMetadata(client, urlBuilder, entity);
		if (metadata.Resolved) {
			// The service's own type definition is the oracle: every unknown name is known from
			// this single fetch, so no per-field probing is needed on this path.
			List<string> unknown = keys.Where(key => !metadata.Properties.Contains(key)).ToList();
			if (unknown.Count > 0) {
				return ODataWriteResponse.Failure(BuildUnknownFieldsMessage(entity, id, unknown, partial: false, viaProbe: false));
			}
			List<string> emptyGuidLookups = fields
				.Where(field => metadata.ReferenceIdProperties.Contains(field.Name)
					&& field.Value.ValueKind == JsonValueKind.String
					&& IsEmptyGuid(field.Value.GetString()))
				.Select(field => field.Name)
				.Distinct(StringComparer.Ordinal)
				.ToList();
			if (emptyGuidLookups.Count > 0) {
				return ODataWriteResponse.Failure(BuildEmptyGuidLookupMessage(entity, id, emptyGuidLookups));
			}
			return null;
		}

		// The metadata endpoint did not yield a usable type definition (unsupported, empty,
		// non-XML, or a recognized error). Degrade to the $select probe for NAME validation;
		// the lookup value check is skipped on this path because the reference-ID set is unknown
		// (a plain GUID column set to the empty GUID must not be rejected on a guess).
		return ValidateBySelectProbe(client, urlBuilder, entity, id, keys);
	}

	/// <summary>
	/// Outcome of the metadata fetch. <see cref="Resolved"/> means the CSDL was parsed and the
	/// entity's property sets are populated; otherwise exactly one of
	/// <see cref="ServerError"/> (a recognized Creatio error shape for the entity) or
	/// <see cref="UnverifiedDetail"/> (an empty or non-XML, non-JSON body) describes why - both
	/// already redacted at construction so no unredacted transport detail is ever held.
	/// </summary>
	private sealed record EntityMetadata(
		bool Resolved,
		HashSet<string> Properties,
		HashSet<string> ReferenceIdProperties,
		string? ServerError,
		string? UnverifiedDetail);

	/// <summary>
	/// GETs <c>odata/{entity}/$metadata</c> and parses the CSDL: the entity's properties
	/// (following <c>BaseType</c> inheritance) and its lookup reference-ID properties (the
	/// <c>Partner</c> attribute of each <c>NavigationProperty</c>). See
	/// <see cref="EntityMetadata"/> for the outcome encoding.
	/// </summary>
	private static EntityMetadata FetchMetadata(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity) {
		string url = urlBuilder.Build($"odata/{entity.Trim()}/$metadata");
		string body = client.ExecuteGetRequest(url, RequestTimeoutMs, TransientAttempts, TransientDelaySec);
		if (string.IsNullOrWhiteSpace(body)) {
			return new EntityMetadata(false, [], [], null, "the OData metadata response was empty.");
		}
		if (body.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
			// The metadata endpoint answers with CSDL XML. A parse that yields the entity's type
			// definition resolves the validation; any other parse outcome leaves the fields
			// unverified (the fallback probe then decides what it can).
			try {
				(CSDLType? type, HashSet<string> referenceIds) = ParseCSDLEntity(body, entity);
				if (type is not null) {
					return new EntityMetadata(true, type.Properties, referenceIds, null, null);
				}
				return new EntityMetadata(false, [], [], null,
					"the OData metadata response did not contain a type definition for the entity.");
			} catch (Exception) {
				return new EntityMetadata(false, [], [], null,
					SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
			}
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			return ODataResponseError.TryDetect(doc.RootElement, out string serverError)
				? new EntityMetadata(false, [], [], SensitiveErrorTextRedactor.Redact(serverError), null)
				: new EntityMetadata(false, [], [], null,
					SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
		} catch (JsonException) {
			return new EntityMetadata(false, [], [], null,
				SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
		}
	}

	/// <summary>
	/// One OData <c>EntityType</c> as read from CSDL: its property names (navigation properties
	/// included - both are legal <c>$select</c> members) and its base type name for inheritance
	/// resolution.
	/// </summary>
	private sealed record CSDLType(string Name, string? BaseType, HashSet<string> Properties,
		HashSet<string> PartnerReferenceIds);

	/// <summary>
	/// Parses the CSDL document and resolves the <paramref name="entity"/> type following
	/// <c>BaseType</c> inheritance (cycle-guarded). Returns the resolved property set and the
	/// lookup reference-ID set (every <c>NavigationProperty/@Partner</c> value in the
	/// inheritance chain), or <c>(null, empty set)</c> when the document carries no
	/// <c>EntityType</c> matching the entity name.
	/// </summary>
	private static (CSDLType? Type, HashSet<string> ReferenceIds) ParseCSDLEntity(string body, string entity) {
		Dictionary<string, CSDLType> types = ParseCSDLTypes(body);
		if (!TryResolveEntity(types, entity, out CSDLType? target)) {
			return (null, []);
		}
		HashSet<string> referenceIds = [];
		CollectInherited(target, types, visited: [], referenceIds);
		return (target, referenceIds);
	}

	/// <summary>
	/// Reads every <c>EntityType</c> element of the CSDL document into a name-keyed map.
	/// Navigation properties are recorded twice: as legal member names (selectable) and, when
	/// they declare a <c>Partner</c> reference property, as reference-ID names.
	/// </summary>
	private static Dictionary<string, CSDLType> ParseCSDLTypes(string body) {
		Dictionary<string, CSDLType> types = new(StringComparer.Ordinal);
		XmlReaderSettings settings = new() { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit };
		string? currentTypeName = null;
		using XmlReader reader = XmlReader.Create(new StringReader(body), settings);
		while (reader.Read()) {
			if (reader.NodeType == XmlNodeType.Element) {
				if (reader.LocalName == "EntityType" && reader.GetAttribute("Name") is string name) {
					currentTypeName = name;
					types[name] = new CSDLType(name, reader.GetAttribute("BaseType"), [], []);
				}
				else if (currentTypeName is not null && types.TryGetValue(currentTypeName, out CSDLType? type)) {
					if (reader.LocalName == "Property" && reader.GetAttribute("Name") is string propName) {
						type.Properties.Add(propName);
					}
					else if (reader.LocalName == "NavigationProperty") {
						if (reader.GetAttribute("Name") is string navName) {
							type.Properties.Add(navName);
						}
						if (reader.GetAttribute("Partner") is string partner) {
							type.PartnerReferenceIds.Add(partner);
						}
					}
					else if (reader.LocalName is "ComplexType" or "EntityContainer") {
						// Leaves the EntityType scope: nested elements belong to another construct.
						currentTypeName = null;
					}
				}
			}
			else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "EntityType") {
				currentTypeName = null;
			}
		}
		return types;
	}

	/// <summary>
	/// Finds the entity type, tolerating case (the entity set name is what the caller supplied;
	/// the CSDL carries the type's canonical casing) and reporting the single match.
	/// </summary>
	private static bool TryResolveEntity(Dictionary<string, CSDLType> types, string entity, out CSDLType? target) {
		target = types.Values.FirstOrDefault(type =>
			string.Equals(type.Name, entity.Trim(), StringComparison.OrdinalIgnoreCase));
		return target is not null;
	}

	/// <summary>
	/// Walks the <c>BaseType</c> chain (cycle-guarded) accumulating every property name in
	/// <paramref name="target"/>'s resolved set and every <c>Partner</c> reference-ID name in
	/// <paramref name="referenceIds"/>.
	/// </summary>
	private static void CollectInherited(
		CSDLType type,
		Dictionary<string, CSDLType> types,
		List<string> visited,
		HashSet<string> referenceIds) {
		if (visited.Contains(type.Name)) {
			return;
		}
		visited.Add(type.Name);
		foreach (string partner in type.PartnerReferenceIds) {
			referenceIds.Add(partner);
		}
		if (type.BaseType is not null && types.TryGetValue(type.BaseType, out CSDLType? baseType)) {
			CollectInherited(baseType, types, visited, referenceIds);
			type.Properties.UnionWith(baseType.Properties);
		}
	}

	/// <summary>
	/// True when the string is the empty GUID in any casing - the value form the platform drops
	/// on lookup (reference) columns instead of clearing the reference.
	/// </summary>
	private static bool IsEmptyGuid(string? value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.Trim().Equals(Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Fallback validation through the service's own <c>$select</c> strictness: a single-record
	/// GET with <c>$select=Id,<fields></c>. The service rejects an unknown property with a
	/// named error ("Could not find a property named 'X' on type 'Y'"), so the offending fields
	/// become a named tool failure instead of a silent no-write. Because the service reports only
	/// the FIRST unknown property, each remaining field is re-probed individually (capped at
	/// <see cref="MaxFollowUpProbes"/>). An empty or non-JSON probe body leaves the fields
	/// UNVERIFIED, which fails the call the same way - "cannot confirm" must never degrade into
	/// "proceed and report success".
	/// </summary>
	private static ODataWriteResponse? ValidateBySelectProbe(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity,
		string id,
		IReadOnlyList<string> keys) {
		ProbeResult batch = Probe(client, urlBuilder, entity, id, keys);
		if (batch.Succeeded) {
			return null;
		}
		if (batch.ServerError is null) {
			// Empty or non-JSON probe body: the probe did not reach the OData pipeline intact,
			// so field existence is UNKNOWN - and unknown must not become "proceed to write".
			// The transport detail (already redacted) names the layer that failed so the caller
			// can triage it the same way as a failed write.
			return ODataWriteResponse.Failure(
				$"The pre-write field probe for {entity}({id}) returned a response that could not be verified: {batch.UnverifiedDetail}. " +
				"No write was performed; check connectivity with odata-read and retry.");
		}

		string? firstUnknown = ExtractUnknownProperty(batch.ServerError);
		if (firstUnknown is null) {
			// The probe failed for a reason other than a missing property (record not found,
			// unregistered entity, ...): surface it, do not guess, do not write.
			return ODataWriteResponse.Failure(
				$"The pre-write field probe for {entity}({id}) failed, so the update was not performed: {batch.ServerError}");
		}

		List<string> unknown = [firstUnknown];
		bool partial = false;
		List<string> remaining = keys.Where(key => key != firstUnknown).ToList();
		for (int i = 0; i < remaining.Count; i++) {
			string key = remaining[i];
			if (i >= MaxFollowUpProbes) {
				partial = true;
				break;
			}
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
					$"The pre-write field probe for '{key}' on {entity}({id}) failed, so the update was not performed: {single.ServerError}");
			}
			unknown.Add(key);
		}

		return ODataWriteResponse.Failure(BuildUnknownFieldsMessage(entity, id, unknown, partial, viaProbe: true));
	}

	/// <summary>
	/// Outcome of a single-record <c>$select</c> probe. Exactly one of the three signals is set:
	/// <see cref="Succeeded"/> (a clean JSON OData body confirms the keys),
	/// <see cref="ServerError"/> (a recognized Creatio error shape, e.g. the unknown-property
	/// fault), or <see cref="UnverifiedDetail"/> (an empty or non-JSON body that neither
	/// confirms nor explains anything). The text signals are redacted at construction.
	/// </summary>
	private sealed record ProbeResult(bool Succeeded, string? ServerError, string? UnverifiedDetail);

	/// <summary>
	/// GETs the addressed record with <c>$select=Id,<keys></c> (bounded retry: the probe is
	/// read-only). A JSON body without a recognized error shape confirms the keys exist; a
	/// recognized error shape is captured (redacted) as
	/// <see cref="ProbeResult.ServerError"/>; an empty or non-JSON body is captured (redacted)
	/// as <see cref="ProbeResult.UnverifiedDetail"/>.
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
		string body = client.ExecuteGetRequest(url, RequestTimeoutMs, TransientAttempts, TransientDelaySec);
		if (string.IsNullOrWhiteSpace(body)) {
			return new ProbeResult(false, null, "the probe response was empty.");
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			return ODataResponseError.TryDetect(doc.RootElement, out string serverError)
				? new ProbeResult(false, SensitiveErrorTextRedactor.Redact(serverError), null)
				: new ProbeResult(true, null, null);
		} catch (JsonException) {
			return new ProbeResult(false, null,
				SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
		}
	}

	/// <summary>
	/// Matches the OData service's unknown-property fault:
	/// "Could not find a property named 'X' on type 'Y'."
	/// </summary>
	private static readonly Regex UnknownPropertyPattern = new(
		@"Could not find a property named '([^']*)'",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	/// <summary>Extracts the property name from the service's unknown-property fault, if any.</summary>
	private static string? ExtractUnknownProperty(string serverError) {
		Match match = UnknownPropertyPattern.Match(serverError);
		return match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
			? match.Groups[1].Value
			: null;
	}

	/// <summary>
	/// Builds the failure text for fields the OData type does not expose. It names the addressed
	/// record, states that nothing was written, why the rejection happens, how the conclusion was
	/// reached (<paramref name="viaProbe"/>: the degraded $select probe, a service rejection rather
	/// than a definitive type check) and whether the list is complete (the fallback probe path may
	/// stop early), and where to go when the column DOES exist but is absent from $metadata
	/// (e.g. a Color column): execute-esq can read it, but odata-update cannot write it.
	/// </summary>
	private static string BuildUnknownFieldsMessage(string entity, string id, IReadOnlyList<string> unknown, bool partial, bool viaProbe) {
		string list = string.Join(", ", unknown.Select(key => $"'{key}'"));
		string note = partial
			? " (the list may be partial: the per-field follow-up probes stop after a limit)"
			: string.Empty;
		string verdict = viaProbe
			? $"could not be verified against the service (the $select probe rejected them as unknown properties) on {entity}({id})"
			: $"do not exist on the OData type of {entity}({id}) (verified against its $metadata)";
		return
			$"odata-update rejected: field(s) {list}{note} {verdict}, so nothing was written. " +
			"Every field in data must exist on the entity's OData type - the same strictness the OData service applies to odata-read $select. " +
			"If a column exists on the entity but is not exposed through OData (for example a Color column), it cannot be written via odata-update: " +
			"verify it with execute-esq and use a supported write path. Fix the field names and retry.";
	}

	/// <summary>
	/// Builds the failure text for a lookup (reference) field set to the empty GUID: the
	/// platform silently drops that value (the reference is left untouched) while still
	/// answering success, so the call is rejected with a hint that <c>null</c> clears the
	/// reference.
	/// </summary>
	private static string BuildEmptyGuidLookupMessage(string entity, string id, IReadOnlyList<string> fields) {
		string list = string.Join(", ", fields.Select(field => $"'{field}'"));
		return
			$"odata-update rejected: {list} is a lookup (reference) field of {entity}({id}), and the platform silently ignores the empty GUID " +
			"00000000-0000-0000-0000-000000000000 on lookup fields instead of clearing the reference - the value would not have been persisted. " +
			"Send null to clear the reference (a real target GUID writes the reference). No write was performed.";
	}
}
