using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("restart-web-app", Aliases = ["restart"], HelpText = "Restart a web application")]
public class RestartOptions : RemoteCommandOptions {

	/// <summary>
	/// Hard upper bound for <see cref="ReadyTimeout"/> / the MCP <c>waitTimeoutSeconds</c> parameter (review
	/// Finding 3, ENG-91315). The readiness wait now runs detached and lock-free while pinning the (non-evictable)
	/// session container for its whole duration, so an unbounded, caller-chosen timeout could pin a container
	/// arbitrarily long. 3600s (1h) is well above the 600s default — generous for genuinely slow environments —
	/// while still bounding the pin. Adjustable if a longer legitimate wait is ever needed.
	/// </summary>
	internal const int MaxReadyTimeoutSeconds = 3600;

	[Option("wait-ready", Required = false, Default = false,
		HelpText = "After requesting the restart, poll the application's health-check endpoint until it answers before returning")]
	public bool WaitReady { get; set; }

	[Option("ready-timeout", Required = false, Default = 600,
		HelpText = "Max seconds to wait for readiness when --wait-ready is set (default: 600, max: 3600)")]
	public int ReadyTimeout { get; set; }

}

public class RestartCommand : RemoteCommand<RestartOptions> {

	#region Fields: Private

	private readonly IServerReadinessWaiter _readinessWaiter;

	#endregion

	#region Constructors: Public

	public RestartCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServerReadinessWaiter readinessWaiter)
		: base(applicationClient, settings) {
		_readinessWaiter = readinessWaiter;
	}

	#endregion

	#region Properties: Protected

	protected override string ServicePath =>
		EnvironmentSettings.IsNetCore ? "/ServiceModel/AppInstallerService.svc/RestartApp"
			: @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";

	#endregion

	#region Methods: Public

	/// <summary>
	/// Requests the restart and, when <see cref="RestartOptions.WaitReady"/> is set, polls the instance's
	/// health-check endpoint until it responds or <see cref="RestartOptions.ReadyTimeout"/> elapses.
	/// </summary>
	/// <param name="options">Restart options, including the readiness-wait toggle and timeout.</param>
	/// <returns>0 on success (and, when waiting, on confirmed readiness); 1 on failure or a readiness timeout.</returns>
	public override int Execute(RestartOptions options) {
		int result = base.Execute(options);
		if (result != 0 || !options.WaitReady) {
			return result;
		}

		return WaitForReadiness(options) ? 0 : 1;
	}

	/// <summary>
	/// Polls the instance's health-check endpoint until it answers or <see cref="RestartOptions.ReadyTimeout"/>
	/// elapses, WITHOUT issuing the restart request. Exposed separately from <see cref="Execute"/> so the MCP
	/// restart tools can run the restart request under the per-tenant execution lock, release it, and then run
	/// this read-only wait lock-free — the multi-minute warm-up must not serialize other same-tenant calls
	/// (ENG-91315, review Finding 2). <see langword="virtual"/> so unit tests can fake the wait.
	/// </summary>
	/// <param name="options">Restart options carrying the readiness timeout.</param>
	/// <returns><c>true</c> when the instance answered within the timeout; otherwise <c>false</c>.</returns>
	public virtual bool WaitForReadiness(RestartOptions options) =>
		_readinessWaiter.WaitForReady(new ServerReadinessOptions {
			Uri = EnvironmentSettings.Uri,
			IsNetCore = EnvironmentSettings.IsNetCore,
			// Clamp to a sane ceiling (Finding 3): this wait pins the session container for its whole duration,
			// so an unbounded caller-chosen timeout is a hardening gap. Bounds both the CLI --ready-timeout and
			// the MCP waitTimeoutSeconds paths, which both funnel through here.
			Timeout = TimeSpan.FromSeconds(Math.Clamp(options.ReadyTimeout, 1, RestartOptions.MaxReadyTimeoutSeconds))
		});

	#endregion

}
