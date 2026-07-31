using Microsoft.Extensions.DependencyInjection;

namespace Clio.Common;

/// <summary>
/// <see cref="IInteractiveConsole"/> for explicitly non-interactive hosts. Always declines
/// (returns <see langword="false"/>) without touching the console, so a Safe-environment
/// confirmation fails closed instead of blocking. Used in tests and any host that opts out of
/// interactive prompting; production CLI uses <see cref="RealInteractiveConsole"/>, which already
/// fails closed on redirected stdin.
/// </summary>
public sealed class NonInteractiveConsole : IInteractiveConsole {
	// CLIO001: stateless, behaviourless singleton for non-DI call sites that must force a non-interactive
	// console (e.g. the MCP per-request child containers in ToolCommandResolver). Constructed once in a
	// field initializer, never per-call; DI consumers still receive their registered instance.
	/// <summary>Shared stateless instance for non-DI call sites that force non-interactive behavior.</summary>
	public static readonly NonInteractiveConsole Shared = new();

	private readonly ILogger _logger;

	/// <summary>Initializes a non-interactive console that logs a warning when it declines.</summary>
	/// <param name="logger">Optional logger used to surface the declined confirmation.</param>
	public NonInteractiveConsole(ILogger logger = null) {
		_logger = logger;
	}

	/// <summary>
	/// Registers the shared non-interactive console into a child DI container, overriding the default
	/// <see cref="RealInteractiveConsole"/>. Use it for every automation host that builds its own container
	/// and resolves commands to run without a human at the console — the per-request MCP child containers
	/// (<c>ToolCommandResolver</c>) and scenario steps (<c>ScenarioRunnerCommand</c>) — so a command that
	/// runs a warn-and-proceed confirmation (e.g. compile-creatio's, ENG-93157) fails OPEN (proceeds) by
	/// construction instead of blocking on <see cref="System.Console.ReadKey()"/> on an attached TTY.
	/// </summary>
	/// <param name="services">The child container's service collection.</param>
	public static void ForceInContainer(IServiceCollection services) =>
		services.AddSingleton<IInteractiveConsole>(Shared);

	/// <inheritdoc />
	public bool IsInteractive => false;

	/// <inheritdoc />
	public bool Prompt(string message) {
		// General-purpose fail-closed confirmation: any non-interactive caller (a Safe-environment guard,
		// a warn-and-proceed compile confirmation, …) declines here without touching the console.
		_logger?.WriteWarning(
			$"Confirmation required but the context is non-interactive; declining. {message}");
		return false;
	}
}
