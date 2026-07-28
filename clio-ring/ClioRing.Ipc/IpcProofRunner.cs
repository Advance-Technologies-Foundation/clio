using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClioRing.Ipc;

/// <summary>
/// Console/test-mode measurements harness for the clio MCP-over-stdio proof. Spawns one clio child,
/// exercises the full read-only lifecycle (cold start, warm round-trip, catalog fetch, a trivial call,
/// a representative env call, graceful shutdown, and an explicit transport restart recovery), and writes the
/// numbers to a Markdown report. Never touches the GUI. Invoked via <c>--ipc-proof</c>.
/// </summary>
public static class IpcProofRunner {
	/// <summary>
	/// Runs the proof end to end and writes <paramref name="outputPath"/>. Returns 0 on a fully green
	/// run (connected, catalog fetched, respawn recovered), non-zero otherwise. Every step is guarded so
	/// a single failure is recorded rather than aborting the whole harness.
	/// </summary>
	public static async Task<int> RunAsync(
		ClioIpcSettings settings,
		string outputPath,
		IReadOnlyList<string> candidateEnvNames,
		Action<string> log,
		CancellationToken cancellationToken = default) {
		log($"[ipc-proof] command: {settings.Command} {string.Join(' ', settings.Args)}");

		var report = new StringBuilder();
		var state = new ProofState();

		var client = new ClioIpcClient(settings, log);

		try {
			await ConnectAsync(client, state, log, cancellationToken).ConfigureAwait(false);
			if (state.ConnectedOk) {
				await RunConnectedProofAsync(client, state, candidateEnvNames, log, cancellationToken).ConfigureAwait(false);
			}
			await ShutdownAsync(client, state.Results, log).ConfigureAwait(false);
		}
		finally {
			try {
				await client.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception) {
				// Already disposed in the happy path.
			}
		}

		WriteReport(report, settings, state.Handshake, state.Results, state.CatalogCount, state.DestructiveCount,
			state.ConnectedOk, state.CatalogOk, state.RespawnOk);
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
		await File.WriteAllTextAsync(outputPath, report.ToString(), cancellationToken).ConfigureAwait(false);
		log($"[ipc-proof] wrote {outputPath}");

		return state.ConnectedOk && state.CatalogOk && state.RespawnOk ? 0 : 1;
	}

	private static async Task ConnectAsync(ClioIpcClient client, ProofState state, Action<string> log,
		CancellationToken cancellationToken) {
		var stopwatch = Stopwatch.StartNew();
		try {
			state.Handshake = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
			state.ConnectedOk = true;
			state.Results.Add(("Cold start (spawn -> initialized)", Ms(stopwatch),
				"First ConnectAsync; StdioClientTransport spawns + handshakes in one call."));
			log($"[ipc-proof] cold start: {Ms(stopwatch)} (server {state.Handshake.ServerName} {state.Handshake.ServerVersion})");
		}
		catch (Exception ex) {
			state.Results.Add(("Cold start (spawn -> initialized)", "FAILED", Truncate(ex.Message, 200)));
			log($"[ipc-proof] cold start FAILED: {ex.Message}");
		}
		finally {
			stopwatch.Stop();
		}
	}

	private static async Task RunConnectedProofAsync(ClioIpcClient client, ProofState state,
		IReadOnlyList<string> candidateEnvNames, Action<string> log, CancellationToken cancellationToken) {
		log($"[ipc-proof] capabilities: {string.Join(", ", state.Handshake!.Capabilities)} protocol={state.Handshake.ProtocolVersion ?? "n/a"}");
		await MeasurePingsAsync(client, state.Results, log, cancellationToken).ConfigureAwait(false);
		await FetchCatalogAsync(client, state, log, cancellationToken).ConfigureAwait(false);
		IReadOnlyList<string> envNames = await ListEnvironmentsAsync(client, state.Results, log, cancellationToken)
			.ConfigureAwait(false);
		await MeasureEnvDetailAsync(client, candidateEnvNames, envNames, state.Results, log, cancellationToken)
			.ConfigureAwait(false);
		await MeasureRestartAsync(client, state, log, cancellationToken).ConfigureAwait(false);
	}

	private static async Task MeasurePingsAsync(ClioIpcClient client,
		ICollection<(string Step, string Value, string Notes)> results, Action<string> log,
		CancellationToken cancellationToken) {
		var pings = new List<double>();
		for (int index = 0; index < 5; index++) {
			var stopwatch = Stopwatch.StartNew();
			try {
				await client.PingAsync(cancellationToken).ConfigureAwait(false);
				pings.Add(stopwatch.Elapsed.TotalMilliseconds);
			}
			catch (Exception ex) {
				log($"[ipc-proof] ping {index} failed: {ex.Message}");
			}
		}
		string summary = pings.Count > 0 ? $"{pings.Min():F1}/{pings.Average():F1}/{pings.Max():F1} ms" : "FAILED";
		results.Add(("Handshake/warm protocol RTT (ping min/avg/max, n=5)", summary,
			"Bare MCP ping round-trip; stdio fuses spawn+initialize so this is the steady-state handshake-shaped RTT."));
	}

	private static async Task FetchCatalogAsync(ClioIpcClient client, ProofState state, Action<string> log,
		CancellationToken cancellationToken) {
		var stopwatch = Stopwatch.StartNew();
		try {
			IReadOnlyList<ClioCatalogEntry> catalog = await client.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
			state.CatalogCount = catalog.Count;
			state.DestructiveCount = catalog.Count(entry => entry.Destructive);
			bool modern = client.LastCatalogIsModern;
			state.CatalogOk = state.CatalogCount > 0 && modern;
			state.Results.Add(("Catalog fetch (get-tool-contract {})", Ms(stopwatch),
				$"{state.CatalogCount} tools ({state.DestructiveCount} destructive, {catalog.Count(entry => entry.Resident)} resident); modernIndex={modern}{(modern ? "" : " -> INCOMPATIBLE clio (old/empty shape)")}."));
			log($"[ipc-proof] catalog: {state.CatalogCount} tools in {Ms(stopwatch)} (modernIndex={modern})");
		}
		catch (Exception ex) {
			state.Results.Add(("Catalog fetch (get-tool-contract {})", "FAILED", Truncate(ex.Message, 200)));
			log($"[ipc-proof] catalog FAILED: {ex.Message}");
		}
	}

	private static async Task<IReadOnlyList<string>> ListEnvironmentsAsync(ClioIpcClient client,
		ICollection<(string Step, string Value, string Notes)> results, Action<string> log,
		CancellationToken cancellationToken) {
		var stopwatch = Stopwatch.StartNew();
		try {
			ClioToolCallResult response = await client.CallToolAsync("list-environments", "{}", cancellationToken)
				.ConfigureAwait(false);
			List<string> names = ExtractEnvironmentNames(response);
			results.Add(("Warm trivial call (list-environments)", Ms(stopwatch),
				$"isError={response.IsError}; {names.Count} environments; structuredContent={response.HasStructuredContent}."));
			log($"[ipc-proof] list-environments: {names.Count} envs in {Ms(stopwatch)}");
			return names;
		}
		catch (Exception ex) {
			results.Add(("Warm trivial call (list-environments)", "FAILED", Truncate(ex.Message, 200)));
			log($"[ipc-proof] list-environments FAILED: {ex.Message}");
			return [];
		}
	}

	private static async Task MeasureRestartAsync(ClioIpcClient client, ProofState state, Action<string> log,
		CancellationToken cancellationToken) {
		var stopwatch = Stopwatch.StartNew();
		try {
			ClioServerHandshake fresh = await client.RestartAsync(cancellationToken).ConfigureAwait(false);
			ClioToolCallResult postCheck = await client.CallToolAsync("list-environments", "{}", cancellationToken)
				.ConfigureAwait(false);
			state.RespawnOk = client.IsConnected && !postCheck.IsError;
			state.Results.Add(("Transport restart recovery", Ms(stopwatch),
				$"postRestartCallOk={!postCheck.IsError}; server {fresh.ServerName} {fresh.ServerVersion}."));
			log($"[ipc-proof] restart: {Ms(stopwatch)} (postCallOk={!postCheck.IsError})");
		}
		catch (Exception ex) {
			state.Results.Add(("Transport restart recovery", "FAILED", Truncate(ex.Message, 200)));
			log($"[ipc-proof] restart FAILED: {ex.Message}");
		}
	}

	private static async Task ShutdownAsync(ClioIpcClient client,
		ICollection<(string Step, string Value, string Notes)> results, Action<string> log) {
		var stopwatch = Stopwatch.StartNew();
		try {
			await client.DisposeAsync().ConfigureAwait(false);
			results.Add(("Bounded transport-owned shutdown", Ms(stopwatch),
				"DisposeAsync awaits the MCP SDK transport, which owns the exact child and applies its configured 750ms shutdown timeout."));
			log($"[ipc-proof] shutdown: {Ms(stopwatch)}");
		}
		catch (Exception ex) {
			results.Add(("Graceful shutdown (stdin close -> exit)", "FAILED", Truncate(ex.Message, 200)));
			log($"[ipc-proof] shutdown FAILED: {ex.Message}");
		}
	}

	private static async Task MeasureEnvDetailAsync(
		ClioIpcClient client,
		IReadOnlyList<string> candidateEnvNames,
		IReadOnlyList<string> discoveredEnvNames,
		List<(string, string, string)> results,
		Action<string> log,
		CancellationToken cancellationToken) {
		// Prefer a candidate env that actually exists; otherwise any discovered env.
		string? target = candidateEnvNames.FirstOrDefault(discoveredEnvNames.Contains)
			?? discoveredEnvNames.FirstOrDefault();

		if (target is null) {
			results.Add(("Representative call (env details)", "SKIPPED",
				"No environment reachable/registered; list-environments RTT above is the representative call instead."));
			log("[ipc-proof] env-detail SKIPPED: no environment available");
			return;
		}

		const string detailTool = "describe-environment";

		// Also exercise GetToolContractAsync (per-tool contract retrieval) on that tool.
		try {
			ClioToolCallResult contract = await client.GetToolContractAsync(detailTool, cancellationToken).ConfigureAwait(false);
			log($"[ipc-proof] get-tool-contract({detailTool}): isError={contract.IsError}, {contract.RawText.Length} chars");
		}
		catch (Exception ex) {
			log($"[ipc-proof] get-tool-contract({detailTool}) failed: {ex.Message}");
		}

		// describe-environment is a long-tail (non-resident) tool, so it is dispatched through the
		// resident clio-run executor: {"command":<tool>,"args":{…}}. This is read-only.
		string clioRunArgs = $"{{\"command\":\"{detailTool}\",\"args\":{{\"environment\":\"{target}\"}}}}";
		var sw = Stopwatch.StartNew();
		try {
			ClioToolCallResult detail = await client.CallToolAsync("clio-run", clioRunArgs, cancellationToken).ConfigureAwait(false);
			sw.Stop();
			results.Add(($"Representative call (clio-run -> {detailTool}, env={target})", Ms(sw),
				$"isError={detail.IsError}; {detail.RawText.Length} chars returned (env may be offline — call path is what is proven)."));
			log($"[ipc-proof] {detailTool} via clio-run ({target}): {Ms(sw)} (isError={detail.IsError})");
		}
		catch (Exception ex) {
			sw.Stop();
			results.Add(($"Representative call (clio-run -> {detailTool}, env={target})", "FAILED", Truncate(ex.Message, 200)));
			log($"[ipc-proof] {detailTool} via clio-run FAILED: {ex.Message}");
		}
	}

	private static List<string> ExtractEnvironmentNames(ClioToolCallResult result) {
		var names = new List<string>();
		if (string.IsNullOrWhiteSpace(result.RawText)) {
			return names;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(result.RawText);
			CollectEnvNames(doc.RootElement, names);
		}
		catch (JsonException) {
			// Non-JSON payload; leave empty.
		}
		return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	// list-environments shape is tolerated flexibly: an array of {name}/{Name}, or an object whose
	// property names are the environments (the show-web-app-list "Environments" map shape).
	private static void CollectEnvNames(JsonElement element, List<string> names) {
		switch (element.ValueKind) {
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray()) {
					if (item.ValueKind == JsonValueKind.String) {
						names.Add(item.GetString()!);
					}
					else if (item.ValueKind == JsonValueKind.Object) {
						if (item.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String) {
							names.Add(n.GetString()!);
						}
						else if (item.TryGetProperty("Name", out JsonElement n2) && n2.ValueKind == JsonValueKind.String) {
							names.Add(n2.GetString()!);
						}
					}
				}
				break;
			case JsonValueKind.Object:
				if (element.TryGetProperty("environments", out JsonElement envsLower)) {
					CollectEnvNames(envsLower, names);
				}
				else if (element.TryGetProperty("Environments", out JsonElement envsUpper)) {
					foreach (JsonProperty prop in envsUpper.EnumerateObject()) {
						names.Add(prop.Name);
					}
				}
				break;
		}
	}

	private static void WriteReport(
		StringBuilder sb,
		ClioIpcSettings settings,
		ClioServerHandshake? handshake,
		List<(string Step, string Value, string Notes)> results,
		int catalogCount,
		int destructiveCount,
		bool connectedOk,
		bool catalogOk,
		bool respawnOk) {
		sb.AppendLine("# clio ring — MCP-over-stdio IPC proof");
		sb.AppendLine();
		sb.AppendLine($"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z by `--ipc-proof`._");
		sb.AppendLine();
		string verdict = connectedOk && catalogOk && respawnOk ? "PASS" : "PARTIAL/FAIL";
		sb.AppendLine($"**Verdict: {verdict}** — connected={connectedOk}, catalog={catalogOk}, respawn={respawnOk}.");
		sb.AppendLine();
		AppendLaunch(sb, settings);
		AppendHandshake(sb, handshake);
		AppendMeasurements(sb, results);
		AppendCatalog(sb, catalogCount, destructiveCount);
	}

	private static void AppendLaunch(StringBuilder sb, ClioIpcSettings settings) {
		sb.AppendLine("## Launch");
		sb.AppendLine();
		sb.AppendLine($"- Command: `{settings.Command} {string.Join(' ', settings.Args)}`");
		sb.AppendLine($"- Working directory: `{settings.WorkingDirectory ?? "(launcher dir)"}`");
		sb.AppendLine();
	}

	private static void AppendHandshake(StringBuilder sb, ClioServerHandshake? handshake) {
		sb.AppendLine("## Handshake (initialize)");
		sb.AppendLine();
		if (handshake is not null) {
			sb.AppendLine($"- Server: **{handshake.ServerName} {handshake.ServerVersion}**");
			sb.AppendLine($"- Protocol version: `{handshake.ProtocolVersion ?? "n/a"}`");
			sb.AppendLine($"- Capabilities: {string.Join(", ", handshake.Capabilities.Select(c => $"`{c}`"))}");
			sb.AppendLine($"- Instructions advertised: {(string.IsNullOrEmpty(handshake.Instructions) ? "no" : $"yes ({handshake.Instructions!.Length} chars)")}");
		}
		else {
			sb.AppendLine("- Handshake did not complete.");
		}
		sb.AppendLine();
	}

	private static void AppendMeasurements(StringBuilder sb,
		IEnumerable<(string Step, string Value, string Notes)> results) {
		sb.AppendLine("## Measurements");
		sb.AppendLine();
		sb.AppendLine("| Step | Value | Notes |");
		sb.AppendLine("|------|-------|-------|");
		foreach ((string step, string value, string notes) in results) {
			sb.AppendLine($"| {step} | {value} | {notes} |");
		}
		sb.AppendLine();
	}

	private static void AppendCatalog(StringBuilder sb, int catalogCount, int destructiveCount) {
		sb.AppendLine("## Catalog");
		sb.AppendLine();
		sb.AppendLine($"- Total tools discovered via `get-tool-contract {{}}`: **{catalogCount}**");
		sb.AppendLine($"- Destructive tools: {destructiveCount}");
		sb.AppendLine();
		sb.AppendLine("---");
		sb.AppendLine("READ-ONLY proof. No destructive tools were invoked.");
	}

	private static string Ms(Stopwatch sw) => $"{sw.Elapsed.TotalMilliseconds:F1} ms";

	private static string Truncate(string value, int max) =>
		value.Length <= max ? value : value[..max] + "…";

	private sealed class ProofState {
		public List<(string Step, string Value, string Notes)> Results { get; } = [];
		public ClioServerHandshake? Handshake { get; set; }
		public bool ConnectedOk { get; set; }
		public bool CatalogOk { get; set; }
		public bool RespawnOk { get; set; }
		public int CatalogCount { get; set; }
		public int DestructiveCount { get; set; }
	}
}
