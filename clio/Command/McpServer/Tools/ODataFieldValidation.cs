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
	/// Timeout for the OPTIONAL metadata read of <see cref="TryGetPropertyTypes"/>, used with a single
	/// attempt. On that path the CSDL only sharpens the value guard - the write proceeds without it - so
	/// it must not inherit the mandatory pre-write budget (<see cref="RequestTimeoutMs"/> x
	/// <see cref="TransientAttempts"/> = up to 90 s plus delays); a stalled <c>$metadata</c> would
	/// otherwise hold the whole batch before its first POST.
	/// </summary>
	internal const int OptionalMetadataTimeoutMs = 10_000;

	/// <summary>Attempts used for the optional metadata read: one, then degrade.</summary>
	internal const int OptionalMetadataAttempts = 1;

	/// <summary>
	/// A data field to validate. Only the name is carried: validation is name-only, so a value
	/// would be plumbed through and never read.
	/// </summary>
	public sealed record DataField(string Name);

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
	/// <param name="fields">The data field names to verify.</param>
	/// <param name="propertyTypes">
	/// Receives the entity's property name to Edm type map when the CSDL was parsed, or
	/// <see langword="null"/> when the metadata endpoint could not be resolved. The caller uses it to
	/// decide which supplied values are bound to a date-time column (see <see cref="ODataDateTimeGuard"/>);
	/// a <see langword="null"/> map means the type is unknown, not that no column is temporal.
	/// </param>
	internal static ODataWriteResponse? ValidateDataFields(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity,
		string id,
		IReadOnlyList<DataField> fields,
		out IReadOnlyDictionary<string, string>? propertyTypes) {
		propertyTypes = null;
		// A malformed field name would also corrupt a fallback $select list, so it is rejected
		// locally before any remote call. A PATCH key must be a SIMPLE identifier, not the member
		// path odata-read accepts for filters: `Account/Id` is a read-oriented navigation path, and
		// the $select probe positively confirms it (a nested projection is a valid read), after which
		// the tool PATCHed {"Account/Id": ...} and reported success:true without changing the
		// intended field.
		string? malformed = fields.Select(field => field.Name)
			.FirstOrDefault(key => !ODataKeyFormatter.IsSimpleIdentifier(key));
		if (malformed is not null) {
			return ODataWriteResponse.Failure(
				$"data field '{malformed}' is not a writable OData property name (allowed: letters, digits and underscores; " +
				"a navigation path such as 'Account/Id' is readable but cannot be written - set the foreign-key column, e.g. 'AccountId'). " +
				"No write was performed.");
		}

		List<string> keys = fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).ToList();

		EntityMetadata metadata = FetchMetadata(client, urlBuilder, entity, RequestTimeoutMs, TransientAttempts);
		if (metadata.Resolved) {
			propertyTypes = metadata.PropertyTypes;
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
	/// Reads the entity's property name to Edm type map from the service-root CSDL, for callers that
	/// need the declared types without the name validation (<c>odata-create</c>, which does not verify
	/// field names). Returns <see langword="null"/> when the metadata endpoint could not be resolved -
	/// the caller must treat that as "types unknown" and never as a reason to fail the write.
	/// </summary>
	/// <param name="client">The environment-scoped application client.</param>
	/// <param name="urlBuilder">The environment-scoped URL builder.</param>
	/// <param name="entity">The OData entity set name.</param>
	internal static IReadOnlyDictionary<string, string>? TryGetPropertyTypes(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity) {
		try {
			EntityMetadata metadata = FetchMetadata(
				client, urlBuilder, entity, OptionalMetadataTimeoutMs, OptionalMetadataAttempts);
			return metadata.Resolved ? metadata.PropertyTypes : null;
		} catch (Exception) {
			// The type map is an optimization for the value guard, never a precondition of the write:
			// a failed metadata fetch degrades to the conservative textual rule instead of failing.
			return null;
		}
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
		Dictionary<string, string> PropertyTypes,
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
		string entity,
		int timeoutMs,
		int attempts) {
		string url = urlBuilder.Build("odata/$metadata");
		string body = client.ExecuteGetRequest(url, timeoutMs, attempts, TransientDelaySec);
		if (string.IsNullOrWhiteSpace(body)) {
			return new EntityMetadata(false, [], [], null, "the OData metadata response was empty.");
		}
		if (body.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
			// The metadata endpoint answers with CSDL XML. A parse that yields the entity's type
			// definition resolves the validation; any other parse outcome leaves the fields
			// unverified (the fallback probe then decides what it can).
			try {
				CsdlType? type = ParseCSDLEntity(body, entity);
				if (type is not null) {
					return new EntityMetadata(true, type.Properties, type.PropertyTypes, null, null);
				}
				return new EntityMetadata(false, [], [], null,
					"the OData metadata response did not contain a type definition for the entity.");
			} catch (Exception) {
				return new EntityMetadata(false, [], [], null,
					SensitiveErrorTextRedactor.Redact(CreatioResponseError.DescribeNonJsonResponse(body)));
			}
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			return CreatioResponseError.TryDetect(doc.RootElement, CreatioResponseContext.ODataPayload, out string serverError)
				? new EntityMetadata(false, [], [], SensitiveErrorTextRedactor.Redact(serverError), null)
				: new EntityMetadata(false, [], [], null,
					SensitiveErrorTextRedactor.Redact(CreatioResponseError.DescribeNonJsonResponse(body)));
		} catch (JsonException) {
			return new EntityMetadata(false, [], [], null,
				SensitiveErrorTextRedactor.Redact(CreatioResponseError.DescribeNonJsonResponse(body)));
		}
	}

	/// <summary>
	/// One OData <c>EntityType</c> as read from CSDL: its property names (navigation properties
	/// included - both are legal <c>$select</c> members) and its base type name for inheritance
	/// resolution.
	/// </summary>
	private sealed record CsdlType(
		string Name,
		string? BaseType,
		HashSet<string> Properties,
		Dictionary<string, string> PropertyTypes);

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
			types[name] = new CsdlType(name, reader.GetAttribute("BaseType"), [], new Dictionary<string, string>(StringComparer.Ordinal));
			return;
		}
		if (currentTypeName is null || !types.TryGetValue(currentTypeName, out CsdlType? type)) {
			return;
		}
		RecordMember(reader, type, ref currentTypeName);
	}

	/// <summary>
	/// Records one element that belongs to the <paramref name="type"/> currently being read: only a
	/// structural <c>Property</c> contributes its name to the writable set, and a
	/// <c>ComplexType</c>/<c>EntityContainer</c> ends the entity scope (the nested elements then belong
	/// to another construct).
	/// </summary>
	/// <remarks>
	/// A <c>NavigationProperty</c> is deliberately NOT recorded. It used to land in the same set as the
	/// structural properties, so a raw <c>Account</c> in the update payload passed validation and issued
	/// one PATCH - but an OData relationship is written through bind semantics, not by assigning the
	/// navigation name, and the tool contract points callers at the structural <c>AccountId</c> field
	/// instead. Leaving the name out keeps it unverified, which is also what the fallback <c>$select</c>
	/// probe can honestly say about it: that probe only proves a field is READABLE and structural.
	/// </remarks>
	private static void RecordMember(XmlReader reader, CsdlType type, ref string? currentTypeName) {
		if (reader.LocalName == "Property" && reader.GetAttribute("Name") is string propName) {
			type.Properties.Add(propName);
			// The declared Edm type is what lets the value-level date-time guard fire on temporal
			// columns only, so a text column holding "2024-01-01T04:00:00" stays writable.
			if (reader.GetAttribute("Type") is string propType) {
				type.PropertyTypes[propName] = propType;
			}
			return;
		}
		if (reader.LocalName == "NavigationProperty") {
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
		HashSet<string> visited) {
		if (!visited.Add(type.Name)) {
			return;
		}
		if (type.BaseType is null) {
			return;
		}
		// The map is keyed by the EntityType's short Name, but per CSDL 4.0 a BaseType attribute is a
		// FULLY-QUALIFIED type name ("Terrasoft.Configuration.OData.BaseEntity"). Looking the raw value up
		// never matched, so the whole walk was a silent no-op and every field inherited from BaseEntity
		// (Id, CreatedOn, ModifiedOn, CreatedById, ModifiedById, ...) was reported as non-existent.
		if (types.TryGetValue(ShortTypeName(type.BaseType), out CsdlType? baseType)) {
			CollectInherited(baseType, types, visited);
			type.Properties.UnionWith(baseType.Properties);
			foreach (KeyValuePair<string, string> inherited in baseType.PropertyTypes) {
				// A redeclared property on the derived type wins; TryAdd keeps the derived declaration.
				type.PropertyTypes.TryAdd(inherited.Key, inherited.Value);
			}
		}
	}

	/// <summary>Strips the CSDL namespace qualifier from a type reference.</summary>
	private static string ShortTypeName(string qualifiedName) {
		int lastDot = qualifiedName.LastIndexOf('.');
		return lastDot >= 0 ? qualifiedName[(lastDot + 1)..] : qualifiedName;
	}

	/// <summary>
	/// Fallback validation through the service's own <c>$select</c> strictness: a single-record
	/// GET with <c>$select=Id,<fields></c>. The service rejects an unknown property with a
	/// named error ("Could not find a property named 'X' on type 'Y'"), so the offending fields
	/// become a named tool failure instead of a silent no-write. Because the service reports only
	/// the FIRST unknown property, each remaining field is re-probed individually (capped at
	/// <see cref="MaxFollowUpProbes"/>). Any probe body that is not the addressed record carrying the
	/// probed keys leaves the fields UNVERIFIED, which fails the call the same way - "cannot confirm"
	/// must never degrade into "proceed and report success".
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
			// The probe returned no proof: an empty or non-JSON body (the request did not reach the
			// OData pipeline intact), or JSON that is not the addressed record with the probed keys.
			// Field existence is UNKNOWN - and unknown must not become "proceed to write".
			// The transport detail (already redacted) names the layer that failed so the caller
			// can triage it the same way as a failed write.
			return ODataWriteResponse.Failure(
				$"The pre-write field probe for {entity}({id}) returned a response that could not be verified: {batch.UnverifiedDetail}. " +
				"No write was performed; check connectivity with odata-read and retry.");
		}

		//An extracted name is trusted ONLY when it is exactly one of the keys the caller asked to
		//write. The name comes out of server-authored fault text, so anything else - a different
		//property, a forged fragment - is not a verdict about this request.
		string? firstUnknown = MatchRequestedKey(ExtractUnknownProperty(batch.ServerError), keys);
		if (firstUnknown is null) {
			// The probe failed for a reason other than a missing property the caller named (record not
			// found, unregistered entity, ...). The server's own wording is NOT surfaced: the redactor
			// removes recognized secrets and URIs, but it neither neutralizes prompt-like text nor
			// removes opaque session tokens, and this text lands in an MCP transcript a model reads as
			// trusted content. Fixed local wording, and no write.
			return ODataWriteResponse.Failure(ProbeRejectedMessage(entity, id));
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
			if (MatchRequestedKey(ExtractUnknownProperty(single.ServerError), [key]) is null) {
				return ODataWriteResponse.Failure(ProbeRejectedMessage(entity, id, key));
			}
			unknown.Add(key);
		}

		return ODataWriteResponse.Failure(BuildUnknownFieldsMessage(entity, id, unknown, partial, viaProbe: true));
	}

	/// <summary>
	/// Outcome of a single-record <c>$select</c> probe. Exactly one of the three signals is set:
	/// <see cref="Succeeded"/> (the body is the addressed record and carries every probed key),
	/// <see cref="ServerError"/> (a recognized Creatio error shape, e.g. the unknown-property
	/// fault), or <see cref="UnverifiedDetail"/> (any body that neither proves the keys nor explains
	/// a failure - empty, non-JSON, or JSON that is not the addressed record). The text signals are
	/// redacted at construction.
	/// </summary>
	private sealed record ProbeResult(bool Succeeded, string? ServerError, string? UnverifiedDetail);

	/// <summary>
	/// GETs the addressed record with <c>$select=Id,<keys></c> (bounded retry: the probe is
	/// read-only). Only the addressed record carrying every probed key confirms them; a recognized
	/// error shape is captured (redacted) as <see cref="ProbeResult.ServerError"/>; every other body -
	/// empty, non-JSON, or JSON that is not that record - is captured (redacted) as
	/// <see cref="ProbeResult.UnverifiedDetail"/>.
	/// </summary>
	private static ProbeResult Probe(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string entity,
		string id,
		IReadOnlyList<string> keys,
		int timeoutMs = RequestTimeoutMs) {
		// NOT percent-encoded: the comma is $select's own list separator, and encoding it turns a
		// three-field select into a request for one selector literally named "Id%2CCreatedOn%2CName",
		// which a conforming server rejects as an unknown property - failing every probe on this path.
		// The names are already constrained by IsValidMemberPath to letters, digits, underscore and '/',
		// none of which need encoding.
		string selectList = "Id," + string.Join(",", keys);
		string path = $"{ODataKeyFormatter.KeyPath(entity, id)}?$select={selectList}";
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
			if (IsAddressedRecordWithKeys(doc.RootElement, id, keys, out string unverifiedReason)) {
				// The positive check runs BEFORE error detection, and that order matters: the probe asked
				// for a specific record with $select, so the caller's own column names sit at the root of a
				// successful body, and CreatioResponseError's ASP.NET branch fires on the mere presence of a
				// root member named ExceptionType/ExceptionMessage/StackTrace - all legal column names on a
				// log-shaped entity. Proof that this is the addressed record therefore outranks any error
				// shape it may resemble.
				return new ProbeResult(true, null, null);
			}
			// Not proof. A recognized error envelope explains why; anything else is UNVERIFIED - never
			// success. The absent-error branch used to return Succeeded: true, so a `{}` body (or any
			// unrelated JSON object) confirmed the fields, sent the PATCH and recreated #1212.
			return CreatioResponseError.TryDetect(doc.RootElement, CreatioResponseContext.ODataPayload, out string serverError)
				? new ProbeResult(false, SensitiveErrorTextRedactor.Redact(serverError), null)
				: new ProbeResult(false, null, SensitiveErrorTextRedactor.Redact(unverifiedReason));
		} catch (JsonException) {
			//The body itself is never quoted: DescribeNonJsonResponse embeds a 500-character preview,
			//and a proxy page or session redirect can put arbitrary remote content in it. The locally
			//authored hint says what the shape means without reproducing any of it.
			return new ProbeResult(false, null,
				"the probe response was not JSON, which Creatio's OData pipeline never returns by itself - "
				+ "this points to a proxy, IIS, routing or session problem rather than the request's shape. "
				+ "The body is not reproduced here");
		}
	}

	/// <summary>
	/// True only when the body is positive proof that the probed keys exist: it is the record the
	/// probe addressed (an <c>Id</c> equal to <paramref name="id"/>) and it carries every requested
	/// key. The absence of a known error shape is NOT proof - the previous check accepted any object
	/// carrying <c>@odata.context</c> OR any <c>Id</c>, and the caller accepted every other JSON
	/// shape outright, so a <c>{}</c> probe body, an unrelated record or a partial projection all
	/// reported the fields as confirmed, sent the PATCH and recreated #1212. Everything that is not
	/// this shape leaves the fields unverified and the caller fails without writing.
	/// </summary>
	/// <param name="unverifiedReason">
	/// When the method returns <see langword="false"/>, states which part of the proof is missing so
	/// the caller's failure message names it; otherwise empty.
	/// </param>
	private static bool IsAddressedRecordWithKeys(
		JsonElement root, string id, IReadOnlyList<string> keys, out string unverifiedReason) {
		unverifiedReason = string.Empty;
		if (root.ValueKind != JsonValueKind.Object) {
			unverifiedReason = "the probe response was not a JSON object, so it is not the addressed record.";
			return false;
		}
		if (!(root.TryGetProperty("Id", out JsonElement probedId) && IsSameKey(probedId, id))) {
			unverifiedReason =
				"the probe response did not identify itself as the addressed record - it carries no Id equal to the requested key.";
			return false;
		}
		List<string> missing = keys.Where(key => !HasSelectedMember(root, key)).ToList();
		if (missing.Count > 0) {
			unverifiedReason =
				"the probe response is the addressed record but does not carry "
				+ string.Join(", ", missing.Select(key => $"'{key}'"))
				+ ", so those field(s) are not confirmed to exist.";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Compares the probed <c>Id</c> with the addressed key. Almost every Creatio entity keys on a
	/// GUID but some key on a numeric or string column, so all three representations are accepted;
	/// GUIDs are compared parsed rather than textually, because the service may echo a casing or
	/// brace form the caller did not use.
	/// </summary>
	private static bool IsSameKey(JsonElement probedId, string id) {
		switch (probedId.ValueKind) {
			case JsonValueKind.String:
				string? probedText = probedId.GetString();
				return Guid.TryParse(probedText, out Guid probedGuid) && Guid.TryParse(id, out Guid addressedGuid)
					? probedGuid == addressedGuid
					: string.Equals(probedText, id, StringComparison.OrdinalIgnoreCase);
			case JsonValueKind.Number:
				return string.Equals(probedId.GetRawText(), id.Trim(), StringComparison.Ordinal);
			default:
				return false;
		}
	}

	/// <summary>
	/// True when the record carries the selected member. A member path (<c>A/B</c>) counts either as a
	/// literal member name - a flattened projection - or by walking the nested objects the path names,
	/// so neither serialization of a navigation select is mistaken for a missing field. An explicit
	/// <c>null</c> counts as present: the property exists, it just has no value.
	/// </summary>
	private static bool HasSelectedMember(JsonElement root, string key) {
		if (root.TryGetProperty(key, out _)) {
			return true;
		}
		JsonElement current = root;
		foreach (string segment in key.Split('/')) {
			if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next)) {
				return false;
			}
			current = next;
		}
		return true;
	}

	/// <summary>
	/// Matches the OData service's unknown-property fault:
	/// "Could not find a property named 'X' on type 'Y'."
	/// </summary>
	private static readonly Regex UnknownPropertyPattern = new(
		@"Could not find a property named '([^']*)'",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	/// <summary>
	/// Returns <paramref name="extracted"/> only when it exactly matches one of
	/// <paramref name="requestedKeys"/>. The name is parsed out of server-authored fault text, so it
	/// is a verdict about this request only when it names a field the caller actually asked to write.
	/// </summary>
	private static string? MatchRequestedKey(string? extracted, IReadOnlyList<string> requestedKeys) =>
		extracted is not null && requestedKeys.Contains(extracted, StringComparer.Ordinal)
			? extracted
			: null;

	/// <summary>
	/// The fixed diagnostic for a probe the service rejected for a reason this tool cannot attribute
	/// to one of the caller's own field names. It carries no server prose - see the call sites.
	/// </summary>
	private static string ProbeRejectedMessage(string entity, string id, string? key = null) {
		string subject = key is null ? $"{entity}({id})" : $"'{key}' on {entity}({id})";
		return $"The pre-write field probe for {subject} was rejected by Creatio for a reason that does not "
			+ "identify one of the requested fields, so the update was not performed. The server's own wording "
			+ "is not reproduced here, because a service or proxy response is not trusted text in an MCP "
			+ "transcript; check the environment's own logs, then verify the record Id, the entity name and the "
			+ "credentials. No write was performed.";
	}

	/// <summary>Extracts the property name from the service's unknown-property fault, if any.</summary>	/// <summary>Extracts the property name from the service's unknown-property fault, if any.</summary>
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
