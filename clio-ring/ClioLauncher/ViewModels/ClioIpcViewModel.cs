using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClioLauncher.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioLauncher.ViewModels;

/// <summary>
/// View-model for the EXPERIMENTAL clio IPC proof view: shows the negotiated handshake, a searchable
/// full-catalog list (from <c>get-tool-contract {}</c>), and READ-ONLY env calls (list-environments +
/// env details). Talks to a long-lived clio MCP child through <see cref="IClioIpcClient"/>. All calls
/// run off the UI thread; results are marshalled back via the dispatcher.
/// </summary>
public sealed partial class ClioIpcViewModel : ViewModelBase {
	private readonly IClioIpcClient _client;
	private readonly List<ClioCatalogEntry> _allEntries = new();

	/// <summary>Creates the view-model bound to the shared clio IPC client.</summary>
	public ClioIpcViewModel(IClioIpcClient client) {
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_client.Disconnected += (_, _) => Dispatcher.UIThread.Post(() => {
			ConnectionState = "disconnected (child exited)";
			IsConnected = false;
		});
	}

	/// <summary>The filtered catalog entries currently shown in the list.</summary>
	public ObservableCollection<ClioCatalogEntry> Catalog { get; } = new();

	[ObservableProperty]
	private string _connectionState = "not connected";

	[ObservableProperty]
	private bool _isConnected;

	[ObservableProperty]
	private string _serverIdentity = "";

	// Self-identifying connection line, e.g. "clio 8.1.0.77 · C:\...\clio.dll".
	[ObservableProperty]
	private string _connectionSummary = "";

	[ObservableProperty]
	private string _capabilities = "";

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string _searchText = "";

	[ObservableProperty]
	private int _catalogCount;

	// "150 tools · 71 destructive · 26 resident" — the whole-catalog counts (unaffected by search).
	[ObservableProperty]
	private string _catalogSummary = "";

	// True when the connected clio returned an empty/old-shape catalog: the UI shows an explicit
	// "incompatible clio" panel (with path + version) instead of a silent partial list.
	[ObservableProperty]
	private bool _isIncompatible;

	[ObservableProperty]
	private string _incompatibleMessage = "";

	[ObservableProperty]
	private string _output = "Connect to load the clio command catalog.";

	[ObservableProperty]
	private string _selectedEnvironment = "";

	// Regenerated search filter: re-applies whenever the box changes.
	partial void OnSearchTextChanged(string value) => ApplyFilter();

	/// <summary>Connects (spawns the child + handshake) and loads the full catalog.</summary>
	[RelayCommand]
	private async Task ConnectAndLoadAsync() {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		Output = "Connecting to clio MCP server…";
		try {
			ClioServerHandshake handshake = await _client.ConnectAsync().ConfigureAwait(true);
			IsConnected = true;
			ConnectionState = "connected";
			ServerIdentity = $"{handshake.ServerName} {handshake.ServerVersion}";
			ConnectionSummary = $"{ServerIdentity} · {_client.TargetPath}";
			Capabilities = string.Join(", ", handshake.Capabilities);

			IReadOnlyList<ClioCatalogEntry> catalog = await _client.GetCatalogAsync().ConfigureAwait(true);
			int destructive = catalog.Count(e => e.Destructive);
			int resident = catalog.Count(e => e.Resident);
			CatalogSummary = $"{catalog.Count} tools · {destructive} destructive · {resident} resident";

			// Incompatible clio: empty or old-shape catalog (no safety flags). Show the explicit panel.
			if (catalog.Count == 0 || !_client.LastCatalogIsModern) {
				IsIncompatible = true;
				IncompatibleMessage =
					$"Connected clio does not expose the modern tool catalog (compact index with safety flags).\n" +
					$"Path: {_client.TargetPath}\nVersion: {handshake.ServerVersion}\n" +
					$"Update clio, or point ClioIpc at a newer build in app-settings.json.";
				_allEntries.Clear();
				ApplyFilter();
				Output = $"Incompatible clio at {_client.TargetPath} (v{handshake.ServerVersion}).";
				return;
			}

			IsIncompatible = false;
			_allEntries.Clear();
			_allEntries.AddRange(catalog.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
			ApplyFilter();
			Output = $"Loaded {catalog.Count} tools from {ServerIdentity}. Read-only calls only; destructive tools are disabled in this proof.";
		}
		catch (Exception ex) {
			IsConnected = false;
			ConnectionState = "error";
			Output = $"Connect failed: {ex.Message}";
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>Runs the read-only <c>list-environments</c> tool and shows the raw JSON result.</summary>
	[RelayCommand]
	private async Task RunListEnvironmentsAsync() {
		await RunReadOnlyAsync("list-environments", "{}").ConfigureAwait(true);
	}

	/// <summary>
	/// Runs a catalog entry from the list. In this proof only READ-ONLY tools execute; any tool flagged
	/// destructive is refused (the button is also disabled via <see cref="CanRunEntry"/>, this is the
	/// defence-in-depth guard). Non-destructive tools are dispatched read-only via <c>clio-run</c>,
	/// passing the current environment when one is entered.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanRunEntry))]
	private async Task RunEntryAsync(ClioCatalogEntry? entry) {
		if (entry is null) {
			return;
		}
		if (entry.Destructive) {
			Output = $"'{entry.Name}' is destructive — execution is disabled in this proof.";
			return;
		}
		string env = SelectedEnvironment.Trim();
		string innerArgs = string.IsNullOrEmpty(env) ? "{}" : $"{{\"environment\":\"{env}\"}}";
		string args = $"{{\"command\":\"{entry.Name}\",\"args\":{innerArgs}}}";
		await RunReadOnlyAsync("clio-run", args).ConfigureAwait(true);
	}

	/// <summary>Only non-destructive tools may be executed in the proof.</summary>
	private static bool CanRunEntry(ClioCatalogEntry? entry) => entry is { Destructive: false };

	/// <summary>
	/// Runs env details for <see cref="SelectedEnvironment"/> (read-only). <c>describe-environment</c> is a
	/// long-tail tool, so it is dispatched through the resident <c>clio-run</c> executor.
	/// </summary>
	[RelayCommand]
	private async Task RunGetInfoAsync() {
		string env = SelectedEnvironment.Trim();
		if (string.IsNullOrEmpty(env)) {
			Output = "Enter an environment name first (for example the -e alias).";
			return;
		}
		string args = $"{{\"command\":\"describe-environment\",\"args\":{{\"environment\":\"{env}\"}}}}";
		await RunReadOnlyAsync("clio-run", args).ConfigureAwait(true);
	}

	/// <summary>Restarts the child process (recovery / on-demand).</summary>
	[RelayCommand]
	private async Task RestartAsync() {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		Output = "Restarting clio MCP child…";
		try {
			ClioServerHandshake handshake = await _client.RestartAsync().ConfigureAwait(true);
			IsConnected = true;
			ConnectionState = "connected (restarted)";
			ServerIdentity = $"{handshake.ServerName} {handshake.ServerVersion}";
			Output = $"Restarted. Server {ServerIdentity}.";
		}
		catch (Exception ex) {
			IsConnected = false;
			ConnectionState = "error";
			Output = $"Restart failed: {ex.Message}";
		}
		finally {
			IsBusy = false;
		}
	}

	private async Task RunReadOnlyAsync(string tool, string argsJson) {
		if (IsBusy) {
			return;
		}
		IsBusy = true;
		Output = $"Running {tool}…";
		try {
			ClioToolCallResult result = await _client.CallToolAsync(tool, argsJson).ConfigureAwait(true);
			string body = result.Json ?? result.RawText;
			Output = $"{tool} (isError={result.IsError}):\n{body}";
		}
		catch (Exception ex) {
			Output = $"{tool} failed: {ex.Message}";
		}
		finally {
			IsBusy = false;
		}
	}

	private void ApplyFilter() {
		string query = SearchText.Trim();
		IEnumerable<ClioCatalogEntry> filtered = string.IsNullOrEmpty(query)
			? _allEntries
			: _allEntries.Where(e =>
				e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
				e.Purpose.Contains(query, StringComparison.OrdinalIgnoreCase));

		Catalog.Clear();
		foreach (ClioCatalogEntry entry in filtered) {
			Catalog.Add(entry);
		}
		CatalogCount = Catalog.Count;
	}
}
