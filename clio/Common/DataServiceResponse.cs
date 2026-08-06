using System;
using System.Text.Json;

namespace Clio.Common;

/// <summary>
/// Interprets a Creatio service response envelope.
/// </summary>
internal static class DataServiceResponse {

	/// <summary>
	/// Throws when the response envelope reports failure, naming <paramref name="operationName"/> and the
	/// server's message. An absent body is accepted, as is an envelope carrying no <c>success</c> flag at all.
	/// Everything that leaves the outcome undetermined is a failure: a non-empty body that is not JSON (an
	/// authentication redirect, an error page) means the request never reached the service, and a
	/// <c>success</c> flag that is present but not a true/false value means the envelope cannot be read.
	/// Accepting either would report a write that did not happen as done.
	/// </summary>
	/// <param name="response">The raw response body.</param>
	/// <param name="operationName">The operation to name in the failure message.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the envelope reports failure, when the body is not an envelope at all, or when its
	/// <c>success</c> flag cannot be read as a true/false value.
	/// </exception>
	internal static void ThrowIfUnsuccessful(string response, string operationName) {
		if (string.IsNullOrWhiteSpace(response)) {
			return;
		}

		JsonDocument document;
		try {
			document = JsonDocument.Parse(response);
		}
		catch (JsonException exception) {
			throw new InvalidOperationException(
				$"{operationName} failed: the environment answered with a body that is not a service response " +
				"(an authentication redirect or an error page), so the request never reached the service.",
				exception);
		}

		using (document) {
			if (document.RootElement.ValueKind is not JsonValueKind.Object) {
				throw new InvalidOperationException(
					$"{operationName} failed: the environment answered with a bare JSON value rather than a service " +
					"response envelope, so the request never reached the service.");
			}
			if (!document.RootElement.TryGetProperty("success", out JsonElement successElement)) {
				return;
			}
			if (successElement.ValueKind is JsonValueKind.True) {
				return;
			}
			if (successElement.ValueKind is not JsonValueKind.False) {
				throw new InvalidOperationException(
					$"{operationName} failed: the environment answered with a 'success' flag that is not a " +
					"true/false value, so whether the write reached the service cannot be determined.");
			}
			string errorMessage = "Unknown error";
			if (document.RootElement.TryGetProperty("errorInfo", out JsonElement errorInfo) &&
				errorInfo.ValueKind is JsonValueKind.Object &&
				errorInfo.TryGetProperty("message", out JsonElement messageElement)) {
				errorMessage = messageElement.GetString() ?? errorMessage;
			}
			else if (document.RootElement.TryGetProperty("responseStatus", out JsonElement responseStatus) &&
					 responseStatus.ValueKind is JsonValueKind.Object &&
					 responseStatus.TryGetProperty("Message", out JsonElement rsMessage)) {
				errorMessage = rsMessage.GetString() ?? errorMessage;
			}

			throw new InvalidOperationException(
				$"{operationName} failed: {errorMessage}{BuildProtectedObjectGuidance(errorMessage)}");
		}
	}

	private static string BuildProtectedObjectGuidance(string errorMessage) {
		if (errorMessage is null
			|| !errorMessage.Contains("does not have permissions for the", StringComparison.OrdinalIgnoreCase)) {
			return string.Empty;
		}

		return " This is an object-permission refusal, not a bad request: DB-first bindings apply rows through "
			+ "the DataService, which enforces object permissions, so a protected system object is refused "
			+ "regardless of the authenticated user's administrative rights. Bindings for ordinary schemas are "
			+ "unaffected. For record-level access rights use the set-record-rights tool (it goes through the "
			+ "native RightsService instead). Object-operation rights (SysEntitySchemaOperationRight) have no "
			+ "administration-capable path in clio yet — deploy them through Creatio's own Object permissions "
			+ "administration or a package installation script.";
	}
}
