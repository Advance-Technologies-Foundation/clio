using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// <c>success:true</c> believes a write happened that never did. To keep the success flag
/// meaningful, the supplied data field NAMES are verified before the PATCH goes out, against the
/// entity's OData type as published by the service's own metadata endpoint
/// (<c>GET odata/$metadata</c> - the service-root resource, the same
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
	/// Timeout for the per-field follow-up probes only (the initial batch probe keeps
	/// <see cref="RequestTimeoutMs"/>). A probe that has not answered in a few seconds will not answer
	/// usefully, and the follow-ups run sequentially, so a shorter per-probe budget bounds the
	/// worst-case wall time of the capped fan-out well below N x the write timeout.
	/// </summary>
	internal const int FollowUpProbeTimeoutMs = 10_000;

	/// <summary>
	/// A data field to validate: its name and the raw value the caller supplied (the value is
	/// needed for the lookup empty-GUID check, which is name-blind on its own).
	/// </summary>
	public sealed record DataField(string Name, JsonElement Value);

	/// <summary>
	/// Verifies the supplied <paramref name="fields"/> against the entity's OData type before a
	/// write: every field name must exist on the type. Values are NOT validated. Returns
	/// <c>null</c> when every field is confirmed, or a failure response when
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
			return null;
		}

		// The metadata endpoint did not yield a usable type definition (unsupported, empty,
		// non-XML, or a recognized error). Degrade to the $select probe for name validation.
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
		string? ServerError,
		string? UnverifiedDetail);

	/// <summary>
	/// GETs the SERVICE-ROOT <c>odata/$metadata</c> document and parses the CSDL for the entity's
	/// properties, following <c>BaseType</c> inheritance. The route is the service root, not
	/// <c>odata/{entity}/$metadata</c>: in OData v4 <c>$metadata</c> is a service-root resource and
	/// ASP.NET Web API OData's MetadataRoutingConvention maps only <c>~/$metadata</c>, so the
	/// per-entity form is not a defined resource path - it would 404 into the routing-error body,
	/// leaving this branch permanently unresolved and every call silently on the degraded probe.
	/// The root document covers all types; <see cref="ParseCSDLEntity"/> selects the one addressed.
	/// See <see cref="EntityMetadata"/> for the outcome encoding.
	/// </summary>
	private static EntityMetadata FetchMetadata(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity) {
		string url = urlBuilder.Build("odata/$metadata");
		string body = client.ExecuteGetRequest(url, RequestTimeoutMs, TransientAttempts, TransientDelaySec);
		if (string.IsNullOrWhiteSpace(body)) {
			return new EntityMetadata(false, [], null, "the OData metadata response was empty.");
		}
		if (body.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
			// The metadata endpoint answers with CSDL XML. A parse that yields the entity's type
			// definition resolves the validation; any other parse outcome leaves the fields
			// unverified (the fallback probe then decides what it can).
			try {
				CsdlType? type = ParseCSDLEntity(body, entity);
				if (type is not null) {
					return new EntityMetadata(true, type.Properties, null, null);
				}
				return new EntityMetadata(false, [], null,
					"the OData metadata response did not contain a type definition for the entity.");
			} catch (Exception) {
				return new EntityMetadata(false, [], null,
					SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
			}
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			return ODataResponseError.TryDetect(doc.RootElement, out string serverError)
				? new EntityMetadata(false, [], SensitiveErrorTextRedactor.Redact(serverError), null)
				: new EntityMetadata(false, [], null,
					SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
		} catch (JsonException) {
			return new EntityMetadata(false, [], null,
				SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
		}
	}

	/// <summary>
	/// One OData <c>EntityType</c> as read from CSDL: its property names (navigation properties
	/// included - both are legal <c>$select</c> members) and its base type name for inheritance
	/// resolution.
	/// </summary>
	private sealed record CsdlType(string Name, string? BaseType, HashSet<string> Properties);

	/// <summary>
	/// Parses the CSDL document and resolves the <paramref name="entity"/> type following
	/// <c>BaseType</c> inheritance (cycle-guarded). Returns the resolved type, or <c>null</c> when
	/// the document carries no <c>EntityType</c> matching the entity name.
	/// </summary>
	private static CsdlType? ParseCSDLEntity(string body, string entity) {
		Dictionary<string, CsdlType> types = ParseCSDLTypes(body);
		if (!TryResolveEntity(types, entity, out CsdlType? target)) {
			return null;
		}
		CollectInherited(target, types, visited: []);
		return target;
	}

	/// <summary>
	/// Reads every <c>EntityType</c> element of the CSDL document into a name-keyed map.
	/// Navigation properties are recorded as legal member names - both are selectable.
	/// </summary>
	private static Dictionary<string, CsdlType> ParseCSDLTypes(string body) {
		Dictionary<string, CsdlType> types = new(StringComparer.Ordinal);
		XmlReaderSettings settings = new() { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit };
		string? currentTypeName = null;
		using XmlReader reader = XmlReader.Create(new StringReader(body), settings);
		while (reader.Read()) {
			DispatchNode(reader, types, ref currentTypeName);
		}
		return types;
	}

	/// <summary>
	/// Dispatches one reader node into the <paramref name="types"/> map: an <c>EntityType</c>
	/// opens a new entry, its closing tag ends the current scope, and any other element inside the
	/// current type is a member handled by <see cref="RecordMember"/>.
	/// </summary>
	private static void DispatchNode(XmlReader reader, Dictionary<string, CsdlType> types, ref string? currentTypeName) {
		if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "EntityType") {
			currentTypeName = null;
			return;
		}
		if (reader.NodeType != XmlNodeType.Element) {
			return;
		}
		if (reader.LocalName == "EntityType" && reader.GetAttribute("Name") is string name) {
			currentTypeName = name;
			types[name] = new CsdlType(name, reader.GetAttribute("BaseType"), []);
			return;
		}
		if (currentTypeName is null || !types.TryGetValue(currentTypeName, out CsdlType? type)) {
			return;
		}
		RecordMember(reader, type, ref currentTypeName);
	}

	/// <summary>
	/// Records one element that belongs to the <paramref name="type"/> currently being read: a
	/// <c>Property</c> contributes its name to the property set, a <c>NavigationProperty</c> is
	/// recorded by <see cref="RecordNavigationProperty"/>, and a <c>ComplexType</c>/<c>EntityContainer</c>
	/// ends the entity scope (the nested elements then belong to another construct).
	/// </summary>
	private static void RecordMember(XmlReader reader, CsdlType type, ref string? currentTypeName) {
		if (reader.LocalName == "Property" && reader.GetAttribute("Name") is string propName) {
			type.Properties.Add(propName);
			return;
		}
		if (reader.LocalName == "NavigationProperty" && reader.GetAttribute("Name") is string navName) {
			type.Properties.Add(navName);
			return;
		}
		if (reader.LocalName is "ComplexType" or "EntityContainer") {
			// Leaves the EntityType scope: nested elements belong to another construct.
			currentTypeName = null;
		}
	}

	/// <summary>
	/// Finds the entity type, tolerating case (the entity set name is what the caller supplied;
	/// the CSDL carries the type's canonical casing) and reporting the single match.
	/// </summary>
	private static bool TryResolveEntity(Dictionary<string, CsdlType> types, string entity,
		[NotNullWhen(true)] out CsdlType? target) {
		target = types.Values.FirstOrDefault(type =>
			string.Equals(type.Name, entity.Trim(), StringComparison.OrdinalIgnoreCase));
		return target is not null;
	}

	/// <summary>
	/// Walks the <c>BaseType</c> chain (cycle-guarded) accumulating every property name in
	/// <paramref name="type"/>'s resolved set.
	/// </summary>
	private static void CollectInherited(
		CsdlType type,
		Dictionary<string, CsdlType> types,
		List<string> visited) {
		if (visited.Contains(type.Name)) {
			return;
		}
		visited.Add(type.Name);
		if (type.BaseType is not null && types.TryGetValue(type.BaseType, out CsdlType? baseType)) {
			CollectInherited(baseType, types, visited);
			type.Properties.UnionWith(baseType.Properties);
		}
	}

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
			ProbeResult single = Probe(client, urlBuilder, entity, id, [key], FollowUpProbeTimeoutMs);
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
		IReadOnlyList<string> keys,
		int timeoutMs = RequestTimeoutMs) {
		string selectList = "Id," + string.Join(",", keys);
		string path = $"{ODataKeyFormatter.KeyPath(entity, id)}?$select={Uri.EscapeDataString(selectList)}";
		string url = urlBuilder.Build(path);
		string body = client.ExecuteGetRequest(url, timeoutMs, TransientAttempts, TransientDelaySec);
		if (string.IsNullOrWhiteSpace(body)) {
			// An empty body is read as UNVERIFIED here - the opposite of the write path, where
			// ODataKeyedWrite.ValidateWriteResponse treats an empty/whitespace body as success. The
			// readings differ because the operations differ: a keyed read of an existing record must
			// answer with the record's JSON (or an error) and never legitimately returns empty, so an
			// empty GET body means the request did not reach the OData pipeline intact (a proxy page,
			// a session redirect, a gateway that stripped the body) - whereas a PATCH can legitimately
			// answer a body-less 204 ack. Treating an empty probe body as "fields confirmed" would
			// recreate the false success this validation exists to remove; both stay fail-closed after
			// the bounded retry above is exhausted.
			return new ProbeResult(false, null, "the probe response was empty.");
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			if (IsSelectedRecord(doc.RootElement)) {
				// The probe asked for a specific record with $select, so the caller's own column names
				// sit at the root of a successful body. ODataResponseError.TryDetect classifies whole
				// ERROR envelopes and its ASP.NET branch fires on the mere presence of a root member
				// named ExceptionType/ExceptionMessage/StackTrace - all legal column names on a
				// log-shaped entity. A body carrying @odata.context or the selected Id is the record
				// the probe asked for, so it confirms the keys before any error shape is considered.
				return new ProbeResult(true, null, null);
			}
			return ODataResponseError.TryDetect(doc.RootElement, out string serverError)
				? new ProbeResult(false, SensitiveErrorTextRedactor.Redact(serverError), null)
				: new ProbeResult(true, null, null);
		} catch (JsonException) {
			return new ProbeResult(false, null,
				SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(body)));
		}
	}

	/// <summary>
	/// True when the body is the single record the probe addressed: it carries the service's
	/// <c>@odata.context</c> annotation or the selected <c>Id</c>. An error envelope carries
	/// neither, so this distinguishes a record whose own columns happen to be named like the
	/// members of an error shape from an actual error.
	/// </summary>
	private static bool IsSelectedRecord(JsonElement root) =>
		root.ValueKind == JsonValueKind.Object
		&& (root.TryGetProperty("@odata.context", out _) || root.TryGetProperty("Id", out _));

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

}
