using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClioRing.Ipc;
using ClioRing.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioRing.ViewModels;

/// <summary>Terminal/interim state of a workflow run, driving the end-state badge.</summary>
public enum WorkflowRunState {
	/// <summary>No run yet / idle.</summary>
	Idle,

	/// <summary>A run is in progress.</summary>
	Running,

	/// <summary>The run completed without error.</summary>
	Success,

	/// <summary>The run reported an error.</summary>
	Failure
}

/// <summary>
/// View-model for the EXPERIMENTAL clio workflow surface. Runs a small allow-list of commands against
/// the ring's SELECTED environment over the long-lived <see cref="IClioIpcClient"/>. Read-only commands
/// run immediately; destructive ones (gate/restart/flushdb) must pass a typed confirm naming the exact
/// command + target env. While a command runs a live heartbeat/activity indicator is shown (MCP
/// keep-alive beats — NOT incremental stdout); the tool's actual output and the honest end-state arrive
/// with the final result. The deploy wizard is launched via <see cref="DeployWizardRequested"/>.
/// </summary>
public sealed partial class ClioWorkflowViewModel : ViewModelBase {
	private readonly IClioIpcClient _client;
	private readonly IEnvStateStore _envStore;
	private CancellationTokenSource? _runCts;

	/// <summary>Creates the workflow view-model over the shared IPC client and env-state store.</summary>
	public ClioWorkflowViewModel(IClioIpcClient client, IEnvStateStore envStore) {
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_envStore = envStore ?? throw new ArgumentNullException(nameof(envStore));
		_selectedEnvironment = _envStore.Load().Selected ?? string.Empty;
		_client.Disconnected += (_, _) => Dispatcher.UIThread.Post(() => {
			ConnectionState = "disconnected (child exited)";
			ServerVersion = "not connected";
		});
		BuildCommands();
	}

	/// <summary>Raised when the user asks to open the Deploy Creatio wizard (the host opens the window).</summary>
	public event EventHandler? DeployWizardRequested;

	/// <summary>Raised when the user asks to open the full tool catalog (the host opens the window).</summary>
	public event EventHandler? CatalogRequested;

	/// <summary>The allow-listed command actions.</summary>
	public ObservableCollection<WorkflowCommand> Commands { get; } = new();

	/// <summary>Parsed result rows for list-style commands (package/app cards). Cleared per run.</summary>
	public ObservableCollection<string> ResultRows { get; } = new();

	[ObservableProperty]
	private string _selectedEnvironment;

	[ObservableProperty]
	private string _connectionState = "not connected";

	[ObservableProperty]
	private string _serverVersion = "not connected";

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(RunStateLabel))]
	[NotifyPropertyChangedFor(nameof(IsRunning))]
	private WorkflowRunState _runState = WorkflowRunState.Idle;

	[ObservableProperty]
	private string _runHeader = string.Empty;

	[ObservableProperty]
	private string _output = "Select a command. Commands run against the ring's selected environment.";

	// ---- typed-confirm overlay for destructive commands ----

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanConfirm))]
	private bool _confirmVisible;

	[ObservableProperty]
	private string _confirmTitle = string.Empty;

	[ObservableProperty]
	private string _confirmDetail = string.Empty;

	/// <summary>The word the user must type to enable the confirm (the target environment name).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanConfirm))]
	private string _confirmWord = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanConfirm))]
	private string _confirmInput = string.Empty;

	private WorkflowCommand? _pendingCommand;

	/// <summary>True once the typed input exactly matches the required confirm word.</summary>
	public bool CanConfirm => ConfirmVisible
		&& !string.IsNullOrEmpty(ConfirmWord)
		&& string.Equals(ConfirmInput, ConfirmWord, StringComparison.Ordinal);

	/// <summary>Whether the selected environment is set (env-scoped commands require it).</summary>
	public bool HasEnvironment => !string.IsNullOrWhiteSpace(SelectedEnvironment);

	/// <summary>End-state label for the badge.</summary>
	public string RunStateLabel => RunState switch {
		WorkflowRunState.Running => "RUNNING",
		WorkflowRunState.Success => "SUCCESS",
		WorkflowRunState.Failure => "FAILED",
		_ => string.Empty
	};

	/// <summary>Whether a run is currently in progress.</summary>
	public bool IsRunning => RunState == WorkflowRunState.Running;

	private void BuildCommands() {
		Commands.Clear();
		Commands.Add(new WorkflowCommand { Key = "version", Title = "version", Description = "Connected clio version (from the MCP handshake — no call).", Tool = null, EnvArg = EnvArgKind.None, Destructive = false });
		Commands.Add(new WorkflowCommand { Key = "list-packages", Title = "list-packages", Description = "Packages on the selected environment (name · version · maintainer).", Tool = "list-packages", EnvArg = EnvArgKind.EnvironmentName, Destructive = false });
		Commands.Add(new WorkflowCommand { Key = "list-applications", Title = "list-applications", Description = "Installed applications on the selected environment.", Tool = "list-apps", EnvArg = EnvArgKind.EnvironmentName, Destructive = false });
		Commands.Add(new WorkflowCommand { Key = "gate", Title = "gate (install-gate)", Description = "Installs/updates cliogate on the selected environment.", Tool = "install-gate", EnvArg = EnvArgKind.EnvironmentName, Destructive = true });
		Commands.Add(new WorkflowCommand { Key = "restart", Title = "restart", Description = "Restarts the selected Creatio instance.", Tool = "restart-by-environment-name", EnvArg = EnvArgKind.EnvironmentNameCamel, Destructive = true });
		Commands.Add(new WorkflowCommand { Key = "flushdb", Title = "flushdb (clear redis)", Description = "Empties the Redis DB used by the selected instance.", Tool = "clear-redis-db-by-environment", EnvArg = EnvArgKind.EnvironmentNameCamel, Destructive = true });
	}

	/// <summary>Connects (spawns child + handshake) and records the server version.</summary>
	[RelayCommand]
	private async Task ConnectAsync() {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		try {
			ClioServerHandshake handshake = await _client.ConnectAsync().ConfigureAwait(true);
			ConnectionState = "connected";
			ServerVersion = $"{handshake.ServerName} {handshake.ServerVersion}";
		}
		catch (Exception ex) {
			ConnectionState = "error";
			ServerVersion = "not connected";
			Output = $"Connect failed: {ex.Message}";
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>Entry point for a command button: routes destructive commands through the typed confirm.</summary>
	[RelayCommand]
	private async Task RunAsync(WorkflowCommand? command) {
		if (command is null || IsBusy) {
			return;
		}

		// version is handshake-only — no call, no env needed.
		if (command.Key == "version") {
			await EnsureConnectedForVersionAsync().ConfigureAwait(true);
			RunHeader = "clio version (handshake serverInfo)";
			ResultRows.Clear();
			Output = ServerVersion;
			RunState = WorkflowRunState.Success;
			return;
		}

		if (!HasEnvironment) {
			Output = "No environment selected in the ring. Pick one first; commands target the selected env.";
			RunState = WorkflowRunState.Failure;
			return;
		}

		if (command.Destructive) {
			OpenConfirm(command);
			return;
		}

		await ExecuteAsync(command).ConfigureAwait(true);
	}

	private async Task EnsureConnectedForVersionAsync() {
		if (!_client.IsConnected || ServerVersion == "not connected") {
			await ConnectAsync().ConfigureAwait(true);
		}
	}

	private void OpenConfirm(WorkflowCommand command) {
		_pendingCommand = command;
		ConfirmTitle = $"Confirm: {command.Title}";
		ConfirmDetail =
			$"This runs the DESTRUCTIVE command '{command.Tool}' against environment '{SelectedEnvironment}'.\n" +
			$"Type the environment name below to confirm.";
		ConfirmWord = SelectedEnvironment;
		ConfirmInput = string.Empty;
		ConfirmVisible = true;
	}

	/// <summary>Confirms and runs the pending destructive command (enabled only when the typed word matches).</summary>
	[RelayCommand]
	private async Task ConfirmAsync() {
		if (!CanConfirm || _pendingCommand is null) {
			return;
		}
		WorkflowCommand command = _pendingCommand;
		CancelConfirm();
		await ExecuteAsync(command).ConfigureAwait(true);
	}

	/// <summary>Dismisses the confirm overlay without running.</summary>
	[RelayCommand]
	private void CancelConfirm() {
		ConfirmVisible = false;
		ConfirmInput = string.Empty;
		ConfirmWord = string.Empty;
		_pendingCommand = null;
	}

	/// <summary>
	/// Requests cancellation of the in-flight run. HONEST semantics: the clio operation is detached and
	/// cannot be truly aborted mid-flight — this only stops the ring from waiting on the result.
	/// </summary>
	[RelayCommand]
	private void CancelRun() {
		_runCts?.Cancel();
		AppendLine("(cancel requested — note: the clio operation is detached and continues server-side)");
	}

	/// <summary>Opens the Deploy Creatio wizard (handled by the host window).</summary>
	[RelayCommand]
	private void OpenDeployWizard() => DeployWizardRequested?.Invoke(this, EventArgs.Empty);

	/// <summary>Opens the full tool catalog (handled by the host window).</summary>
	[RelayCommand]
	private void OpenCatalog() => CatalogRequested?.Invoke(this, EventArgs.Empty);

	private async Task ExecuteAsync(WorkflowCommand command) {
		if (command.Tool is null) {
			return;
		}
		IsBusy = true;
		RunState = WorkflowRunState.Running;
		ResultRows.Clear();
		string args = BuildArgs(command);
		RunHeader = $"clio {command.Key} · env={SelectedEnvironment}";
		Output = $"Running {command.Tool} {args}…";

		_runCts?.Dispose();
		_runCts = new CancellationTokenSource();
		var progress = new Progress<string>(line => AppendLine(line));

		try {
			// Destructive commands and gate are dispatched through clio-run (they are long-tail, non-resident).
			string tool;
			string callArgs;
			if (command.Destructive || command.EnvArg == EnvArgKind.EnvironmentNameCamel) {
				tool = "clio-run";
				callArgs = $"{{\"command\":\"{command.Tool}\",\"args\":{args}}}";
			}
			else {
				tool = command.Tool;
				callArgs = args;
			}

			ClioToolCallResult result = await _client.CallToolAsync(tool, callArgs, progress, _runCts.Token).ConfigureAwait(true);
			RenderResult(command, result);
			RunState = result.IsError ? WorkflowRunState.Failure : WorkflowRunState.Success;
		}
		catch (OperationCanceledException) {
			Output += "\n(run cancelled — ring stopped waiting; clio op may still be running)";
			RunState = WorkflowRunState.Failure;
		}
		catch (Exception ex) {
			Output += $"\nError: {ex.Message}";
			RunState = WorkflowRunState.Failure;
		}
		finally {
			IsBusy = false;
		}
	}

	private string BuildArgs(WorkflowCommand command) {
		string env = JsonEncodedText.Encode(SelectedEnvironment).ToString();
		return command.EnvArg switch {
			EnvArgKind.EnvironmentName => $"{{\"environment-name\":\"{env}\"}}",
			EnvArgKind.EnvironmentNameCamel => $"{{\"environmentName\":\"{env}\"}}",
			_ => "{}"
		};
	}

	private void RenderResult(WorkflowCommand command, ClioToolCallResult result) {
		string body = result.Json ?? result.RawText;
		Output = $"{command.Key} (isError={result.IsError})";
		ResultRows.Clear();
		foreach (string row in ParseRows(command.Key, result)) {
			ResultRows.Add(row);
		}
		if (ResultRows.Count == 0) {
			Output = $"{command.Key} (isError={result.IsError}):\n{body}";
		}
	}

	// Parses list-packages / list-apps into human-readable card rows; other commands show raw output.
	private static IEnumerable<string> ParseRows(string key, ClioToolCallResult result) {
		if (string.IsNullOrWhiteSpace(result.RawText)) {
			yield break;
		}
		JsonDocument doc;
		try {
			doc = JsonDocument.Parse(result.RawText);
		}
		catch (JsonException) {
			yield break;
		}
		using (doc) {
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				yield break;
			}
			if (key == "list-packages" && root.TryGetProperty("packages", out JsonElement pkgs) && pkgs.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement p in pkgs.EnumerateArray()) {
					yield return $"{Prop(p, "name", "Name")}  ·  {Prop(p, "version", "Version")}  ·  {Prop(p, "maintainer", "Maintainer")}";
				}
			}
			else if (key == "list-applications" && root.TryGetProperty("applications", out JsonElement apps) && apps.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement a in apps.EnumerateArray()) {
					yield return $"{Prop(a, "name", "Name")}  ·  {Prop(a, "version", "Version")}  ·  {Prop(a, "code", "Code")}";
				}
			}
		}
	}

	private static string Prop(JsonElement obj, params string[] names) {
		foreach (string n in names) {
			if (obj.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String) {
				return e.GetString() ?? "";
			}
		}
		return "-";
	}

	private void AppendLine(string line) {
		void Apply() => Output += "\n" + line;
		if (Dispatcher.UIThread.CheckAccess()) {
			Apply();
		}
		else {
			Dispatcher.UIThread.Post(Apply);
		}
	}

	// ---- design/screenshot seams (no live clio) ----

	/// <summary>Design seam: set the selected environment for screenshots.</summary>
	public void DesignSetEnvironment(string env) => SelectedEnvironment = env;

	/// <summary>Design seam: set connection identity for screenshots.</summary>
	public void DesignSetConnection(string state, string version) {
		ConnectionState = state;
		ServerVersion = version;
	}

	/// <summary>Design seam: show a run result (header + state + output + optional rows).</summary>
	public void DesignShowRun(string header, WorkflowRunState state, string output, IEnumerable<string>? rows = null) {
		RunHeader = header;
		RunState = state;
		Output = output;
		ResultRows.Clear();
		if (rows is not null) {
			foreach (string r in rows) {
				ResultRows.Add(r);
			}
		}
	}

	/// <summary>Design seam: open the typed-confirm overlay for a destructive command.</summary>
	public void DesignShowConfirm(string commandKey) {
		WorkflowCommand? cmd = null;
		foreach (WorkflowCommand c in Commands) {
			if (c.Key == commandKey) {
				cmd = c;
				break;
			}
		}
		if (cmd is not null) {
			OpenConfirm(cmd);
		}
	}
}
