using System.Text.Json;

namespace Clio.Command;

/// <summary>
/// The <c>BaseResponse</c> shape returned by every native Creatio <c>ThemeService</c> write endpoint
/// (<c>CreateTheme</c> / <c>UpdateTheme</c> / <c>DeleteTheme</c>): a success flag plus an optional
/// error block. Shared by the three theme write commands so the failure-detection rule is single-sourced.
/// </summary>
internal sealed record ThemeServiceResponse
{
	/// <summary>Whether the operation succeeded. A missing value is treated as success (the contract default).</summary>
	public bool? Success { get; init; }

	/// <summary>Diagnostic block populated by the service when <see cref="Success"/> is <c>false</c>.</summary>
	public ThemeServiceErrorInfo ErrorInfo { get; init; }
}

/// <summary>
/// The <c>errorInfo</c> block of a <see cref="ThemeServiceResponse"/>.
/// </summary>
internal sealed record ThemeServiceErrorInfo
{
	/// <summary>Server-side error classification (e.g. <c>SecurityException</c>, <c>ArgumentException</c>).</summary>
	public string ErrorCode { get; init; }

	/// <summary>Human-readable failure message surfaced to the caller.</summary>
	public string Message { get; init; }
}

/// <summary>
/// Parses a native <c>ThemeService</c> <c>BaseResponse</c> body: only an explicit <c>success:false</c> is a
/// failure; an empty or non-JSON body is tolerated as success so a contract drift does not surface as a false negative.
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
	/// On an explicit failure, the server-provided <c>errorInfo.message</c> (may be <c>null</c> when the server
	/// omits the block); otherwise <c>null</c>.
	/// </param>
	/// <returns>
	/// <c>true</c> when the body carries an explicit <c>success:false</c>; <c>false</c> for <c>success:true</c>,
	/// an empty body, or a non-JSON body.
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
			return false;
		}
		if (parsed?.Success == false) {
			errorMessage = parsed.ErrorInfo?.Message;
			return true;
		}
		return false;
	}
}
