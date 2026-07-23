using System;
using System.Threading;
using Clio.Command;

namespace Clio.Common;

/// <summary>
/// Target and timing parameters for a <see cref="IServerReadinessWaiter"/> wait.
/// </summary>
public sealed record ServerReadinessOptions {

	/// <summary>Base application uri to probe.</summary>
	public required string Uri { get; init; }

	/// <summary>Whether the target instance runs on .NET Core (WebAppLoader) or .NET Framework (WebHost).</summary>
	public required bool IsNetCore { get; init; }

	/// <summary>Total time budget to wait for readiness before giving up.</summary>
	public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(600);

	/// <summary>Delay before the first probe. The previous app domain may still answer briefly after a
	/// restart request, so an immediate probe risks a false-ready result.</summary>
	public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>Delay between subsequent probes.</summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

}

/// <summary>
/// Polls a Creatio instance's health-check endpoint until it responds successfully or a timeout elapses.
/// </summary>
public interface IServerReadinessWaiter {

	/// <summary>
	/// Waits for the target instance to answer its health-check endpoint.
	/// </summary>
	/// <param name="options">Target uri, host type, and timing budget.</param>
	/// <returns><c>true</c> when a probe succeeded within the timeout; otherwise <c>false</c>.</returns>
	bool WaitForReady(ServerReadinessOptions options);

}

/// <inheritdoc cref="IServerReadinessWaiter"/>
public class ServerReadinessWaiter(HealthCheckCommand healthCheckCommand, ILogger logger) : IServerReadinessWaiter {

	/// <summary>
	/// Test seam replacing <see cref="Thread.Sleep(TimeSpan)"/> so unit tests can exercise the wait loop
	/// without real delays.
	/// </summary>
	internal Action<TimeSpan> Sleep { get; set; } = Thread.Sleep;

	/// <inheritdoc/>
	public bool WaitForReady(ServerReadinessOptions options) {
		logger.WriteInfo($"Waiting {options.InitialDelay.TotalSeconds:0} seconds for server to start...");
		Sleep(options.InitialDelay);

		// Start the timeout budget AFTER the initial delay: the delay is a fixed pre-condition, not part
		// of the probing window. Computing the deadline before the delay meant any Timeout <= InitialDelay
		// (e.g. --ready-timeout 5 with the 10s default delay) elapsed before the loop ran and returned a
		// false "not ready" for a healthy instance. The do/while also guarantees at least one probe even
		// when the caller passes a tiny or non-positive Timeout.
		DateTime deadlineUtc = DateTime.UtcNow + options.Timeout;

		int attempt = 0;
		do {
			attempt++;
			HealthCheckOptions healthOptions = new() {
				Uri = options.Uri,
				IsNetCore = options.IsNetCore
			};
			int result = healthCheckCommand.Execute(healthOptions);
			if (result == 0) {
				logger.WriteInfo($"Server is ready after {attempt} attempt(s).");
				return true;
			}

			if (DateTime.UtcNow >= deadlineUtc) {
				break;
			}

			logger.WriteInfo(
				$"Waiting for server to become ready... (attempt {attempt}). Next check in {options.PollInterval.TotalSeconds:0} seconds.");
			Sleep(options.PollInterval);
		} while (DateTime.UtcNow < deadlineUtc);

		logger.WriteWarning($"Server did not become ready within {options.Timeout.TotalSeconds:0} seconds.");
		return false;
	}

}
