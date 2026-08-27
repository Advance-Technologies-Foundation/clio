using System.Text.Json;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared validation and environment resolution for the keyed OData write tools
/// (<see cref="ODataUpdateTool"/>, <see cref="ODataDeleteTool"/>), which both address a single
/// record by GUID and are guarded against keyless mass operations.
/// </summary>
internal static class ODataKeyedWrite {

	/// <summary>
	/// Validates that the entity name is well-formed and the id is a record GUID.
	/// Returns a failure response, or <c>null</c> when the target is valid.
	/// </summary>
	/// <param name="entity">The OData entity set name.</param>
	/// <param name="id">The record GUID.</param>
	/// <param name="operationNoun">The operation for the keyless-guard message, e.g. "update" or "delete".</param>
	internal static ODataWriteResponse ValidateTarget(string entity, string id, string operationNoun) {
		if (string.IsNullOrWhiteSpace(entity)) {
			return ODataWriteResponse.Failure("entity is required.");
		}
		if (!ODataKeyFormatter.IsValidEntityName(entity)) {
			return ODataWriteResponse.Failure("entity must be a valid OData entity set name (letters, digits, underscore).");
		}
		if (string.IsNullOrWhiteSpace(id) || !ODataKeyFormatter.IsGuid(id.Trim())) {
			return ODataWriteResponse.Failure($"id is required and must be a record GUID; keyless mass {operationNoun} is not allowed.");
		}
		return null;
	}

	/// <summary>
	/// Enforces the explicit confirmation gate for a destructive keyed write.
	/// Returns a failure response when <paramref name="confirm"/> is false, otherwise <c>null</c>.
	/// </summary>
	/// <param name="confirm">The caller-supplied confirmation flag.</param>
	/// <param name="entity">The OData entity set name.</param>
	/// <param name="id">The record GUID.</param>
	/// <param name="verb">The action verb, e.g. "update" or "delete" (also the odata-&lt;verb&gt; tool suffix).</param>
	/// <param name="consequence">The noun describing what is authorized, e.g. "change" or "deletion".</param>
	internal static ODataWriteResponse RequireConfirmation(bool confirm, string entity, string id, string verb, string consequence) {
		if (confirm) {
			return null;
		}
		return ODataWriteResponse.Failure(
			$"Refusing to {verb} {entity.Trim()}({id.Trim()}) without confirmation. " +
			$"This is a destructive operation; re-call odata-{verb} with \"confirm\": true to authorize this {consequence}.");
	}

	/// <summary>
	/// Resolves the environment-scoped application client and builds the key-addressed OData URL.
	/// </summary>
	internal static (IApplicationClient client, string url) ResolveTarget(
		IToolCommandResolver commandResolver, string environmentName, string entity, string id) {
		EnvironmentOptions options = new() { Environment = environmentName };
		IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
		IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);
		string url = urlBuilder.Build(ODataKeyFormatter.KeyPath(entity, id));
		return (client, url);
	}

	/// <summary>
	/// Validates the response body of a keyed PATCH/DELETE write. Creatio normally answers a
	/// successful update or delete with <c>204 No Content</c> (empty body), so an empty response is
	/// success. The transport layer (<see cref="IApplicationClient"/>) returns whatever body came
	/// back regardless of HTTP status - it never throws for a non-2xx response - so a body that IS
	/// present must be inspected: a recognized Creatio error shape, or a body that fails to parse as
	/// JSON at all (an IIS/proxy error page, a stale-session redirect), both mean the write must not
	/// be reported as successful.
	/// </summary>
	/// <param name="response">The raw response body returned by the PATCH/DELETE request.</param>
	/// <returns>A redacted failure message, or <c>null</c> when the response is consistent with success.</returns>
	internal static string ValidateWriteResponse(string response) {
		// Whitespace counts as "no body", not as an unparsable body: a proxy that pads an otherwise
		// empty 204 with a newline is the realistic source of a whitespace-only response, and Creatio
		// itself always answers a genuine failure with one of the recognized JSON error shapes. Treating
		// whitespace as a failure would therefore turn successful writes into false negatives, which is
		// the worse outcome here - the caller would re-send an update that already landed.
		if (string.IsNullOrWhiteSpace(response)) {
			return null;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(response);
			return ODataResponseError.TryDetect(doc.RootElement, out string serverError)
				? SensitiveErrorTextRedactor.Redact(serverError)
				: null;
		} catch (JsonException) {
			return SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(response));
		}
	}
}
