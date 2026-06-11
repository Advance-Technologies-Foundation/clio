namespace Clio.Common;

/// <summary>
/// <see cref="IInteractiveConsole"/> for explicitly non-interactive hosts. Always declines
/// (returns <see langword="false"/>) without touching the console, so a Safe-environment
/// confirmation fails closed instead of blocking. Used in tests and any host that opts out of
/// interactive prompting; production CLI uses <see cref="RealInteractiveConsole"/>, which already
/// fails closed on redirected stdin.
/// </summary>
public sealed class NonInteractiveConsole : IInteractiveConsole {
	private readonly ILogger _logger;

	/// <summary>Initializes a non-interactive console that logs a warning when it declines.</summary>
	/// <param name="logger">Optional logger used to surface the declined confirmation.</param>
	public NonInteractiveConsole(ILogger logger = null) {
		_logger = logger;
	}

	/// <inheritdoc />
	public bool Prompt(string message) {
		_logger?.WriteWarning(
			$"Safe-environment confirmation required but the context is non-interactive; declining. {message}");
		return false;
	}
}
