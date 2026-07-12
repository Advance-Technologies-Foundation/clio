using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClioLauncher.Ipc;
using ClioLauncher.Services;
using ClioLauncher.ViewModels;
using ClioLauncher.Views;

namespace ScreenshotTool;

/// <summary>
/// Offscreen renders of the EXPERIMENTAL clio workflow window using design seams only — no clio child,
/// no network. Mirrors the ring capture harness (matte-composited PNGs at 1x/1.5x).
/// </summary>
internal static class WorkflowCaptures {
	public static int CaptureAll(string outDir) {
		int count = 0;

		// 1) DEFAULT — connected, env selected, idle.
		count += Capture(outDir, "workflow-default", vm => {
			vm.DesignSetConnection("connected", "clio 8.1.0.77");
			vm.DesignSetEnvironment("ve");
		});

		// 2) LIST-PACKAGES — success with parsed card rows.
		count += Capture(outDir, "workflow-packages", vm => {
			vm.DesignSetConnection("connected", "clio 8.1.0.77");
			vm.DesignSetEnvironment("ve");
			vm.DesignShowRun("clio list-packages · env=ve", WorkflowRunState.Success,
				"list-packages (isError=False)",
				new[] {
					"CrtBase  ·  8.1.5.2176  ·  Terrasoft",
					"CrtCore  ·  8.1.5.2176  ·  Terrasoft",
					"UsrCustomApp  ·  1.0.3  ·  k.krylov",
				});
		});

		// 3) RESTART CONFIRM — destructive typed-confirm overlay.
		count += Capture(outDir, "workflow-restart-confirm", vm => {
			vm.DesignSetConnection("connected", "clio 8.1.0.77");
			vm.DesignSetEnvironment("ve");
			vm.DesignShowConfirm("restart");
		});

		// 4) RUNNING — live heartbeat/activity (keep-alive beats, not incremental stdout), RUNNING badge.
		count += Capture(outDir, "workflow-running", vm => {
			vm.DesignSetConnection("connected", "clio 8.1.0.77");
			vm.DesignSetEnvironment("ve");
			vm.DesignShowRun("clio gate · env=ve", WorkflowRunState.Running,
				"Running install-gate {\"environment-name\":\"ve\"}…\n" +
				"· still working (heartbeat)…\n" +
				"· still working (heartbeat)…\n" +
				"(output + result arrive when the operation completes)");
		});

		// 5) INSTALL FORM — Rancher source (default Kubernetes db/redis; nothing to choose).
		count += CaptureInstall(outDir, "install-form-rancher", vm => vm.DesignPopulate(local: false));

		// 6) INSTALL FORM — Local source (database + Redis pickers visible) + a sample pipeline.
		count += CaptureInstall(outDir, "install-form-local", vm => vm.DesignPopulate(local: true));

		// 7) UNINSTALL FLOW — local-env picker + open Yes/No confirm + a successful sample pipeline.
		count += CaptureUninstall(outDir, "uninstall-flow-confirm", vm => vm.DesignPopulate(succeed: true));

		// 8) UNINSTALL FLOW — the AC-ERR case: config-read fails, run fails, environment left registered.
		count += CaptureUninstall(outDir, "uninstall-flow-config-failure", vm => vm.DesignPopulate(succeed: false));

		return count;
	}

	private static int CaptureUninstall(string outDir, string name, Action<UninstallFormViewModel> arrange) {
		int n = 0;
		foreach (double scale in new[] { 1.0, 1.5 }) {
			var vm = new UninstallFormViewModel(new SampleClioAdapter(), new StubIpcClient()) { AutoDiscoverOnOpen = false };
			var window = new UninstallWindow(vm);
			window.Show();
			Program.Pump();

			arrange(vm);
			Program.Pump();

			RenderTargetBitmap? rtb = Program.RenderMatted(window, scale);
			window.Close();
			if (rtb is null) {
				continue;
			}
			string suffix = scale == 1.0 ? "@1x" : "@1.5x";
			string path = Path.Combine(outDir, $"{name}{suffix}.png");
			rtb.Save(path, new PngBitmapEncoderOptions());
			Console.WriteLine($"Saved {path} ({rtb.PixelSize.Width}x{rtb.PixelSize.Height})");
			n++;
		}
		return n;
	}

	private static int CaptureInstall(string outDir, string name, Action<InstallFormViewModel> arrange) {
		int n = 0;
		foreach (double scale in new[] { 1.0, 1.5 }) {
			var vm = new InstallFormViewModel(new StubIpcClient()) { AutoDiscoverOnOpen = false };
			var window = new InstallWindow(vm);
			window.Show();
			Program.Pump();

			arrange(vm);
			Program.Pump();

			RenderTargetBitmap? rtb = Program.RenderMatted(window, scale);
			window.Close();
			if (rtb is null) {
				continue;
			}
			string suffix = scale == 1.0 ? "@1x" : "@1.5x";
			string path = Path.Combine(outDir, $"{name}{suffix}.png");
			rtb.Save(path, new PngBitmapEncoderOptions());
			Console.WriteLine($"Saved {path} ({rtb.PixelSize.Width}x{rtb.PixelSize.Height})");
			n++;
		}
		return n;
	}

	private static int Capture(string outDir, string name, Action<ClioWorkflowViewModel> arrange) {
		int n = 0;
		foreach (double scale in new[] { 1.0, 1.5 }) {
			var vm = new ClioWorkflowViewModel(new StubIpcClient(), new InMemoryEnvStateStore(new ClioLauncher.Models.EnvState { Selected = "ve" }));
			var window = new ClioWorkflowWindow(vm);
			window.Show();
			Program.Pump();

			arrange(vm);
			Program.Pump();

			RenderTargetBitmap? rtb = Program.RenderMatted(window, scale);
			window.Close();
			if (rtb is null) {
				continue;
			}
			string suffix = scale == 1.0 ? "@1x" : "@1.5x";
			string path = Path.Combine(outDir, $"{name}{suffix}.png");
			rtb.Save(path, new PngBitmapEncoderOptions());
			Console.WriteLine($"Saved {path} ({rtb.PixelSize.Width}x{rtb.PixelSize.Height})");
			n++;
		}
		return n;
	}
}

/// <summary>No-op <see cref="IClioIpcClient"/> for offscreen renders (design seams never call it).</summary>
internal sealed class StubIpcClient : IClioIpcClient {
	public bool IsConnected => true;
	public ClioServerHandshake? Handshake => null;
	public string TargetPath => "(stub)";
	public bool LastCatalogIsModern => true;
#pragma warning disable CS0067 // event never used in the stub
	public event EventHandler? Disconnected;
#pragma warning restore CS0067

	public Task<ClioServerHandshake> ConnectAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(new ClioServerHandshake { ServerName = "clio", ServerVersion = "8.1.0.77", Capabilities = Array.Empty<string>() });

	public Task<IReadOnlyList<ClioCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<ClioCatalogEntry>>(Array.Empty<ClioCatalogEntry>());

	public Task<ClioToolCallResult> GetToolContractAsync(string name, CancellationToken cancellationToken = default) =>
		Task.FromResult(new ClioToolCallResult());

	public Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, CancellationToken cancellationToken = default) =>
		Task.FromResult(new ClioToolCallResult());

	public Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<string>? progress, CancellationToken cancellationToken = default) =>
		Task.FromResult(new ClioToolCallResult());

	public Task<ClioToolCallResult> CallToolAsync(string name, string? argumentsJson, IProgress<ClioStageEvent>? stageProgress, CancellationToken cancellationToken = default) =>
		Task.FromResult(new ClioToolCallResult());

	public Task<ClioServerHandshake> RestartAsync(CancellationToken cancellationToken = default) => ConnectAsync(cancellationToken);

	public Task PingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
