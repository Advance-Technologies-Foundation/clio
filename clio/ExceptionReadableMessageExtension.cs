using System;
using System.IO;
using System.Net;
using System.Text;
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
	/// Renders a failure that kept a server-authored excerpt: the carrier's own composed message,
	/// preceded by the context the outer exception adds and followed by the enrichment the ordinary arms
	/// would have contributed - plus, at debug verbosity, the scrubbed and fenced excerpt and the chain.
	/// </summary>
	/// <remarks>
	/// The inner chain is rendered as type name + sanitized, fenced message rather than raw, because an
	/// inner exception on this path is the fault the platform's own text came out of.
	/// <para>
	/// NOT a blanket short-circuit (PR #1374 review). This extension is the global CLI renderer for ~20
	/// commands, and <c>ClassifyingDataProvider</c> wraps the provider at both <c>BindingsModule</c>
	/// registrations, so this arm fires far beyond sys-settings. Replacing the outer exception outright
	/// cost three things at once: a command that wraps a provider failure to say WHICH operation failed
	/// printed only the inner carrier's message; the <see cref="WebException"/> enrichment arm below -
	/// whose own comment says it must precede the <see cref="InvalidOperationException"/> arm so the
	/// 401-vs-connect signal survives into the non-debug line CI reads - was preempted one arm higher up;
	/// and the debug render was strictly narrower than <c>ToString()</c>, printing the carrier's messages
	/// beside a stack trace belonging to a different exception. All three are addressed here: the outer's
	/// context is kept as a prefix, the nested-<see cref="WebException"/> enrichment is appended, and at
	/// debug the outer's type and message plus every inner ABOVE the carrier are rendered first.
	/// </para>
	/// </remarks>
	private static string RenderServerDetailCarrier(Exception carrier, Exception outer, bool debug)
		=> debug
			? RenderCarrierForDebug(carrier, outer)
			: RenderCarrierForConsole(carrier, outer);

	/// <summary>
	/// The single non-debug line: the outer exception's context, the carrier's console rendering, and the
	/// enrichment the ordinary arms of <see cref="GetReadableMessageException"/> would have contributed.
	/// </summary>
	private static string RenderCarrierForConsole(Exception carrier, Exception outer)
	{
		StringBuilder line = new();
		//The outer exception said WHICH operation failed; dropping it left the operator with the
		//provider's diagnosis and no idea which command produced it.
		if (!ReferenceEquals(outer, carrier) && DescribeOuterContext(outer, carrier) is { } prefix)
		{
			line.Append(prefix).Append(": ");
		}
		//The CONSOLE rendering when the carrier has one: Message keeps the agent fence for the MCP
		//envelope, and a terminal is not a model's context window (PR #1374 review).
		line.Append(carrier is IConsoleRenderedFailure consoleRendered
			? consoleRendered.ConsoleMessage
			: carrier.Message);
		//The enrichment the ordinary arms would have added. Without it a SessionRejectedException
		//wrapping a 401 WebException - exactly what Guard composes - lost the status.
		if (TryGetWebException(outer, out WebException nestedWebException))
		{
			line.Append(" (").Append(DescribeWebException(nestedWebException)).Append(')');
		}
		return line.ToString();
	}

	/// <summary>
	/// The debug render: everything <c>ToString()</c> would have shown down to the carrier, then the
	/// carrier's own message, its fenced server excerpt, its inner chain, and the outer's stack trace.
	/// </summary>
	private static string RenderCarrierForDebug(Exception carrier, Exception outer)
	{
		StringBuilder rendered = new();
		//At debug nothing may be silently narrower than exception.ToString(): the outer's own type and
		//message, and every inner ABOVE the carrier, are rendered before the carrier's own render. When
		//the carrier IS the outer there is nothing above it, and the render starts at its message - the
		//behaviour this arm already had.
		if (!ReferenceEquals(outer, carrier))
		{
			AppendChainAboveCarrier(rendered, carrier, outer);
		}
		rendered.Append(carrier.Message);
		if (carrier is IServerDetailCarrier { ServerDetail: { } detail }
			&& UntrustedText.Fenced(detail) is { } safeDetail)
		{
			rendered.Append(Environment.NewLine).Append("server detail: ").Append(safeDetail);
		}
		for (Exception inner = carrier.InnerException; inner != null; inner = inner.InnerException)
		{
			AppendTypeAndMessage(rendered, inner.GetType().Name, UntrustedText.Fenced(
				TextUtilities.SanitizeForDisplay(inner.Message, MaxRenderedInnerMessageLength)));
		}
		if (outer.StackTrace != null)
		{
			rendered.Append(Environment.NewLine).Append(outer.StackTrace);
		}
		return rendered.ToString();
	}

	/// <summary>
	/// Renders the outer exception and every inner one ABOVE <paramref name="carrier"/>, leaving
	/// <paramref name="rendered"/> positioned so the carrier's own message appends next.
	/// </summary>
	private static void AppendChainAboveCarrier(StringBuilder rendered, Exception carrier, Exception outer)
	{
		rendered.Append(outer.GetType().Name);
		if (!string.IsNullOrWhiteSpace(outer.Message))
		{
			rendered.Append(": ").Append(outer.Message);
		}
		for (Exception above = outer.InnerException;
			above != null && !ReferenceEquals(above, carrier);
			above = above.InnerException)
		{
			AppendTypeAndMessage(rendered, above.GetType().Name, above.Message);
		}
		rendered.Append(Environment.NewLine).Append("---> ").Append(carrier.GetType().Name).Append(": ");
	}

	/// <summary>Appends one <c>---&gt; Type: message</c> chain line, omitting an absent message.</summary>
	private static void AppendTypeAndMessage(StringBuilder rendered, string typeName, string message)
	{
		rendered.Append(Environment.NewLine).Append("---> ").Append(typeName);
		if (!string.IsNullOrWhiteSpace(message))
		{
			rendered.Append(": ").Append(message);
		}
	}

	/// <summary>Cap on an inner exception's message when it is rendered at debug verbosity.</summary>
	private const int MaxRenderedInnerMessageLength = 300;

	/// <summary>
	/// The context the outer exception adds over the carrier's own message, or <see langword="null"/>
	/// when it adds none.
	/// </summary>
	/// <remarks>
	/// An <see cref="AggregateException"/> is a container, not a fault - its message is a generic "One or
	/// more errors occurred", which is noise in front of a real diagnosis. A wrapper whose message
	/// already quotes the carrier's is skipped too, so the same sentence is not printed twice.
	/// </remarks>
	private static string DescribeOuterContext(Exception outer, Exception carrier)
	{
		if (outer is AggregateException || string.IsNullOrWhiteSpace(outer.Message))
		{
			return null;
		}
		string carrierMessage = carrier.Message ?? string.Empty;
		if (carrierMessage.Length > 0 && outer.Message.Contains(carrierMessage, StringComparison.Ordinal))
		{
			return null;
		}
		return outer.Message;
	}

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
