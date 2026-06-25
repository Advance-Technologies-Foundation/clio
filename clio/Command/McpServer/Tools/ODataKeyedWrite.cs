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
	/// Resolves the environment-scoped application client and URL builder.
	/// </summary>
	internal static (IApplicationClient Client, IServiceUrlBuilder UrlBuilder) ResolveClients(
		IToolCommandResolver commandResolver, string environmentName) {
		EnvironmentOptions options = new() { Environment = environmentName };
		return (
			commandResolver.Resolve<IApplicationClient>(options),
			commandResolver.Resolve<IServiceUrlBuilder>(options)
		);
	}

	/// <summary>
	/// Resolves the environment-scoped application client and builds the key-addressed OData URL.
	/// </summary>
	internal static (IApplicationClient client, string url) ResolveTarget(
		IToolCommandResolver commandResolver, string environmentName, string entity, string id) {
		var (client, urlBuilder) = ResolveClients(commandResolver, environmentName);
		string url = urlBuilder.Build(ODataKeyFormatter.KeyPath(entity, id));
		return (client, url);
	}
}
