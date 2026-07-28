using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.Theming;

/// <summary>
/// The <c>BaseResponse</c> shape returned by the native Creatio <c>ThemeService</c> endpoints: a success flag
/// plus an optional error block.
/// </summary>
internal record ThemeServiceResponse
{
	/// <summary>Whether the operation succeeded. A missing value is treated as success (the contract default).</summary>
	[JsonPropertyName("success")]
	public bool? Success { get; init; }

	/// <summary>Diagnostic block populated by the service when <see cref="Success"/> is <c>false</c>.</summary>
	[JsonPropertyName("errorInfo")]
	public ThemeServiceErrorInfo ErrorInfo { get; init; }
}

/// <summary>
/// The <c>errorInfo</c> block of a <see cref="ThemeServiceResponse"/>.
/// </summary>
internal sealed record ThemeServiceErrorInfo
{
	/// <summary>Server-side error classification (e.g. <c>SecurityException</c>, <c>ArgumentException</c>).</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>Human-readable failure message surfaced to the caller.</summary>
	[JsonPropertyName("message")]
	public string Message { get; init; }
}

/// <summary>
/// Parses a native <c>ThemeService</c> <c>BaseResponse</c> body. An explicit <c>success:false</c> is a failure,
/// and so is a non-empty body that is not valid JSON: ThemeService always answers with a JSON <c>BaseResponse</c>,
/// so a non-JSON payload (e.g. an authentication-redirect login page or a proxy/error page) means the request
/// never reached the service and the operation must not be reported as successful. Only a genuinely empty body
/// is tolerated as success (the contract default).
/// </summary>
internal static class ThemeServiceResponseParser
{
	private static readonly JsonSerializerOptions ResponseJsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Determines whether <paramref name="response"/> reports an explicit failure (<c>success:false</c>).
	/// </summary>
	/// <param name="response">The raw response body returned by the ThemeService endpoint.</param>
	/// <param name="errorMessage">
	/// On an explicit <c>success:false</c>, the server-provided <c>errorInfo.message</c> — control-character-stripped
	/// and length-capped for safe display (may be <c>null</c> when the server omits the block); on a non-empty,
	/// non-JSON body, an "Unexpected response from server" diagnostic carrying a control-character-stripped,
	/// length-capped excerpt of the body; otherwise <c>null</c>.
	/// </param>
	/// <returns>
	/// <c>true</c> when the body carries an explicit <c>success:false</c> or is a non-empty, non-JSON body;
	/// <c>false</c> for <c>success:true</c> or an empty body.
	/// </returns>
	public static bool TryGetFailure(string response, out string errorMessage) {
		return TryGetFailure<ThemeServiceResponse>(response, out errorMessage, out _);
	}

	/// <summary>
	/// Same failure detection as <see cref="TryGetFailure(string, out string)"/>, but deserializes the body into
	/// <typeparamref name="T"/> so a caller can read payload fields (e.g. a <c>values</c> array) on a non-failure
	/// JSON response — keeping the success / <c>errorInfo</c> / non-JSON evaluation in one place.
	/// </summary>
	/// <typeparam name="T">A <see cref="ThemeServiceResponse"/>, or a subtype that adds payload fields.</typeparam>
	/// <param name="response">The raw response body returned by the ThemeService endpoint.</param>
	/// <param name="errorMessage">The failure diagnostic (as in the parameterless overload); otherwise <c>null</c>.</param>
	/// <param name="payload">The deserialized body on a non-failure JSON response; <c>null</c> for an empty body or a failure.</param>
	/// <returns><c>true</c> for an explicit <c>success:false</c> or a non-empty non-JSON body; otherwise <c>false</c>.</returns>
	public static bool TryGetFailure<T>(string response, out string errorMessage, out T payload)
		where T : ThemeServiceResponse {
		errorMessage = null;
		payload = null;
		if (string.IsNullOrWhiteSpace(response)) {
			return false;
		}
		try {
			payload = JsonSerializer.Deserialize<T>(response, ResponseJsonOptions);
		}
		catch (JsonException) {
			errorMessage = $"Unexpected response from server: {TextUtilities.SanitizeForDisplay(response)}";
			return true;
		}
		if (payload?.Success == false) {
			errorMessage = TextUtilities.SanitizeForDisplay(payload.ErrorInfo?.Message);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Builds the user-facing diagnostic for a failed ThemeService operation: the server-provided message when
	/// present, otherwise a generic "success=false" hint pointing at the Creatio application logs.
	/// </summary>
	/// <param name="operation">The ThemeService operation name (e.g. <c>CreateTheme</c>) used to prefix the message.</param>
	/// <param name="serverMessage">The server-provided failure message, if any.</param>
	/// <returns>A single diagnostic line describing the failure.</returns>
	public static string DescribeFailure(string operation, string serverMessage) {
		return string.IsNullOrWhiteSpace(serverMessage)
			? $"{operation} returned success=false. Check the Creatio application logs for details."
			: $"{operation} failed: {serverMessage}";
	}
}
