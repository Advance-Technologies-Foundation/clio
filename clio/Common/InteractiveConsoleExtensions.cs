using System;

namespace Clio.Common;

/// <summary>
/// Helpers over <see cref="IInteractiveConsole"/> for warn-and-proceed confirmations.
/// </summary>
public static class InteractiveConsoleExtensions {

	/// <summary>
	/// Exit code returned by a heavy operation whose warn-and-proceed confirmation the user declined
	/// (postponed). It is deliberately non-zero and distinct from the generic failure code so in-process
	/// callers (for example <c>push-package --force-compilation</c>) and shell <c>&amp;&amp;</c> chains can tell
	/// "the operation was declined / did not run" apart from "it ran successfully" (ENG-93157, RC-10).
	/// </summary>
	public const int DeclinedExitCode = 2;

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

	/// <summary>
	/// Shared warn/confirm/postpone orchestration for a heavy operation, so every caller enforces the
	/// invariant identically (ENG-93157, RC-6). Proceeds (returns <see langword="true"/>) when
	/// <paramref name="isSilent"/> is set (<c>--silent</c> = default behavior without interaction) or when
	/// <see cref="ConfirmOrProceedWhenNonInteractive"/> proceeds; otherwise (an interactive user declined)
	/// logs <paramref name="postponeHint"/> and returns <see langword="false"/>. The caller maps a
	/// <see langword="false"/> result to <see cref="DeclinedExitCode"/>.
	/// </summary>
	/// <param name="console">The interactive console seam.</param>
	/// <param name="isSilent">Whether <c>--silent</c> was requested (skip the prompt and proceed).</param>
	/// <param name="warning">The heavy-operation warning shown before the yes/no prompt.</param>
	/// <param name="logger">Logger used to surface the run-later hint when the user postpones.</param>
	/// <param name="postponeHint">The "how to run it later" hint logged when the user declines.</param>
	/// <returns><see langword="true"/> to proceed; <see langword="false"/> only when an interactive user declines.</returns>
	public static bool ConfirmHeavyOperation(this IInteractiveConsole console, bool isSilent, string warning,
		ILogger logger, string postponeHint) {
		ArgumentNullException.ThrowIfNull(console);
		ArgumentNullException.ThrowIfNull(logger);
		if (isSilent || console.ConfirmOrProceedWhenNonInteractive(warning)) {
			return true;
		}
		logger.WriteInfo(postponeHint);
		return false;
	}
}
