using System;

namespace Clio.Common;

/// <summary>
/// Picks the exception message to surface to an MCP caller. Single implementation shared by every MCP
/// error path (the call-tool filter and the nested <c>clio-run</c> dispatcher) so the two can never
/// disagree about which message an agent sees.
/// </summary>
public static class SurfacedExceptionMessage
{
	/// <summary>
	/// Returns the message that describes the failure best: normally the inner-most exception's, so a
	/// dispatch wrapper (for example <see cref="System.Reflection.TargetInvocationException"/>) never hides the
	/// real cause — but stopping at an <see cref="IAuthoritativeErrorMessage"/> exception, whose own message was
	/// built for the caller and whose inner exception is diagnostics only (ENG-93365).
	/// </summary>
	/// <param name="exception">The caught exception.</param>
	/// <returns>The message to surface.</returns>
	public static string Resolve(Exception exception) {
		ArgumentNullException.ThrowIfNull(exception);
		Exception current = exception;
		while (current.InnerException is not null && current is not IAuthoritativeErrorMessage) {
			current = current.InnerException;
		}
		return current.Message;
	}
}
