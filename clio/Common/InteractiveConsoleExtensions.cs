using System;

namespace Clio.Common;

/// <summary>
/// Helpers over <see cref="IInteractiveConsole"/> for warn-and-proceed confirmations.
/// </summary>
public static class InteractiveConsoleExtensions {

	/// <summary>
	/// Warn-and-proceed confirmation that fails <b>open</b>: returns <see langword="true"/> (proceed)
	/// when the host is non-interactive (redirected stdin / MCP / CI — nothing to ask) or when an
	/// interactive user confirms the prompt; returns <see langword="false"/> only when an interactive
	/// user explicitly declines. This is the opposite direction from <see cref="IInteractiveConsole.Prompt"/>,
	/// which fails closed. Use it for non-destructive "are you sure?" gates that must never block or
	/// abort an automated host (ENG-93157).
	/// </summary>
	/// <param name="console">The interactive console seam.</param>
	/// <param name="warning">The warning message shown before the yes/no prompt on an interactive terminal.</param>
	/// <returns><see langword="true"/> to proceed; <see langword="false"/> only when an interactive user declines.</returns>
	public static bool ConfirmOrProceedWhenNonInteractive(this IInteractiveConsole console, string warning) {
		ArgumentNullException.ThrowIfNull(console);
		return !console.IsInteractive || console.Prompt(warning);
	}
}
