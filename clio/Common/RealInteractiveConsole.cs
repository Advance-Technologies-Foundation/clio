using System;

namespace Clio.Common;

/// <summary>
/// Production <see cref="IInteractiveConsole"/> for the interactive CLI. Fails closed (returns
/// <see langword="false"/> without reading the console) whenever standard input is redirected —
/// i.e. the stdio MCP server or a CI pipe — so a Safe-environment confirmation can never deadlock
/// on <see cref="Console.ReadKey()"/>. On a real terminal it prompts and reads a key as before.
/// </summary>
public sealed class RealInteractiveConsole : IInteractiveConsole {
	// CLIO001: stateless, behaviourless singleton for the few non-DI construction sites
	// (e.g. the direct `new SettingsRepository()` inside ConfigurationOptions). Constructed once
	// in a field initializer, never per-call; DI consumers still receive the registered instance.
	/// <summary>Shared stateless instance for non-DI call sites.</summary>
	public static readonly RealInteractiveConsole Shared = new();

	private readonly Func<bool> _isInputRedirected;
	private readonly Func<char> _readKey;

	/// <summary>Initializes a console bound to the real <see cref="Console"/>.</summary>
	public RealInteractiveConsole()
		: this(() => Console.IsInputRedirected, () => Console.ReadKey().KeyChar) { }

	// Test seam: inject the input-redirected probe and the keypress source so the confirmation
	// logic is unit-testable without a real terminal or redirected stdin.
	internal RealInteractiveConsole(Func<bool> isInputRedirected, Func<char> readKey) {
		_isInputRedirected = isInputRedirected;
		_readKey = readKey;
	}

	/// <inheritdoc />
	public bool Prompt(string message) {
		if (_isInputRedirected()) {
			// Non-interactive host (MCP stdio / CI pipe): we cannot ask and must not block — fail closed.
			return false;
		}
		// CLIO002: this is a genuine interactive terminal prompt, not application logging. The
		// paired Console.ReadKey() has no ILogger equivalent, and routing the prompt text through
		// ILogger (which may be buffered or redirected) would decouple it from the keypress it
		// guards. Direct Console use is correct here and mirrors the original Fill() behavior.
#pragma warning disable CLIO002
		Console.WriteLine(message);
		Console.Write("Do you want to continue? [Y/N]: ");
		char answer = _readKey();
		Console.WriteLine();
#pragma warning restore CLIO002
		return answer is 'y' or 'Y';
	}
}
