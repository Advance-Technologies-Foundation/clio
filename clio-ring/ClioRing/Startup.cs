using System;
using System.Collections.Generic;
using System.Net.Http;
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

		services.AddSingleton<IActionCatalogLoader, ActionCatalogLoader>();
		services.AddSingleton<IEnvStateStore, EnvStateStore>();
		services.AddSingleton<IWindowPlacementStore, WindowPlacementStore>();
		services.AddSingleton<IActionCatalogWatcher, ActionCatalogWatcher>();
		services.AddSingleton<IEnvironmentSettingsWatcher, EnvironmentSettingsWatcher>();
		services.AddSingleton<IClioSettingsStore, ClioSettingsStore>();
		services.AddSingleton<IClioProcessGate, ClioProcessGate>();
		services.AddSingleton<HttpClient>();
		services.AddSingleton<IClioToolProcessRunner, ClioToolProcessRunner>();
		services.AddSingleton<IClioToolProcessInspector, ClioToolProcessInspector>();
		services.AddSingleton<IClioToolInstallation, ClioToolInstallation>();
		services.AddSingleton<IClioUpdateStateStore, ClioUpdateStateStore>();
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<IClioToolUpdateService, ClioToolUpdateService>();
		ResolvedClioRuntime clioRuntime = ResolveClioRuntime();
		services.AddSingleton(clioRuntime);
		services.AddSingleton<IClioAdapter, ClioAdapter>();

		// Per-run deployment/uninstall RECEIPT (story 10, ADR D6): NDJSON of the same authoritative
		// ClioStageEvent stream the pipeline UI renders, written to the active logs folder. Wired as a
		// side-observer of each form's pipeline in App (see ShowInstallWindow / ShowUninstallWindow) so the
		// on-disk record cannot disagree with what the user saw. Secret-free at source (D3); best-effort.
		services.AddSingleton(_ => new DeploymentReceipt(RingLog.LogsFolder));

		// EXPERIMENTAL clio MCP-over-stdio client. Always registered but lazy: the child is not spawned
		// until first use, and only the experiment-gated catalog view triggers that use. Off by default,
		// so a normal launch never contacts clio over IPC.
		services.AddSingleton<IClioIpcClient>(sp => new ClioIpcClient(clioRuntime.LaunchSettings,
			StartupLog.Log, clioRuntime.Mode == ClioRuntimeMode.Release
				? sp.GetRequiredService<IClioProcessGate>()
				: new ClioProcessGate()));

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
	/// Resolves the clio MCP child launch configuration from <c>app-settings.json</c>. Release mode uses the
	/// installed clio dotnet tool. Development mode uses a valid <c>DevClioPath</c>, then an explicit
	/// <c>ClioIpc</c> target. Legacy settings infer Development when either target is present.
	/// </summary>
	public static ClioIpcSettings ResolveClioIpcSettings() {
		return ResolveClioRuntime().LaunchSettings;
	}

	/// <summary>Resolves the immutable runtime decision used by both process launch and the main UI.</summary>
	/// <returns>The active runtime mode and its exact launch settings.</returns>
	public static ResolvedClioRuntime ResolveClioRuntime() {
		AppSettings? settings = AppSettingsReader.TryRead();
		return ResolveClioRuntime(settings?.ClioRuntimeMode, settings?.DevClioPath, settings?.ClioIpc);
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
		return ResolveClioIpcSettings(runtimeMode: null, devClioPath, ipc);
	}

	/// <summary>Pure runtime-mode resolution used by startup and tests.</summary>
	/// <param name="runtimeMode">Explicit <c>release</c>/<c>development</c> choice, or null for migration inference.</param>
	/// <param name="devClioPath">The optional development clio build path.</param>
	/// <param name="ipc">The optional explicit development child-process configuration.</param>
	/// <returns>The selected child-process launch settings.</returns>
	public static ClioIpcSettings ResolveClioIpcSettings(string? runtimeMode, string? devClioPath,
		ClioIpcSettingsDto? ipc) {
		return ResolveClioRuntime(runtimeMode, devClioPath, ipc).LaunchSettings;
	}

	/// <summary>Pure runtime decision used by startup and tests.</summary>
	/// <param name="runtimeMode">Explicit release/development choice, or null for migration inference.</param>
	/// <param name="devClioPath">Optional validated development clio path.</param>
	/// <param name="ipc">Optional explicit development IPC target.</param>
	/// <returns>The selected mode and exact launch settings.</returns>
	public static ResolvedClioRuntime ResolveClioRuntime(string? runtimeMode, string? devClioPath,
		ClioIpcSettingsDto? ipc) {
		bool hasValidPath = DevClioLaunch.Validate(devClioPath).IsValid
			&& !string.IsNullOrWhiteSpace(devClioPath);
		bool hasExplicitIpc = ClioRuntimeConfiguration.IsValidExplicitIpc(ipc?.Command, ipc?.Args);
		bool useDevelopment = string.Equals(runtimeMode, "development", System.StringComparison.OrdinalIgnoreCase)
			|| (string.IsNullOrWhiteSpace(runtimeMode) && (hasValidPath || hasExplicitIpc));
		if (!useDevelopment || string.Equals(runtimeMode, "release", System.StringComparison.OrdinalIgnoreCase)) {
			return new ResolvedClioRuntime(ClioRuntimeMode.Release, ClioIpcSettings.Default);
		}

		if (hasValidPath) {
			return new ResolvedClioRuntime(ClioRuntimeMode.Development, DevClioLaunch.Build(devClioPath!));
		}

		if (!hasExplicitIpc) {
			return new ResolvedClioRuntime(ClioRuntimeMode.Release, ClioIpcSettings.Default,
				ClioRuntimeMode.Development,
				"Development was selected, but its saved clio target is invalid. Open Settings to configure it.");
		}
		var settings = new ClioIpcSettings {
			Command = ipc!.Command!,
			Args = new List<string>(ipc.Args!),
			WorkingDirectory = string.IsNullOrWhiteSpace(ipc.WorkingDirectory) ? null : ipc.WorkingDirectory
		};
		return new ResolvedClioRuntime(ClioRuntimeMode.Development, settings);
	}
}
