using System;
using System.Text.Json;

namespace Clio.Common;

/// <summary>
/// Interprets a Creatio service response envelope.
/// </summary>
internal static class DataServiceResponse {

	/// <summary>
	/// Same check for an answer whose HTTP status is known, recording the answer as the site's response when
	/// the check fails.
	/// </summary>
	/// <remarks>
	/// One rule is added to the body check: a status the client kept that is not 2xx is a failure even when the
	/// body would pass it - an empty body or an envelope without a <c>success</c> flag - because accepting it would
	/// report a write that may not have happened as done. With such a status and no service envelope (an empty
	/// body, an error page), the status decides the wording: only a refusal without processing
	/// (<see cref="SiteHttpResponse.IsRefusedWithoutProcessing"/>) says the write was not applied; a 5xx says it
	/// is not confirmed (a gateway can answer after the service applied it); any other 3xx or 4xx says the refusal
	/// does not prove it was not applied.
	/// </remarks>
	/// <param name="answer">The answer as <see cref="SiteCall"/> returned it.</param>
	/// <param name="operationName">The operation to name in the failure message.</param>
	/// <param name="soleWriteAttempts">
	/// The attempt count of the request when it is the sole write of the tools that run it, so its refusal is
	/// recorded through <see cref="SiteResponseCapture.RecordRefusalOfSoleWrite"/>; <see langword="null"/> for
	/// any other write.
	/// </param>
	/// <exception cref="InvalidOperationException">Thrown as by the body check, or for a non-2xx status.</exception>
	internal static void ThrowIfUnsuccessful(SiteHttpResponse answer, string operationName,
		int? soleWriteAttempts = null) {
		ArgumentNullException.ThrowIfNull(answer);
		try {
			if (answer.IsKnownFailureStatus && !IsServiceEnvelope(answer.Body)) {
				throw new InvalidOperationException(DescribeFailureStatus(answer, operationName));
			}
			ThrowIfUnsuccessful(answer.Body, operationName);
			if (answer.IsKnownFailureStatus) {
				throw new InvalidOperationException(DescribeFailureStatus(answer, operationName));
			}
		}
		catch (InvalidOperationException) {
			if (soleWriteAttempts is int attempts) {
				SiteResponseCapture.RecordRefusalOfSoleWrite(answer, attempts);
			}
			else {
				SiteResponseCapture.RecordRefusal(answer);
			}
			throw;
		}
	}

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
			+ "native RightsService instead). For object operation rights (SysEntitySchemaOperationRight) use the "
			+ "set-object-rights tool, which goes through the native Object permissions service instead.";
	}

	private static string DescribeFailureStatus(SiteHttpResponse answer, string operationName) {
		if (answer.IsRefusedWithoutProcessing) {
			return $"{operationName} failed: the environment refused it without processing it (HTTP "
				+ $"{answer.StatusCode}), so it was not applied.";
		}
		return answer.StatusCode >= 500
			? $"{operationName} was not confirmed: the environment answered HTTP {answer.StatusCode} without a "
				+ "service verdict, so whether it was applied is unknown. Read the data before retrying."
			: $"{operationName} failed: the environment refused it with HTTP {answer.StatusCode} and no service "
				+ "verdict, which does not prove it was not applied. Read the data before retrying.";
	}

	// Whether the body is a JSON object, the shape every service envelope has. Anything else - no body, an error
	// page, plain text - carries no verdict of the service.
	private static bool IsServiceEnvelope(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			return document.RootElement.ValueKind is JsonValueKind.Object;
		}
		catch (JsonException) {
			return false;
		}
	}

}
