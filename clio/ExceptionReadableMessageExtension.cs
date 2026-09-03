using System;
using System.IO;
using System.Net;
using System.Text;
using Clio.Command.McpServer;
using Clio.Common;

namespace Clio;

internal static class ExceptionReadableMessageExtension
{
	public static string GetReadableMessageException(this Exception exception, bool debug = false)
	{
		// Issue #1333: a failure that carries server-authored text is rendered by ITS OWN rules, before
		// anything else - in both verbosities.
		//
		// Non-debug: the InvalidOperationException arm below returns `ex.InnerException?.Message ?? ex.Message`,
		// and DataProviderFailureException IS an InvalidOperationException whose inner is the original
		// parser fault. So `clio get-syssetting <code> -e <env>` against an expired password printed the
		// inner parser prose instead of the composed diagnostic that names both possible causes - losing
		// the diagnosis, and forwarding server-influenced text to the console.
		//
		// Debug: `exception.ToString()` dumps every inner Message raw - unscrubbed, unfenced, uncapped -
		// and never shows ServerDetail at all, so the excerpt an operator turns on --debug FOR was the one
		// thing missing.
		if (TryGetServerDetailCarrier(exception, out Exception carrier))
		{
			return RenderServerDetailCarrier(carrier, exception, debug);
		}
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
	/// Renders a failure that kept a server-authored excerpt: its own composed message (never an inner
	/// one), plus - at debug verbosity only - the scrubbed and fenced excerpt and the inner chain.
	/// </summary>
	/// <remarks>
	/// The inner chain is rendered as type name + sanitized, fenced message rather than raw, because an
	/// inner exception on this path is the fault the platform's own text came out of.
	/// </remarks>
	private static string RenderServerDetailCarrier(Exception carrier, Exception outer, bool debug)
	{
		string message = carrier.Message;
		if (!debug)
		{
			return message;
		}
		StringBuilder rendered = new(message);
		if (carrier is IServerDetailCarrier { ServerDetail: { } detail })
		{
			string safeDetail = SensitiveErrorTextRedactor.RedactUntrustedOrNull(detail);
			if (safeDetail != null)
			{
				rendered.Append(Environment.NewLine).Append("server detail: ").Append(safeDetail);
			}
		}
		for (Exception inner = carrier.InnerException; inner != null; inner = inner.InnerException)
		{
			string safeInner = SensitiveErrorTextRedactor.RedactUntrustedOrNull(
				TextUtilities.SanitizeForDisplay(inner.Message, MaxRenderedInnerMessageLength));
			rendered.Append(Environment.NewLine).Append("---> ").Append(inner.GetType().Name);
			if (safeInner != null)
			{
				rendered.Append(": ").Append(safeInner);
			}
		}
		if (outer.StackTrace != null)
		{
			rendered.Append(Environment.NewLine).Append(outer.StackTrace);
		}
		return rendered.ToString();
	}

	/// <summary>Cap on an inner exception's message when it is rendered at debug verbosity.</summary>
	private const int MaxRenderedInnerMessageLength = 300;

	/// <summary>
	/// Finds the failure in the chain that kept a server-authored excerpt, so its own message - not an
	/// inner one - is what a reader sees.
	/// </summary>
	private static bool TryGetServerDetailCarrier(Exception exception, out Exception carrier)
	{
		for (Exception current = exception; current != null; current = current.InnerException)
		{
			if (current is IServerDetailCarrier)
			{
				carrier = current;
				return true;
			}
			if (current is AggregateException { InnerExceptions.Count: 1 } aggregate
				&& aggregate.InnerExceptions[0] is IServerDetailCarrier)
			{
				carrier = aggregate.InnerExceptions[0];
				return true;
			}
		}
		carrier = null;
		return false;
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
