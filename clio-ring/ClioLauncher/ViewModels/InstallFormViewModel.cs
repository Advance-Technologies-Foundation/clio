using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClioLauncher.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioLauncher.ViewModels;

/// <summary>
/// View-model for the guided Creatio Install form — the single screen reached from the main radial ring's
/// "Deploy Creatio" action (story 8, ADR D5). It gathers a deploy plan (database + Redis <b>source</b>
/// [Local or Rancher], a Creatio build/ZIP, an instance name and a pre-selected editable free port) with
/// discovered defaults, then — on a single Install click — runs an internal preflight as a <b>pre-call
/// validation / blocker display</b>. A failed requirement surfaces exactly one human-readable message plus one
/// corrective action and does NOT start the install; a clean preflight proceeds straight to the deploy with no
/// dry-run and no extra confirmation.
/// </summary>
/// <remarks>
/// One-authoritative-runId contract: preflight is NEVER authored as a <see cref="DeployPipelineViewModel"/>
/// run. The pipeline stays idle/empty until clio's single authoritative stream drives it — on the Install
/// click, <see cref="DeployPipelineViewModel.BeginRun"/> hands the per-run sink to <c>CallToolAsync</c> and
/// clio's own manifest/stage/run-completed events (one runId) render the pipeline. The ring never ingests
/// synthetic manifest/stage events, so clio's real run can never collide with (and reset) a competing ring run.
/// <para/>
/// Safety (ADR D8): the real deploy is initiated ONLY from the user's Install click; nothing auto-fires it.
/// The live deploy invocation is additionally gated behind <see cref="LiveDeployEnabled"/>, which stays OFF
/// in shipped builds until BOTH the clio deploy honest-failure work (story 12) AND the typed <c>_meta</c>
/// progress forwarding (story 4) land — until then a clean preflight validates the plan but does not start a
/// real install. No raw MCP/tool/flag concepts are surfaced to the user anywhere in this form.
/// </remarks>
public sealed partial class InstallFormViewModel : ViewModelBase {
	private readonly IClioIpcClient _client;
	private CancellationTokenSource? _cts;

	/// <summary>Creates the guided Install form over the shared IPC client.</summary>
	/// <param name="client">The long-lived clio MCP client (shared across the app session).</param>
	/// <param name="liveDeployEnabled">
	/// When true, a clean preflight proceeds to the real deploy. Kept OFF in shipped builds until stories 12
	/// and 4 land; tests set it true to assert the deploy is invoked exactly once on the Install click.
	/// </param>
	public InstallFormViewModel(IClioIpcClient client, bool liveDeployEnabled = false) {
		_client = client ?? throw new ArgumentNullException(nameof(client));
		LiveDeployEnabled = liveDeployEnabled;
	}

	/// <summary>The GitHub-Actions-style pipeline that renders the preflight step then the deploy stages.</summary>
	public DeployPipelineViewModel Pipeline { get; } = new();

	/// <summary>
	/// Whether a clean preflight proceeds to the real deploy. OFF by default (safety gate); the form still
	/// validates and runs preflight, but does not start a real install until this is enabled.
	/// </summary>
	public bool LiveDeployEnabled { get; }

	/// <summary>Whether the window should auto-run discovery when it opens (off for screenshot/soak seams).</summary>
	public bool AutoDiscoverOnOpen { get; set; } = true;

	/// <summary>Discovered builds (newest-first); each build's FullPath is the ZIP to deploy.</summary>
	public ObservableCollection<CreatioBuild> Builds { get; } = new();

	/// <summary>Local database choices (shown/used only for the Local source).</summary>
	public ObservableCollection<InfraDb> LocalDatabases { get; } = new();

	/// <summary>Local Redis choices (shown/used only for the Local source).</summary>
	public ObservableCollection<InfraRedis> LocalRedisServers { get; } = new();

	[ObservableProperty]
	private string _connectionState = "not connected";

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ProductsFolderKnown))]
	private string _buildsStatus = "Discovering…";

	/// <summary>Whether a builds folder was resolved (else the build dropdown is empty with a reason).</summary>
	public bool ProductsFolderKnown => Builds.Count > 0;

	[ObservableProperty]
	private CreatioBuild? _selectedBuild;

	[ObservableProperty]
	private string _infraStatus = string.Empty;

	/// <summary>True = Local source (a database + Redis on this machine); false = Rancher (default Kubernetes).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsLocal))]
	[NotifyPropertyChangedFor(nameof(SourceLabel))]
	private bool _local;

	/// <summary>True when the Local source is selected (drives the database/Redis picker visibility).</summary>
	public bool IsLocal => Local;

	/// <summary>Human label for the source toggle.</summary>
	public string SourceLabel => Local
		? "Local — a database and Redis on this machine"
		: "Rancher — the default Kubernetes database and Redis";

	[ObservableProperty]
	private InfraDb? _selectedDatabase;

	[ObservableProperty]
	private InfraRedis? _selectedRedis;

	[ObservableProperty]
	private string _instanceName = string.Empty;

	/// <summary>Pre-selected free port (editable text; validated to the deploy port range).</summary>
	[ObservableProperty]
	private string _port = string.Empty;

	/// <summary>Inline validation summary shown when the form cannot be installed yet (empty = none).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasValidationSummary))]
	private string _validationSummary = string.Empty;

	/// <summary>Whether the form has a validation summary to show.</summary>
	public bool HasValidationSummary => !string.IsNullOrEmpty(ValidationSummary);

	/// <summary>
	/// Set when a clean preflight cannot proceed to a real install because the live deploy gate is off
	/// (stories 12 + 4 not yet landed). Empty when live deploy is enabled. Human-readable, no tool concepts.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPreviewNotice))]
	private string _previewNotice = string.Empty;

	/// <summary>Whether the preview-gate notice is showing.</summary>
	public bool HasPreviewNotice => !string.IsNullOrEmpty(PreviewNotice);

	[ObservableProperty]
	private string _output = "Getting ready — finding builds, checking infrastructure and a free port…";

	/// <summary>Auto-runs discovery when the form opens (shares the <see cref="LoadDefaultsAsync"/> IsBusy guard).</summary>
	public Task InitializeAsync() => LoadDefaultsAsync();

	/// <summary>Loads discovery defaults (builds, local infrastructure, a free port) and pre-selects recommended values.</summary>
	[RelayCommand]
	private async Task LoadDefaultsAsync() {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		Output = "Connecting and discovering builds, infrastructure and a free port…";
		try {
			await _client.ConnectAsync().ConfigureAwait(true);
			ConnectionState = "connected";

			BuildsResult builds = DeployDiscovery.ParseBuilds((await CallLongTailAsync("list-creatio-builds").ConfigureAwait(true)).RawText);
			Builds.Clear();
			foreach (CreatioBuild b in builds.Builds) {
				Builds.Add(b);
			}
			SelectedBuild = Builds.Count > 0 ? Builds[0] : null;
			BuildsStatus = builds.Builds.Count > 0
				? $"{builds.Builds.Count} build(s) available in {builds.ProductsFolder}"
				: $"No builds found: {builds.Message}";
			OnPropertyChanged(nameof(ProductsFolderKnown));

			PassingInfra infra = DeployDiscovery.ParsePassingInfra((await CallLongTailAsync("show-passing-infrastructure").ConfigureAwait(true)).RawText);
			LocalDatabases.Clear();
			foreach (InfraDb d in infra.Databases) {
				LocalDatabases.Add(d);
			}
			LocalRedisServers.Clear();
			foreach (InfraRedis r in infra.RedisServers) {
				LocalRedisServers.Add(r);
			}
			SelectedDatabase = FindDb(infra.RecommendedDbServerName) ?? (LocalDatabases.Count > 0 ? LocalDatabases[0] : null);
			SelectedRedis = FindRedis(infra.RecommendedRedisServerName) ?? (LocalRedisServers.Count > 0 ? LocalRedisServers[0] : null);
			InfraStatus = $"{LocalDatabases.Count} local database(s) and {LocalRedisServers.Count} Redis server(s) available.";

			// Pick the default source based on what THIS machine can actually run, so the guided Install works
			// out of the box instead of steering the user into a source that can't run here. Prefer Rancher when
			// Kubernetes is ready; otherwise fall back to Local when local db + Redis are ready. The user can
			// still switch sources. (Without this the form always defaulted to Rancher, so on a dev box with no
			// reachable cluster the very first Install was blocked at preflight — "no progress, no install".)
			AssertResult assert = DeployDiscovery.ParseAssert(
				(await CallLongTailAsync("assert-infrastructure").ConfigureAwait(true)).RawText);
			bool rancherReady = PreflightGate.RequiredFailures(assert, local: false).Count == 0;
			bool localReady = PreflightGate.RequiredFailures(assert, local: true).Count == 0;
			Local = !rancherReady && localReady;

			PortScan port = DeployDiscovery.ParsePortScan((await CallLongTailAsync("find-empty-iis-port").ConfigureAwait(true)).RawText);
			Port = port.FirstAvailablePort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

			Output = "Ready. Choose a source, name the instance, then click Install.";
		}
		catch (Exception ex) {
			ConnectionState = "error";
			Output = $"Could not get ready: {ex.Message}";
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>
	/// The user's Install click — the sole trigger for a real install (ADR D8). Runs the internal preflight as
	/// a pre-call validation / blocker display (NOT a pipeline run); on a problem it shows exactly one human
	/// message plus one corrective action and does not proceed, leaving the pipeline idle. On success it starts
	/// the deploy immediately (gated by <see cref="LiveDeployEnabled"/> until stories 12 + 4 land), and the
	/// pipeline is driven solely by clio's single authoritative stream via the per-run sink.
	/// </summary>
	[RelayCommand]
	private async Task InstallAsync() {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		ValidationSummary = string.Empty;
		PreviewNotice = string.Empty;
		_cts?.Dispose();
		_cts = new CancellationTokenSource();

		try {
			DeployPlan plan = BuildPlan();

			// Preflight is PRE-CALL VALIDATION only — it never authors a pipeline run. The pipeline stays idle
			// until clio's single authoritative stream drives it. A failed check shows one human blocker + one
			// corrective action and does NOT invoke the deploy.

			// Blocker #1 — invalid form input (missing name, port out of range, missing Local db/redis).
			IReadOnlyList<string> formErrors = DeployRequestBuilder.Validate(plan);
			if (formErrors.Count > 0) {
				(string message, string action) = DescribeFormProblem(plan, formErrors);
				Block(message, action);
				return;
			}

			// Blocker #2 — the machine/infrastructure is not ready for the chosen source.
			AssertResult assert = await PreflightAsync().ConfigureAwait(true);
			IReadOnlyList<string> required = PreflightGate.RequiredFailures(assert, plan.Local);
			if (required.Count > 0) {
				(string message, string action) = DescribeInfraProblem(plan.Local);
				Block(message, action);
				return;
			}

			// Preflight passed.
			if (!LiveDeployEnabled) {
				// Safety gate: validated, but a real install is not started in this preview build. Pipeline idle.
				PreviewNotice =
					"Requirements passed and the install is ready to run. Starting the real install is turned " +
					"off in this preview build; it will be enabled once live progress reporting is complete.";
				Output = PreviewNotice;
				return;
			}

			// Real deploy — proceed immediately (no dry-run, no extra confirmation). The pipeline is driven
			// SOLELY by clio's single authoritative stream: BeginRun() hands back the per-run sink and clio's
			// own manifest/stage/run-completed events (one runId) render it. The ring authors no stage events.
			Output = "Installing Creatio…";
			IProgress<ClioStageEvent> sink = Pipeline.BeginRun();
			string request = DeployRequestBuilder.BuildClioRunJson(plan);
			ClioToolCallResult result = await _client.CallToolAsync("clio-run", request, sink, _cts.Token).ConfigureAwait(true);

			// Fallback ONLY when clio streamed no terminal stage event (e.g. a clio that failed before emitting,
			// or a build without stage events). We did NOT observe a success terminal, so we must NOT claim the
			// install succeeded. clio's long-tail failure envelope is {"success":false,"error":…} carried WITHOUT
			// the MCP IsError flag, so check both signals; only clio's authoritative success terminal (handled
			// above via the pipeline) may report success. Never fabricate manifest/stage events.
			if (!Pipeline.HasTerminalOutcome) {
				string? failure = DescribeUnstreamedFailure(result);
				Output = failure
					?? "The install finished without reporting progress, so I can't confirm it completed. "
						+ "Check the clio output/logs before using the instance.";
			}
		}
		catch (OperationCanceledException) {
			Output = "The install was cancelled.";
		}
		catch (Exception ex) {
			Output = "Something went wrong before the install could run: " + ex.Message;
		}
		finally {
			IsBusy = false;
		}
	}

	// Returns a truthful human failure message when a no-terminal result actually signals failure, or null when
	// there is no failure signal (outcome genuinely unknown). Failure signals, in order:
	//   1. the MCP IsError flag;
	//   2. clio's command-result envelope: a non-zero "exit-code" (the shape deploy-creatio returns), with the
	//      real reason carried in "execution-log-messages"[].value;
	//   3. the long-tail envelope carrying "success":false / a non-empty "error".
	// None of these set IsError, so we must inspect the payload — otherwise a real failure is mislabelled as an
	// unconfirmed outcome (the deploy-creatio "Could not find zip file" case looked like "can't confirm").
	private static string? DescribeUnstreamedFailure(ClioToolCallResult result) {
		if (result.IsError) {
			return "The install did not complete — clio reported an error. See the clio output/logs for details.";
		}
		string? payload = result.Json ?? result.RawText;
		if (string.IsNullOrWhiteSpace(payload)) {
			return null;
		}
		try {
			using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(payload);
			System.Text.Json.JsonElement root = doc.RootElement;
			if (root.ValueKind != System.Text.Json.JsonValueKind.Object) {
				return null;
			}

			// (2) Non-zero exit-code — clio's deploy-creatio command-result envelope.
			if (root.TryGetProperty("exit-code", out System.Text.Json.JsonElement exit)
				&& exit.ValueKind == System.Text.Json.JsonValueKind.Number
				&& exit.TryGetInt32(out int exitCode) && exitCode != 0) {
				string reason = ExtractExecutionLog(root);
				return string.IsNullOrWhiteSpace(reason)
					? "The install did not complete — clio exited with an error. See the clio output/logs."
					: "The install did not complete: " + reason;
			}

			// (3) success:false / error string.
			bool failed = root.TryGetProperty("success", out System.Text.Json.JsonElement success)
				&& success.ValueKind == System.Text.Json.JsonValueKind.False;
			bool hasError = root.TryGetProperty("error", out System.Text.Json.JsonElement error)
				&& error.ValueKind == System.Text.Json.JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(error.GetString());
			if (failed || hasError) {
				return hasError
					? "The install did not complete: " + error.GetString()
					: "The install did not complete — clio reported it did not succeed. See the clio output/logs.";
			}
		}
		catch (System.Text.Json.JsonException) {
			// Not JSON (or malformed) — no reliable failure signal; fall through to the "unknown" message.
		}
		return null;
	}

	// Joins the human-readable values from clio's "execution-log-messages"[] array into one secret-free line,
	// so the real failure reason (e.g. "Could not find zip file: …") reaches the user instead of being dropped.
	private static string ExtractExecutionLog(System.Text.Json.JsonElement root) {
		if (!root.TryGetProperty("execution-log-messages", out System.Text.Json.JsonElement messages)
			|| messages.ValueKind != System.Text.Json.JsonValueKind.Array) {
			return string.Empty;
		}
		var parts = new List<string>();
		foreach (System.Text.Json.JsonElement m in messages.EnumerateArray()) {
			if (m.ValueKind == System.Text.Json.JsonValueKind.Object
				&& m.TryGetProperty("value", out System.Text.Json.JsonElement v)
				&& v.ValueKind == System.Text.Json.JsonValueKind.String) {
				string? text = v.GetString();
				if (!string.IsNullOrWhiteSpace(text)) {
					parts.Add(text!);
				}
			}
		}
		return string.Join(" ", parts);
	}

	// Shows exactly one human-readable blocker (message + one corrective action) as a pre-call gate, without
	// touching the clio-owned pipeline (which stays idle until a real run). No raw tool/flag concepts.
	private void Block(string message, string correctiveAction) {
		ValidationSummary = message + " " + correctiveAction;
		Output = message + "\n\n" + correctiveAction;
	}

	// One friendly message + one corrective action for an invalid form (no raw flag/tool names).
	private static (string Message, string Action) DescribeFormProblem(DeployPlan plan, IReadOnlyList<string> errors) {
		if (string.IsNullOrWhiteSpace(plan.SiteName)) {
			return ("The instance needs a name before it can be installed.",
				"Enter a name for the Creatio instance, then click Install.");
		}
		if (plan.SitePort < DeployRequestBuilder.MinPort || plan.SitePort > DeployRequestBuilder.MaxPort) {
			return ($"The port must be a number between {DeployRequestBuilder.MinPort} and {DeployRequestBuilder.MaxPort}.",
				"Enter a port in that range (a free one was pre-selected for you), then click Install.");
		}
		if (plan.Local) {
			return ("A Local install needs both a database and a Redis server chosen.",
				"Pick a database and a Redis server from the lists, or switch the source to Rancher.");
		}
		// Fallback — should not normally be reached; still one message + action, no raw detail.
		return ("The install form is not complete yet.", "Review the highlighted fields, then click Install.");
	}

	// One friendly message + one corrective action for a not-ready machine/infrastructure.
	private static (string Message, string Action) DescribeInfraProblem(bool local) => local
		? ("This machine isn't ready for a Local install yet — the local database and Redis check didn't pass.",
			"Start your local database and Redis, then click Install again.")
		: ("This machine isn't ready for a Rancher install yet — the Kubernetes check didn't pass.",
			"Start Rancher Desktop (or your Kubernetes cluster) and click Install again — or switch the source to Local.");

	private async Task<AssertResult> PreflightAsync() {
		ClioToolCallResult assertResult = await CallLongTailAsync("assert-infrastructure").ConfigureAwait(true);
		return DeployDiscovery.ParseAssert(assertResult.RawText);
	}

	// Discovery/preflight tools are non-resident; dispatch them through the resident runner.
	private Task<ClioToolCallResult> CallLongTailAsync(string tool) =>
		_client.CallToolAsync("clio-run", $"{{\"command\":\"{tool}\",\"args\":{{}}}}");

	private DeployPlan BuildPlan() {
		int.TryParse(Port.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port);
		return new DeployPlan {
			SiteName = InstanceName.Trim(),
			ZipFile = SelectedBuild?.FullPath ?? string.Empty,
			SitePort = port,
			Local = Local,
			DbServerName = Local ? SelectedDatabase?.DbServerName : null,
			RedisServerName = Local ? SelectedRedis?.RedisServerName : null
		};
	}

	/// <summary>Builds the exact deploy-creatio plan the Install click would send (exposed for tests/harness).</summary>
	public DeployPlan CurrentPlan() => BuildPlan();

	private InfraDb? FindDb(string? name) {
		if (string.IsNullOrEmpty(name)) {
			return null;
		}
		foreach (InfraDb d in LocalDatabases) {
			if (string.Equals(d.DbServerName, name, StringComparison.OrdinalIgnoreCase)) {
				return d;
			}
		}
		return null;
	}

	private InfraRedis? FindRedis(string? name) {
		if (string.IsNullOrEmpty(name)) {
			return null;
		}
		foreach (InfraRedis r in LocalRedisServers) {
			if (string.Equals(r.RedisServerName, name, StringComparison.OrdinalIgnoreCase)) {
				return r;
			}
		}
		return null;
	}

	// ---- design/screenshot/soak seams (no live clio) ----

	/// <summary>Design seam: populate the form with sample discovery data + a sample pipeline for a given source.</summary>
	public void DesignPopulate(bool local) {
		AutoDiscoverOnOpen = false;
		ConnectionState = "connected";
		Builds.Clear();
		Builds.Add(new CreatioBuild("8.1.5.2176_StudioNet8_Softkey_PostgreSQL_ENU.zip", @"F:\CreatioBuilds\8.1.5.2176_StudioNet8_Softkey_PostgreSQL_ENU.zip", 2147483648, "2026-06-20T10:00:00Z"));
		Builds.Add(new CreatioBuild("8.1.4.2035_StudioNet8_Softkey_PostgreSQL_ENU.zip", @"F:\CreatioBuilds\8.1.4.2035_StudioNet8_Softkey_PostgreSQL_ENU.zip", 2100000000, "2026-05-10T10:00:00Z"));
		SelectedBuild = Builds[0];
		BuildsStatus = "2 build(s) available in F:\\CreatioBuilds";
		OnPropertyChanged(nameof(ProductsFolderKnown));

		LocalDatabases.Clear();
		LocalDatabases.Add(new InfraDb("my-local-postgres", "postgres", "localhost", 5433));
		LocalRedisServers.Clear();
		LocalRedisServers.Add(new InfraRedis("local-redis", "localhost", 6379));
		SelectedDatabase = LocalDatabases[0];
		SelectedRedis = LocalRedisServers[0];
		InfraStatus = "1 local database and 1 Redis server available.";

		Port = "40001";
		InstanceName = "creatio-demo";
		Local = local;
		Output = "Ready. Choose a source, name the instance, then click Install.";
		Pipeline.DesignPopulate();
	}
}
