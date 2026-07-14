using System.Collections.Generic;
using ClioRing.Diagnostics;
using ClioRing.Ipc;
using ClioRing.Models;
using ClioRing.Services;
using ClioRing.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClioRing;

/// <summary>
/// Composition root for the launcher. Registers behaviour services behind interfaces and the
/// view-models that consume them. Kept explicit (no assembly scanning) so it is AOT-safe.
/// </summary>
public static class Startup {
	/// <summary>Builds the application service provider.</summary>
	public static ServiceProvider BuildServiceProvider() {
		var services = new ServiceCollection();

		services.AddSingleton<IClioAdapter, ClioAdapter>();
		services.AddSingleton<IActionCatalogLoader, ActionCatalogLoader>();
		services.AddSingleton<IEnvStateStore, EnvStateStore>();
		services.AddSingleton<IWindowPlacementStore, WindowPlacementStore>();
		services.AddSingleton<IActionCatalogWatcher, ActionCatalogWatcher>();
		services.AddSingleton<IEnvironmentSettingsWatcher, EnvironmentSettingsWatcher>();
		services.AddSingleton<IClioSettingsStore, ClioSettingsStore>();

		// Per-run deployment/uninstall RECEIPT (story 10, ADR D6): NDJSON of the same authoritative
		// ClioStageEvent stream the pipeline UI renders, written to the active logs folder. Wired as a
		// side-observer of each form's pipeline in App (see ShowInstallWindow / ShowUninstallWindow) so the
		// on-disk record cannot disagree with what the user saw. Secret-free at source (D3); best-effort.
		services.AddSingleton(_ => new DeploymentReceipt(RingLog.LogsFolder));

		// EXPERIMENTAL clio MCP-over-stdio client. Always registered but lazy: the child is not spawned
		// until first use, and only the experiment-gated catalog view triggers that use. Off by default,
		// so a normal launch never contacts clio over IPC.
		services.AddSingleton<IClioIpcClient>(_ => new ClioIpcClient(ResolveClioIpcSettings(), StartupLog.Log));

		services.AddTransient<RingViewModel>();
		services.AddTransient<ClioIpcViewModel>();
		services.AddTransient<ClioWorkflowViewModel>();

		// Guided Creatio Install form (story 8). Live deploy is ENABLED: the honest-failure work (story 12)
		// and typed progress forwarding (story 4) have landed, so a human-driven Install click runs the real
		// clio deploy-creatio and the step pipeline renders live progress. Not agent-initiated — user-gated.
		services.AddTransient<InstallFormViewModel>(sp =>
			new InstallFormViewModel(sp.GetRequiredService<IClioIpcClient>(), liveDeployEnabled: true));

		// Guided Creatio Uninstall flow (story 9). Live uninstall is ENABLED: the clio live-uninstall work
		// (stories 3 + 4) has landed, so a confirmed Yes runs the real uninstall and the pipeline renders live
		// progress. The environment list comes from the same source the ring uses (IClioAdapter), local-filtered.
		services.AddTransient<UninstallFormViewModel>(sp =>
			new UninstallFormViewModel(sp.GetRequiredService<IClioAdapter>(), sp.GetRequiredService<IClioIpcClient>(), liveUninstallEnabled: true));

		return services.BuildServiceProvider();
	}

	/// <summary>
	/// True when the clio IPC experiment is enabled in <c>app-settings.json</c> (<c>Experiments.ClioIpc</c>).
	/// Fail-closed: any missing/unreadable setting means disabled.
	/// </summary>
	public static bool IsClioIpcExperimentEnabled() =>
		AppSettingsReader.TryRead()?.Experiments?.ClioIpc == true;

	/// <summary>
	/// Resolves the clio MCP child launch configuration from <c>app-settings.json</c>. Precedence: a valid
	/// <c>DevClioPath</c> dev-build override wins; then an explicit <c>ClioIpc</c> section; otherwise the
	/// machine <see cref="ClioIpcSettings.Default"/>.
	/// </summary>
	public static ClioIpcSettings ResolveClioIpcSettings() {
		AppSettings? settings = AppSettingsReader.TryRead();
		return ResolveClioIpcSettings(settings?.DevClioPath, settings?.ClioIpc);
	}

	/// <summary>
	/// Pure resolution of the launch configuration from its two inputs (test seam). A valid
	/// <paramref name="devClioPath"/> takes precedence over the <paramref name="ipc"/> section, which in
	/// turn takes precedence over <see cref="ClioIpcSettings.Default"/>.
	/// </summary>
	/// <param name="devClioPath">The optional dev-clio build override path.</param>
	/// <param name="ipc">The optional explicit <c>ClioIpc</c> section.</param>
	/// <returns>The resolved launch configuration.</returns>
	public static ClioIpcSettings ResolveClioIpcSettings(string? devClioPath, ClioIpcSettingsDto? ipc) {
		if (DevClioLaunch.Validate(devClioPath).IsValid && !string.IsNullOrWhiteSpace(devClioPath)) {
			return DevClioLaunch.Build(devClioPath);
		}

		if (ipc is null || string.IsNullOrWhiteSpace(ipc.Command) || ipc.Args is not { Length: > 0 }) {
			return ClioIpcSettings.Default;
		}
		return new ClioIpcSettings {
			Command = ipc.Command,
			Args = new List<string>(ipc.Args),
			WorkingDirectory = string.IsNullOrWhiteSpace(ipc.WorkingDirectory) ? null : ipc.WorkingDirectory
		};
	}
}
