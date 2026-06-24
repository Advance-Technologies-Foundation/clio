using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command;

/// <summary>
/// The <c>BaseResponse</c> shape returned by the native Creatio <c>ThemeService</c> endpoints: a success flag
/// plus an optional error block.
/// </summary>
internal sealed record ThemeServiceResponse
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
	/// On an explicit <c>success:false</c>, the server-provided <c>errorInfo.message</c> (may be <c>null</c> when
	/// the server omits the block); on a non-empty, non-JSON body, an "Unexpected response from server"
	/// diagnostic carrying the raw body; otherwise <c>null</c>.
	/// </param>
	/// <returns>
	/// <c>true</c> when the body carries an explicit <c>success:false</c> or is a non-empty, non-JSON body;
	/// <c>false</c> for <c>success:true</c> or an empty body.
	/// </returns>
	public static bool TryGetFailure(string response, out string errorMessage) {
		errorMessage = null;
		if (string.IsNullOrWhiteSpace(response)) {
			return false;
		}
		ThemeServiceResponse parsed;
		try {
			parsed = JsonSerializer.Deserialize<ThemeServiceResponse>(response, ResponseJsonOptions);
		}
		catch (JsonException) {
			errorMessage = $"Unexpected response from server: {response}";
			return true;
		}
		if (parsed?.Success == false) {
			errorMessage = parsed.ErrorInfo?.Message;
			return true;
		}
		return false;
	}
}
