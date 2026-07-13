using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using ClioRing;
using ClioRing.Diagnostics;
using ClioRing.Ipc;
using ClioRing.Services;
using ClioRing.ViewModels;

namespace ClioRing.Desktop;

sealed class Program {
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	[STAThread]
	public static void Main(string[] args) {
		int? consoleExitCode = TryRunConsoleMode(args);
		if (consoleExitCode.HasValue) {
			Environment.Exit(consoleExitCode.Value);
			return;
		}

		RunDesktop(args);
	}

	private static int? TryRunConsoleMode(string[] args) {
		// EXPERIMENTAL clio IPC measurements harness. Runs the MCP-over-stdio proof as a pure console
		// routine (NO Avalonia, no window) and exits with the harness result code. Time-boxed by the
		// runner's own calls; safe to invoke headless.
		if (args.Contains("--ipc-proof")) {
			return RunIpcProof(args);
		}

		// Deploy-creatio dry-run: construct the clio-run request for Rancher + Local against real clio and
		// run the read-only preflight, WITHOUT firing the destructive deploy. Verifies request/args.
		if (args.Contains("--deploy-dryrun")) {
			return RunDeployDryRun().GetAwaiter().GetResult();
		}

		// Button-path debugger harness. Without --execute it performs discovery, selects the requested
		// build/local infrastructure, and prints the exact plan without deploying. With --execute it calls
		// the same generated InstallCommand as the Avalonia button.
		if (args.Contains("--install-probe")) {
			return RunInstallProbe(args).GetAwaiter().GetResult();
		}
		if (args.Contains("--uninstall-probe")) {
			return RunUninstallProbe(args).GetAwaiter().GetResult();
		}
		if (args.Contains("--pipeline-order-probe")) {
			return RunPipelineOrderProbe();
		}
		if (args.Contains("--env-refresh-probe")) {
			return RunEnvironmentRefreshProbe(args).GetAwaiter().GetResult();
		}
		if (args.Contains("--env-watch-probe")) {
			return RunEnvironmentWatchProbe();
		}

		// Single-instance identity proof (console, no Avalonia): distinct exe paths get distinct
		// identities (coexist), the same path single-instances, and path normalization collapses
		// casing/trailing-slash variants. Exits 0 on PASS.
		if (args.Contains("--singleinstance-test")) {
			return RunSingleInstanceTest();
		}

		// Console diagnostic: print any DIFFERENT clio-ring build detected running elsewhere, then exit.
		if (args.Contains("--detect-builds")) {
			CrossBuildInfo? other = SingleInstance.DetectOtherRunningBuild();
			Console.Error.WriteLine(other is null
				? $"[detect-builds] id={SingleInstance.Id} dir={AppContext.BaseDirectory} other=none"
				: $"[detect-builds] id={SingleInstance.Id} other={other.Describe()}");
			return other is null ? 0 : 10;
		}
		return null;
	}

	private static void RunDesktop(string[] args) {
		// Earliest managed stamp: the reference point for cold-start (process-start -> first-paint).
		Metrics.ProcessStartTicks = Stopwatch.GetTimestamp();
		LaunchOptions.Current = LaunchOptions.Parse(args);

		StartupLog.Log($"=== launch pid={Environment.ProcessId} args=[{string.Join(' ', args)}] ===");

		// Single instance: a second launch activates the existing ring, then exits.
		// Harness modes (smoke/bench/exit-after-paint) bypass this so they always exercise render.
		if (!LaunchOptions.Current.IsHarnessMode && !SingleInstance.TryAcquire()) {
			StartupLog.Log("second instance detected -> signalling existing instance and exiting");
			SingleInstance.SignalExisting();
			return;
		}

		try {
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
			StartupLog.Log("shutdown reason=lifetime-exit");
		}
		catch (Exception ex) {
			StartupLog.Log($"FATAL: {ex}");
			throw;
		}
	}

	private static async System.Threading.Tasks.Task<int> RunInstallProbe(string[] args) {
		void Log(string value) => Console.Error.WriteLine("[install-probe] " + value);
		await using var client = new ClioIpcClient(Startup.ResolveClioIpcSettings(), Log);
		var form = new InstallFormViewModel(client, liveDeployEnabled: true);
		form.Pipeline.EnableSynchronousIngestionForHarness();
		await form.InitializeAsync().ConfigureAwait(false);

		string requestedBuild = ArgValue(args, "--build")
			?? "10.1.268_SalesEnterprise_Marketing_ServiceEnterpriseNet8_Softkey_PostgreSQL_ENU.zip";
		CreatioBuild? build = form.Builds.FirstOrDefault(item =>
			string.Equals(item.FileName, requestedBuild, StringComparison.OrdinalIgnoreCase));
		if (build is null) {
			Log($"FAIL build not discovered: {requestedBuild}; discovered={form.Builds.Count}");
			return 3;
		}

		form.Local = true;
		form.SelectedBuild = build;
		form.InstanceName = ArgValue(args, "--site") ?? "semse";
		form.Port = ArgValue(args, "--port") ?? "40001";
		DeployPlan plan = form.CurrentPlan();
		string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes(plan.ZipFile)))[..16];
		Log($"selected file={build.FileName} chars={plan.ZipFile.Length} trimmed={plan.ZipFile == plan.ZipFile.Trim()} " +
			$"exists={File.Exists(plan.ZipFile)} fingerprint={fingerprint}");
		Log($"local={plan.Local} db={plan.DbServerName} redis={plan.RedisServerName} site={plan.SiteName} port={plan.SitePort}");

		if (!args.Contains("--execute")) {
			Log("PASS dry boundary trace; button was not executed");
			return File.Exists(plan.ZipFile) ? 0 : 4;
		}

		Log("executing the same InstallCommand as the UI button");
		await form.InstallCommand.ExecuteAsync(null).ConfigureAwait(false);
		Log($"finished state={form.Pipeline.RunState} steps={form.Pipeline.Steps.Count} output={form.Output}");
		return form.Pipeline.IsSucceeded ? 0 : 5;
	}

	private static async System.Threading.Tasks.Task<int> RunUninstallProbe(string[] args) {
		void Log(string value) => Console.Error.WriteLine("[uninstall-probe] " + value);
		string? target = ArgValue(args, "--env");
		if (string.IsNullOrWhiteSpace(target)) {
			Log("FAIL --env is required; no default uninstall target is allowed");
			return 9;
		}
		if (args.Contains("--execute") && !string.Equals(ArgValue(args, "--confirm-env"), target, StringComparison.Ordinal)) {
			Log("FAIL --execute requires --confirm-env with the exact target name");
			return 10;
		}
		await using var client = new ClioIpcClient(Startup.ResolveClioIpcSettings(), Log);
		var form = new UninstallFormViewModel(new ClioAdapter(), client, liveUninstallEnabled: true);
		form.Pipeline.EnableSynchronousIngestionForHarness();
		await form.InitializeAsync().ConfigureAwait(false);
		ClioEnvironment? environment = form.LocalEnvironments.FirstOrDefault(item =>
			string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
		if (environment is null) {
			Log($"FAIL local environment not discovered: {target}; discovered={form.LocalEnvironments.Count}");
			return 11;
		}

		form.SelectedEnvironment = environment;
		form.RequestUninstallCommand.Execute(null);
		Log($"selected env={environment.Name} uri={environment.Uri} location={environment.Location} " +
			$"confirmVisible={form.IsConfirmVisible} request={form.CurrentRequest()}");
		if (!args.Contains("--execute")) {
			Log("PASS dry boundary trace; Yes was not executed");
			return form.IsConfirmVisible ? 0 : 12;
		}

		Log("executing the same ConfirmUninstallCommand as the UI Yes button");
		await form.ConfirmUninstallCommand.ExecuteAsync(null).ConfigureAwait(false);
		Log($"finished state={form.Pipeline.RunState} steps={form.Pipeline.Steps.Count} output={form.Output}");
		IReadOnlyList<ClioEnvironment> remaining = await new ClioAdapter().ListEnvironmentsAsync().ConfigureAwait(false);
		bool removedFromCatalog = remaining.All(item => !string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
		Log($"postcondition removedFromCatalog={removedFromCatalog}");
		return form.Pipeline.IsSucceeded && removedFromCatalog ? 0 : 13;
	}
	private static int RunPipelineOrderProbe() {
		var pipeline = new DeployPipelineViewModel();
		var adapter = new ClioStageEventAdapter(new InlineProgress<ClioStageEvent>(pipeline.Ingest));
		Guid runId = Guid.NewGuid();
		var stages = new[] {
			new ClioStageManifestEntry("unzip", "Unzip distribution", 0, 1, false)
		};
		ClioStageEvent[] events = {
			new(1, ClioStageEventContract.EventTypes.Stage, runId, 1, ClioStageEventContract.Operations.Deploy,
				Stage: new ClioStageDetail("unzip", "Unzip distribution", 0, 1,
					ClioStageEventContract.StageStatuses.Running, Message: "Unzipping")),
			new(1, ClioStageEventContract.EventTypes.RunCompleted, runId, 3, ClioStageEventContract.Operations.Deploy,
				RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Success, "Deployment completed")),
			new(1, ClioStageEventContract.EventTypes.Manifest, runId, 0, ClioStageEventContract.Operations.Deploy,
				Stages: stages),
			new(1, ClioStageEventContract.EventTypes.Stage, runId, 2, ClioStageEventContract.Operations.Deploy,
				Stage: new ClioStageDetail("unzip", "Unzip distribution", 0, 1,
					ClioStageEventContract.StageStatuses.Done, Message: "Unzipped"))
		};

		foreach (ClioStageEvent stageEvent in events) {
			var notification = new System.Text.Json.Nodes.JsonObject {
				["_meta"] = new System.Text.Json.Nodes.JsonObject {
					["clioStageEvent"] = System.Text.Json.JsonSerializer.SerializeToNode(
						stageEvent, ClioStageEventJsonContext.Default.ClioStageEvent)
				}
			};
			adapter.Consume(notification);
		}

		bool passed = pipeline.IsSucceeded && pipeline.Steps.Count == 1 && pipeline.Steps[0].IsDone;
		Console.Error.WriteLine($"[pipeline-order-probe] state={pipeline.RunState} steps={pipeline.Steps.Count} " +
			$"stepState={(pipeline.Steps.Count == 0 ? "none" : pipeline.Steps[0].State)} result={(passed ? "PASS" : "FAIL")}");
		return passed ? 0 : 6;
	}

	private static async System.Threading.Tasks.Task<int> RunEnvironmentRefreshProbe(string[] args) {
		string expected = ArgValue(args, "--env") ?? "ve";
		var viewModel = new RingViewModel(
			new ClioAdapter(), new ActionCatalogLoader(), new InMemoryEnvStateStore(), new NullActionCatalogWatcher());
		await viewModel.LoadEnvironmentsAsync().ConfigureAwait(false);
		bool firstLoad = viewModel.FilteredEnvironments.Any(env =>
			string.Equals(env.Name, expected, StringComparison.OrdinalIgnoreCase));
		await viewModel.RefreshEnvironmentsCommand.ExecuteAsync(null).ConfigureAwait(false);
		bool refreshed = viewModel.FilteredEnvironments.Any(env =>
			string.Equals(env.Name, expected, StringComparison.OrdinalIgnoreCase));
		Console.Error.WriteLine($"[env-refresh-probe] env={expected} firstLoad={firstLoad} " +
			$"manualRefresh={refreshed} count={viewModel.FilteredEnvironments.Count} result={(refreshed ? "PASS" : "FAIL")}");
		return refreshed ? 0 : 7;
	}

	private static int RunEnvironmentWatchProbe() {
		string directory = Path.Combine(Path.GetTempPath(), $"clio-ring-env-watch-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "appsettings.json");
		File.WriteAllText(path, "{}");
		using var signal = new ManualResetEventSlim();
		using var watcher = new EnvironmentSettingsWatcher();
		watcher.Changed += signal.Set;
		watcher.Start(path);
		File.WriteAllText(path, "{\"Environments\":{}}");
		bool passed = signal.Wait(TimeSpan.FromSeconds(5));
		watcher.Stop();
		Directory.Delete(directory, recursive: true);
		Console.Error.WriteLine($"[env-watch-probe] changed={passed} result={(passed ? "PASS" : "FAIL")}");
		return passed ? 0 : 8;
	}

	private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> {
		public void Report(T value) => report(value);
	}

	// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
					 .UsePlatformDetect()
					 .WithInterFont()
					 .LogToTrace();

	// --ipc-proof [--out <path>] [--env <name> ...]
	// Resolves the clio launch config from app-settings.json (same resolver the app uses), runs the
	// read-only proof, and writes the Markdown report. Returns the runner's exit code.
	private static int RunIpcProof(string[] args) {
		string outPath = ArgValue(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "measurements", "ipc-proof.md");
		List<string> envs = ArgValues(args, "--env");
		if (envs.Count == 0) {
			envs.Add("ve");
			envs.Add("d1");
		}

		ClioIpcSettings settings = Startup.ResolveClioIpcSettings();
		// Optional override (testing): --clio-dll <path> points the child at a specific clio dll, e.g. to
		// confirm the incompatible-clio path against an older build.
		string? dllOverride = ArgValue(args, "--clio-dll");
		if (!string.IsNullOrWhiteSpace(dllOverride)) {
			settings = settings with { Command = "dotnet", Args = new[] { dllOverride, "mcp-server" } };
		}
		try {
			return IpcProofRunner.RunAsync(settings, outPath, envs, Console.Error.WriteLine, CancellationToken.None)
				.GetAwaiter().GetResult();
		}
		catch (Exception ex) {
			Console.Error.WriteLine($"[ipc-proof] FATAL: {ex}");
			return 2;
		}
	}

	// Constructs the deploy-creatio request for both infra modes against real clio and runs the
	// read-only preflight, WITHOUT firing deploy-creatio. Prints the exact requests for verification.
	private static async System.Threading.Tasks.Task<int> RunDeployDryRun() {
		void Log(string s) => Console.Error.WriteLine(s);
		ClioIpcSettings settings = Startup.ResolveClioIpcSettings();
		await using var client = new ClioIpcClient(settings, Log);
		try {
			ClioServerHandshake hs = await client.ConnectAsync().ConfigureAwait(false);
			Log($"[deploy-dryrun] connected: {hs.ServerName} {hs.ServerVersion}");

			// These discovery/preflight tools are non-resident -> dispatch via clio-run.
			async System.Threading.Tasks.Task<ClioToolCallResult> LongTail(string tool) =>
				await client.CallToolAsync("clio-run", $"{{\"command\":\"{tool}\",\"args\":{{}}}}").ConfigureAwait(false);

			BuildsResult builds = DeployDiscovery.ParseBuilds((await LongTail("list-creatio-builds").ConfigureAwait(false)).RawText);
			PortScan port = DeployDiscovery.ParsePortScan((await LongTail("find-empty-iis-port").ConfigureAwait(false)).RawText);
			PassingInfra infra = DeployDiscovery.ParsePassingInfra((await LongTail("show-passing-infrastructure").ConfigureAwait(false)).RawText);

			string zip = builds.Builds.Count > 0 ? builds.Builds[0].FullPath : @"F:\CreatioBuilds\<no-build-found>.zip";
			int sitePort = port.FirstAvailablePort ?? 40001;
			Log($"[deploy-dryrun] builds={builds.Builds.Count} (status={builds.Status}, folder={builds.ProductsFolder}); freePort={port.FirstAvailablePort}; localDb={infra.Databases.Count}, localRedis={infra.RedisServers.Count}");

			var rancher = new DeployPlan { SiteName = "creatio-rancher", ZipFile = zip, SitePort = sitePort, Local = false };
			string db = infra.RecommendedDbServerName ?? (infra.Databases.Count > 0 ? infra.Databases[0].DbServerName : "postgres-local");
			string redis = infra.RecommendedRedisServerName ?? (infra.RedisServers.Count > 0 ? infra.RedisServers[0].RedisServerName : "redis-local");
			var local = new DeployPlan { SiteName = "creatio-local", ZipFile = zip, SitePort = sitePort, Local = true, DbServerName = db, RedisServerName = redis };

			Log("");
			Log("[deploy-dryrun] RANCHER request (db/redis omitted):");
			Log("  " + SecretRedactor.Redact(DeployRequestBuilder.BuildClioRunJson(rancher)));
			Log("[deploy-dryrun] LOCAL request:");
			Log("  " + SecretRedactor.Redact(DeployRequestBuilder.BuildClioRunJson(local)));

			Log("");
			Log("[deploy-dryrun] preflight (read-only):");
			ClioToolCallResult assertResult = await LongTail("assert-infrastructure").ConfigureAwait(false);
			foreach (string tool in new[] { "show-passing-infrastructure", "find-empty-iis-port" }) {
				ClioToolCallResult r = await LongTail(tool).ConfigureAwait(false);
				string body = r.Json ?? r.RawText;
				Log($"  {tool}: isError={r.IsError}, {(body.Length > 90 ? body[..90] + "…" : body)}");
			}

			// Preflight-gate demonstration (fix 3): show which required checks would BLOCK each mode.
			AssertResult assert = DeployDiscovery.ParseAssert(assertResult.RawText);
			Log($"  assert-infrastructure: overall={assert.Status}; k8={assert.SectionStatus("k8")}, local={assert.SectionStatus("local")}, filesystem={assert.SectionStatus("filesystem")}");
			IReadOnlyList<string> rancherFail = PreflightGate.RequiredFailures(assert, local: false);
			IReadOnlyList<string> localFail = PreflightGate.RequiredFailures(assert, local: true);
			Log($"  gate RANCHER: {(rancherFail.Count == 0 ? "PASS (would proceed)" : "BLOCK -> " + string.Join(" | ", rancherFail))}");
			Log($"  gate LOCAL:   {(localFail.Count == 0 ? "PASS (would proceed)" : "BLOCK -> " + string.Join(" | ", localFail))}");

			Log("");
			Log("[deploy-dryrun] deploy-creatio was NOT fired (dry run). PASS.");
			return 0;
		}
		catch (Exception ex) {
			Log($"[deploy-dryrun] FATAL: {ex.Message}");
			return 2;
		}
	}

	// Proves the per-executable-path single-instance identity without a GUI.
	private static int RunSingleInstanceTest() {
		static int Check(string label, bool ok) {
			Console.Error.WriteLine($"[si-test] {(ok ? "PASS" : "FAIL")}: {label}");
			return ok ? 0 : 1;
		}
		int failures = 0;

		string dirA = @"C:\Tools\clio-ring-dev";
		string dirB = @"C:\Tools\clio-ring-ipc-preview";

		string idA = SingleInstance.ComputeInstanceId(dirA);
		string idB = SingleInstance.ComputeInstanceId(dirB);

		// 1. Different install directories -> distinct identities (they coexist, no cross-activation).
		failures += Check($"distinct paths -> distinct ids ({idA} != {idB})", idA != idB);

		// 2. Deterministic + normalized: casing and trailing slash variants collapse to one identity.
		failures += Check("same path is stable/deterministic", SingleInstance.ComputeInstanceId(dirA) == idA);
		failures += Check("path normalization (case-insensitive)", SingleInstance.ComputeInstanceId(dirA.ToUpperInvariant()) == idA);
		failures += Check("path normalization (trailing separator)", SingleInstance.ComputeInstanceId(dirA + @"\") == idA);

		// 3. Mutex behaviour: the SAME identity single-instances (second acquire is not new), while a
		//    DIFFERENT identity acquires independently (coexists).
		string nameA = $@"Local\clio-ring-{idA}-sitest";
		string nameB = $@"Local\clio-ring-{idB}-sitest";
		using (var mutexA1 = new Mutex(initiallyOwned: true, nameA, out bool a1New)) {
			failures += Check("first acquire of path A is primary", a1New);
			using var mutexA2 = new Mutex(initiallyOwned: true, nameA, out bool a2New);
			failures += Check("second acquire of SAME path A single-instances (not new)", !a2New);
			using var mutexB = new Mutex(initiallyOwned: true, nameB, out bool bNew);
			failures += Check("acquire of DIFFERENT path B coexists (is new)", bNew);
		}

		Console.Error.WriteLine($"[si-test] this build id={SingleInstance.Id} dir={AppContext.BaseDirectory}");
		Console.Error.WriteLine(failures == 0 ? "[si-test] ALL PASS" : $"[si-test] {failures} FAILURE(S)");
		return failures == 0 ? 0 : 1;
	}

	private static string? ArgValue(string[] args, string name) {
		int i = Array.IndexOf(args, name);
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	private static List<string> ArgValues(string[] args, string name) {
		var values = new List<string>();
		for (int i = 0; i < args.Length - 1; i++) {
			if (args[i] == name) {
				values.Add(args[i + 1]);
			}
		}
		return values;
	}
}
