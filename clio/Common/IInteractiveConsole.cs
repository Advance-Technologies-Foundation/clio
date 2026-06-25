namespace Clio.Common;

/// <summary>
/// Abstraction over interactive console confirmation prompts so that non-interactive hosts
/// (the stdio MCP server, CI) cannot deadlock on <see cref="System.Console.ReadKey()"/> when a
/// Safe-flagged (production) environment requires confirmation.
/// </summary>
public interface IInteractiveConsole {
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
