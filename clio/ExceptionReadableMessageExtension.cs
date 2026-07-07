using System;
using System.IO;
using System.Net;

namespace Clio;

internal static class ExceptionReadableMessageExtension
{
	public static string GetReadableMessageException(this Exception exception, bool debug = false)
	{
		if (debug) return exception.ToString();
		return exception switch
		{
			AggregateException ex when ex.InnerException != null
				=> ex.InnerException.GetReadableMessageException(debug),
			WebException ex when ex.Status == WebExceptionStatus.ConnectFailure
				=> $"Cannot connect to the application: {ex.Message} ({DescribeWebException(ex)}). "
					+ "Make sure the site is running and accessible.",
			WebException ex => $"{ex.Message} ({DescribeWebException(ex)})",
			FileNotFoundException ex => $"{ex.Message}{ex.FileName}",
			// Must precede the InvalidOperationException arm: an IOE (or any wrapper) whose inner chain
			// carries a WebException should still surface the structured "(WebException: <status> …)"
			// enrichment, otherwise the IOE arm below would shadow it and drop the 401-vs-connect signal.
			_ when TryGetWebException(exception, out WebException nestedWebException)
				=> $"{exception.Message} ({DescribeWebException(nestedWebException)})",
			InvalidOperationException ex => ex.InnerException?.Message ?? ex.Message,
			_ => exception.Message
		};
	}

	/// <summary>
	/// Builds a compact, non-debug-friendly description of a <see cref="WebException"/> that always
	/// includes its <see cref="WebException.Status"/> and, when the response is an
	/// <see cref="HttpWebResponse"/>, the HTTP status code and reason — e.g.
	/// <c>WebException: ProtocolError (HTTP 401 Unauthorized)</c> or <c>WebException: ConnectFailure</c>.
	/// This is what lets CI (which runs non-debug) tell an auth failure apart from a connect/timeout
	/// failure when only the readable message is logged.
	/// </summary>
	private static string DescribeWebException(WebException exception)
	{
		string detail = $"WebException: {exception.Status}";
		if (exception.Response is HttpWebResponse httpResponse)
		{
			detail += $" (HTTP {(int)httpResponse.StatusCode} {httpResponse.StatusCode})";
		}
		return detail;
	}

	/// <summary>
	/// Walks the inner-exception chain looking for a <see cref="WebException"/> so that an HTTP
	/// failure wrapped in another exception type still surfaces its status in the readable message.
	/// </summary>
	private static bool TryGetWebException(Exception exception, out WebException webException)
	{
		for (Exception current = exception.InnerException; current != null; current = current.InnerException)
		{
			if (current is WebException found)
			{
				webException = found;
				return true;
			}
		}
		webException = null;
		return false;
	}
}
