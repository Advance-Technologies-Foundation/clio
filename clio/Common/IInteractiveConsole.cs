namespace Clio.Common;

/// <summary>
/// Abstraction over interactive console confirmation prompts so that non-interactive hosts
/// (the stdio MCP server, CI) cannot deadlock on <see cref="System.Console.ReadKey()"/> when a
/// Safe-flagged (production) environment requires confirmation.
/// </summary>
public interface IInteractiveConsole {
	/// <summary>
	/// Whether the host can actually ask the user a question. It is <see langword="false"/> for every
	/// non-interactive context (redirected stdin / MCP / CI) and <see langword="true"/> only on a real
	/// terminal. Use it for <b>warn-and-proceed</b> confirmations that must fail <b>open</b> — a
	/// non-interactive host should continue without blocking rather than abort — in contrast to
	/// <see cref="Prompt(string)"/>, which fails closed (non-interactive returns <see langword="false"/>).
	/// </summary>
	bool IsInteractive { get; }

	/// <summary>
	/// Prompts the user with a yes/no confirmation question.
	/// </summary>
	/// <param name="message">The confirmation message to display before the prompt.</param>
	/// <returns>
	/// <see langword="true"/> only when the user explicitly confirms; otherwise <see langword="false"/>.
	/// Every non-interactive context (redirected stdin / MCP / CI) <b>fails closed</b> and returns
	/// <see langword="false"/> without blocking.
	/// </returns>
	bool Prompt(string message);
}
