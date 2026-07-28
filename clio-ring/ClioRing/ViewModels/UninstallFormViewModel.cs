using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;
using ClioRing.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioRing.ViewModels;

/// <summary>
/// View-model for the guided Creatio Uninstall flow — the single screen reached from the main radial ring's
/// "Uninstall Creatio" action (story 9, ADR D5/D8). It lists the <b>local</b> registered clio environments
/// (the same environment source the ring uses), lets the user pick one, and — on the Uninstall button —
/// shows a simple "Are you sure? Yes/No" confirm that names the target environment and spells out the
/// irreversible consequence (the database is dropped and application files are removed, with no undo). There
/// is NO exact-name typing (superseded by story 9) and NO dry-run. On Yes, when the live gate is on, the real
/// uninstall runs and the shared <see cref="DeployPipelineViewModel"/> is driven SOLELY by clio's single
/// authoritative stream (one runId) — the ring authors no manifest/stage events, so clio's real run can never
/// collide with a competing ring run. On No nothing runs.
/// </summary>
/// <remarks>
/// Safety (ADR D8, FR-21/AC-16): the real uninstall is initiated ONLY by the user's explicit Yes click —
/// never on open, never by an agent. The live uninstall invocation is additionally gated behind
/// <see cref="LiveUninstallEnabled"/>, which stays OFF in shipped builds until the clio live-uninstall work
/// (stories 3 + 4) lands — until then Yes renders the pipeline preview but does not start a real uninstall.
/// No raw MCP/tool/flag concepts are surfaced to the user anywhere in this flow.
/// </remarks>
public sealed partial class UninstallFormViewModel : ViewModelBase {
	// The ordered uninstall manifest the ring renders for the pipeline. clio's own live stages (post-stories
	// 3 + 4) flow through the same pipeline via the sink and reconcile against this manifest.
	private static readonly (string Id, string Name)[] UninstallStages = {
		("read-config", "Read configuration"),
		("stop-iis", "Stop IIS site"),
		("delete-iis", "Delete IIS site"),
		("drop-db", "Drop database"),
		("delete-files", "Delete files"),
		("unregister", "Unregister environment")
	};

	private readonly IClioAdapter _clio;
	private readonly IClioIpcClient _client;
	private CancellationTokenSource? _cts;
	private ClioEnvironment? _confirmedEnvironment;

	/// <summary>Creates the guided Uninstall flow over the ring's environment source and the shared IPC client.</summary>
	/// <param name="clio">The environment source (registered clio environments; local-filtered here).</param>
	/// <param name="client">The long-lived clio MCP client (shared across the app session).</param>
	/// <param name="liveUninstallEnabled">
	/// When true, the user's Yes click proceeds to the real uninstall. Kept OFF in shipped builds until
	/// stories 3 + 4 land; tests set it true to assert the uninstall is invoked exactly once on Yes.
	/// </param>
	public UninstallFormViewModel(IClioAdapter clio, IClioIpcClient client, bool liveUninstallEnabled = false) {
		_clio = clio ?? throw new ArgumentNullException(nameof(clio));
		_client = client ?? throw new ArgumentNullException(nameof(client));
		LiveUninstallEnabled = liveUninstallEnabled;
	}

	/// <summary>The GitHub-Actions-style pipeline that renders the uninstall stages.</summary>
	public DeployPipelineViewModel Pipeline { get; } = new();

	/// <summary>
	/// Whether the user's Yes click proceeds to a real uninstall. OFF by default (safety gate); the flow
	/// still renders the pipeline preview but does not start a real uninstall until this is enabled.
	/// </summary>
	public bool LiveUninstallEnabled { get; }

	/// <summary>Whether the flow should auto-list environments when it opens (off for screenshot/soak seams).</summary>
	public bool AutoDiscoverOnOpen { get; set; } = true;

	/// <summary>The local registered environments the user can tear down (name is the uninstall target).</summary>
	public ObservableCollection<ClioEnvironment> LocalEnvironments { get; } = new();

	[ObservableProperty]
	private string _connectionState = "not connected";

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string _environmentsStatus = "Finding local environments…";

	/// <summary>The environment the user has picked to uninstall (null = none selected yet).</summary>
	[ObservableProperty]
	private ClioEnvironment? _selectedEnvironment;

	/// <summary>Inline notice shown when Uninstall cannot proceed yet (e.g. no environment picked). Empty = none.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasValidationSummary))]
	private string _validationSummary = string.Empty;

	/// <summary>Whether the flow has a validation notice to show.</summary>
	public bool HasValidationSummary => !string.IsNullOrEmpty(ValidationSummary);

	// ---- simple "Are you sure? Yes/No" confirm ----

	/// <summary>Whether the simple Yes/No confirm is showing.</summary>
	[ObservableProperty]
	private bool _isConfirmVisible;

	/// <summary>The confirm question naming the concrete target environment.</summary>
	[ObservableProperty]
	private string _confirmMessage = string.Empty;

	/// <summary>The prominent, explicit irreversible consequence line for the confirm.</summary>
	[ObservableProperty]
	private string _confirmConsequence = string.Empty;

	/// <summary>
	/// Set when Yes cannot proceed to a real uninstall because the live gate is off (stories 3 + 4 not yet
	/// landed). Empty when live uninstall is enabled. Human-readable, no tool concepts.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPreviewNotice))]
	private string _previewNotice = string.Empty;

	/// <summary>Whether the preview-gate notice is showing.</summary>
	public bool HasPreviewNotice => !string.IsNullOrEmpty(PreviewNotice);

	[ObservableProperty]
	private string _output = "Pick the local environment you want to remove.";

	/// <summary>Auto-lists local environments when the flow opens (shares the <see cref="LoadEnvironmentsAsync"/> guard).</summary>
	public Task InitializeAsync() => LoadEnvironmentsAsync();

	/// <summary>Lists the registered environments (filtered to local) and pre-selects the first one.</summary>
	[RelayCommand]
	private async Task LoadEnvironmentsAsync() {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		Output = "Finding local environments…";
		try {
			IReadOnlyList<ClioEnvironment> all = await _clio.ListEnvironmentsAsync().ConfigureAwait(true);
			ConnectionState = "connected";
			LocalEnvironments.Clear();
			foreach (ClioEnvironment env in all.Where(e => e.Location == EnvLocation.Local)) {
				LocalEnvironments.Add(env);
			}
			SelectedEnvironment = LocalEnvironments.Count > 0 ? LocalEnvironments[0] : null;
			EnvironmentsStatus = LocalEnvironments.Count > 0
				? $"{LocalEnvironments.Count} local environment(s) available."
				: "No local environments are registered.";
			Output = LocalEnvironments.Count > 0
				? "Pick the local environment you want to remove, then click Uninstall."
				: "There are no local environments to remove.";
		}
		catch (Exception ex) {
			ConnectionState = "error";
			EnvironmentsStatus = "Could not list environments.";
			Output = $"Could not list environments: {ex.Message}";
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>
	/// The Uninstall button: opens the simple Yes/No confirm for the picked environment. It never starts a
	/// real uninstall — only the confirm's Yes click can. A missing selection is blocked with a human notice.
	/// </summary>
	[RelayCommand]
	private void RequestUninstall() {
		PreviewNotice = string.Empty;
		if (SelectedEnvironment is null) {
			ValidationSummary = "Pick a local environment to remove before clicking Uninstall.";
			IsConfirmVisible = false;
			return;
		}
		ValidationSummary = string.Empty;
		OpenConfirm();
	}

	private void OpenConfirm() {
		ClioEnvironment env = SelectedEnvironment!;
		_confirmedEnvironment = env;
		ConfirmMessage = $"Are you sure you want to uninstall '{env.Name}'?";
		ConfirmConsequence =
			"This will DROP the database (local or containerized) and permanently REMOVE all application " +
			"files. There is no undo.";
		IsConfirmVisible = true;
	}

	/// <summary>Cancels the confirm (No): nothing runs and no changes are made.</summary>
	[RelayCommand]
	private void CancelUninstall() {
		_confirmedEnvironment = null;
		IsConfirmVisible = false;
		ConfirmMessage = string.Empty;
		ConfirmConsequence = string.Empty;
		Output = "Cancelled. Nothing was removed.";
	}

	/// <summary>
	/// The user's Yes click — the sole trigger for a real uninstall (ADR D8). When the live gate is on it runs
	/// the real uninstall exactly once with the per-run pipeline sink, and the pipeline is driven SOLELY by
	/// clio's single authoritative stream (one runId); the ring authors no manifest/stage events. When the gate
	/// is off it shows a human preview notice and starts nothing, leaving the pipeline idle.
	/// </summary>
	[RelayCommand]
	private async Task ConfirmUninstallAsync() {
		if (IsBusy || _confirmedEnvironment is null) {
			return;
		}
		ClioEnvironment env = _confirmedEnvironment;
		_confirmedEnvironment = null;
		IsConfirmVisible = false;
		ConfirmMessage = string.Empty;
		ConfirmConsequence = string.Empty;
		PreviewNotice = string.Empty;
		IsBusy = true;
		_cts?.Dispose();
		_cts = new CancellationTokenSource();

		try {
			if (!LiveUninstallEnabled) {
				// Safety gate: a real uninstall is not started in this preview build. The pipeline stays idle
				// until clio's authoritative stream drives it (no ring-authored preview run).
				PreviewNotice =
					$"'{env.Name}' is ready to be removed. Starting the real uninstall is turned off in this " +
					"preview build; it will be enabled once live progress reporting is complete.";
				Output = PreviewNotice;
				return;
			}

			// Real uninstall — proceed immediately (no dry-run). The pipeline is driven SOLELY by clio's single
			// authoritative stream: BeginRun() hands back the per-run sink and clio's own manifest/stage/
			// run-completed events (one runId) render it. The ring authors no stage events.
			Output = $"Uninstalling '{env.Name}'…";
			IProgress<ClioStageEvent> sink = Pipeline.BeginRun();
			string request = BuildUninstallRequest(env.Name);
			ClioToolCallResult result = await _client.CallToolAsync("clio-run", request, sink, _cts.Token).ConfigureAwait(true);

			// Fallback ONLY when clio streamed no terminal: surface the outcome as a human message. Never
			// fabricate manifest/stage events into the clio-owned pipeline.
			if (!Pipeline.HasTerminalOutcome) {
				Output = result.IsError
					? "The uninstall did not complete. See the details for more."
					: "The uninstall finished without reporting a final outcome, so I can't confirm it completed. " +
					  "Refresh the environment list and verify the instance before continuing.";
			}
		}
		catch (OperationCanceledException) {
			Output = "The uninstall was cancelled.";
		}
		catch (Exception ex) {
			Output = "Something went wrong before the uninstall could run: " + ex.Message;
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>Builds the exact clio-run request the Yes click would send (exposed for tests/harness).</summary>
	public string CurrentRequest() => BuildUninstallRequest(SelectedEnvironment?.Name ?? string.Empty);

	// Builds the clio-run request that dispatches uninstall-creatio for the target environment:
	// {"command":"uninstall-creatio","args":{"environment-name":"<env>"}}. Written with Utf8JsonWriter so it
	// is safe under the AOT host's disabled reflection serializer.
	private static string BuildUninstallRequest(string environmentName) {
		var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer)) {
			writer.WriteStartObject();
			writer.WriteString("command", "uninstall-creatio");
			writer.WritePropertyName("args");
			writer.WriteStartObject();
			writer.WriteString("environment-name", environmentName);
			writer.WriteEndObject();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	// ---- synthetic ring-side stage-event builders for the uninstall pipeline ----

	private static ClioStageEvent UninstallManifest(Guid runId, int sequence) {
		var entries = new ClioStageManifestEntry[UninstallStages.Length];
		for (int i = 0; i < UninstallStages.Length; i++) {
			entries[i] = new ClioStageManifestEntry(UninstallStages[i].Id, UninstallStages[i].Name, i, UninstallStages.Length, false);
		}
		return new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Manifest,
			runId, sequence, ClioStageEventContract.Operations.Uninstall, Stages: entries);
	}

	private static ClioStageEvent StageEvent(Guid runId, int sequence, int index, string status, string message,
		long? durationMs = null, string? detail = null, string? errorCode = null) =>
		new(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Stage, runId, sequence,
			ClioStageEventContract.Operations.Uninstall,
			Stage: new ClioStageDetail(UninstallStages[index].Id, UninstallStages[index].Name, index, UninstallStages.Length,
				status, DurationMs: durationMs, Message: message, Detail: detail, ErrorCode: errorCode));

	private static ClioStageEvent RunCompleted(Guid runId, int sequence, string outcome, string summary,
		string? detail = null) =>
		new(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.RunCompleted, runId, sequence,
			ClioStageEventContract.Operations.Uninstall,
			RunCompleted: new ClioRunCompleted(outcome, summary, Detail: detail));

	// ---- design/screenshot/soak seams (no live clio) ----

	/// <summary>Design seam: populate the flow with sample local environments, open the confirm, and render a
	/// sample uninstall pipeline (no live clio). <paramref name="succeed"/> false renders the AC-ERR case
	/// (config-read fails, run fails, the environment is left registered).</summary>
	public void DesignPopulate(bool succeed = true) {
		AutoDiscoverOnOpen = false;
		ConnectionState = "connected";
		LocalEnvironments.Clear();
		LocalEnvironments.Add(new ClioEnvironment("creatio-demo", "http://localhost:40001", IsNetCore: true));
		LocalEnvironments.Add(new ClioEnvironment("qa-local", "https://qa.tscrm.com", IsNetCore: false));
		SelectedEnvironment = LocalEnvironments[0];
		EnvironmentsStatus = "2 local environment(s) available.";
		Output = "Pick the local environment you want to remove, then click Uninstall.";
		OpenConfirm();
		DesignPopulatePipeline(succeed);
	}

	private void DesignPopulatePipeline(bool succeed) {
		Pipeline.BeginRun();
		var runId = Guid.NewGuid();
		int seq = 0;
		Pipeline.Ingest(UninstallManifest(runId, seq++));

		if (succeed) {
			Pipeline.Ingest(StageEvent(runId, seq++, 0, ClioStageEventContract.StageStatuses.Done, "Configuration read", durationMs: 300));
			Pipeline.Ingest(StageEvent(runId, seq++, 1, ClioStageEventContract.StageStatuses.Done, "IIS site stopped", durationMs: 1200));
			Pipeline.Ingest(StageEvent(runId, seq++, 2, ClioStageEventContract.StageStatuses.Done, "IIS site deleted", durationMs: 800));
			Pipeline.Ingest(StageEvent(runId, seq++, 3, ClioStageEventContract.StageStatuses.Done, "Database dropped", durationMs: 5400));
			Pipeline.Ingest(StageEvent(runId, seq++, 4, ClioStageEventContract.StageStatuses.Done, "Application files removed", durationMs: 2200));
			Pipeline.Ingest(StageEvent(runId, seq++, 5, ClioStageEventContract.StageStatuses.Running, "Unregistering the environment…"));
			Pipeline.Ingest(RunCompleted(runId, seq++, ClioStageEventContract.RunOutcomes.Success, "Creatio was uninstalled."));
		}
		else {
			// AC-ERR: the config-read stage fails; the run terminates as a failure and the environment is NOT
			// unregistered (honest reporting end-to-end — the failure cascades and 'unregister' never runs).
			Pipeline.Ingest(StageEvent(runId, seq++, 0, ClioStageEventContract.StageStatuses.Failed,
				"Could not read the environment configuration", durationMs: 200,
				detail: "The configuration file for this environment could not be read.", errorCode: "CONFIG_READ_FAILED"));
			Pipeline.Ingest(RunCompleted(runId, seq++, ClioStageEventContract.RunOutcomes.Failure,
				"Uninstall stopped because the configuration could not be read. The environment is still registered.",
				detail: "Fix the environment configuration, then try again."));
		}
	}
}
