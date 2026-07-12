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
/// a representative env call, graceful shutdown, and a child-death → respawn recovery), and writes the
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
		var results = new List<(string Step, string Value, string Notes)>();
		bool connectedOk = false;
		bool catalogOk = false;
		bool respawnOk = false;
		int catalogCount = 0;
		int destructiveCount = 0;
		ClioServerHandshake? handshake = null;
		int childDeathSeen = 0;

		var client = new ClioIpcClient(settings, log);
		client.Disconnected += (_, _) => Interlocked.Increment(ref childDeathSeen);

		try {
			// 1. Cold start: process spawn -> initialized (fused by the stdio transport).
			var sw = Stopwatch.StartNew();
			try {
				handshake = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
				sw.Stop();
				connectedOk = true;
				results.Add(("Cold start (spawn -> initialized)", Ms(sw), "First ConnectAsync; StdioClientTransport spawns + handshakes in one call."));
				log($"[ipc-proof] cold start: {Ms(sw)} (server {handshake.ServerName} {handshake.ServerVersion})");
			}
			catch (Exception ex) {
				sw.Stop();
				results.Add(("Cold start (spawn -> initialized)", "FAILED", Truncate(ex.Message, 200)));
				log($"[ipc-proof] cold start FAILED: {ex.Message}");
			}

			if (connectedOk) {
				// 2. Handshake capability record (proves version-pinning for a separate release cycle).
				log($"[ipc-proof] capabilities: {string.Join(", ", handshake!.Capabilities)} protocol={handshake.ProtocolVersion ?? "n/a"}");

				// 3. Warm protocol round-trip (ping) x5 -> closest measurable proxy for handshake RTT.
				var pings = new List<double>();
				for (int i = 0; i < 5; i++) {
					var p = Stopwatch.StartNew();
					try {
						await client.PingAsync(cancellationToken).ConfigureAwait(false);
						p.Stop();
						pings.Add(p.Elapsed.TotalMilliseconds);
					}
					catch (Exception ex) {
						log($"[ipc-proof] ping {i} failed: {ex.Message}");
					}
				}
				string pingSummary = pings.Count > 0
					? $"{pings.Min():F1}/{pings.Average():F1}/{pings.Max():F1} ms"
					: "FAILED";
				results.Add(("Handshake/warm protocol RTT (ping min/avg/max, n=5)", pingSummary,
					"Bare MCP ping round-trip; stdio fuses spawn+initialize so this is the steady-state handshake-shaped RTT."));

				// 4. Catalog fetch via get-tool-contract {} (the full ~140-tool surface).
				var cat = Stopwatch.StartNew();
				try {
					IReadOnlyList<ClioCatalogEntry> catalog = await client.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
					cat.Stop();
					catalogCount = catalog.Count;
					destructiveCount = catalog.Count(e => e.Destructive);
					bool modern = client.LastCatalogIsModern;
					catalogOk = catalogCount > 0 && modern;
					results.Add(("Catalog fetch (get-tool-contract {})", Ms(cat),
						$"{catalogCount} tools ({destructiveCount} destructive, {catalog.Count(e => e.Resident)} resident); modernIndex={modern}{(modern ? "" : " -> INCOMPATIBLE clio (old/empty shape)")}."));
					log($"[ipc-proof] catalog: {catalogCount} tools in {Ms(cat)} (modernIndex={modern})");
				}
				catch (Exception ex) {
					cat.Stop();
					results.Add(("Catalog fetch (get-tool-contract {})", "FAILED", Truncate(ex.Message, 200)));
					log($"[ipc-proof] catalog FAILED: {ex.Message}");
				}

				// 5. Warm trivial call: list-environments.
				List<string> envNames = new();
				var le = Stopwatch.StartNew();
				try {
					ClioToolCallResult envs = await client.CallToolAsync("list-environments", "{}", cancellationToken).ConfigureAwait(false);
					le.Stop();
					envNames = ExtractEnvironmentNames(envs);
					results.Add(("Warm trivial call (list-environments)", Ms(le),
						$"isError={envs.IsError}; {envNames.Count} environments; structuredContent={envs.HasStructuredContent}."));
					log($"[ipc-proof] list-environments: {envNames.Count} envs in {Ms(le)}");
				}
				catch (Exception ex) {
					le.Stop();
					results.Add(("Warm trivial call (list-environments)", "FAILED", Truncate(ex.Message, 200)));
					log($"[ipc-proof] list-environments FAILED: {ex.Message}");
				}

				// 6. Representative call: env details against a reachable env, best-effort.
				await MeasureEnvDetailAsync(client, candidateEnvNames, envNames, results, log, cancellationToken).ConfigureAwait(false);

				// 7. Child-death -> respawn recovery cycle.
				int deathBefore = Volatile.Read(ref childDeathSeen);
				int? killedPid = client.SimulateChildCrash();
				await Task.Delay(300, cancellationToken).ConfigureAwait(false);
				bool deathObserved = Volatile.Read(ref childDeathSeen) > deathBefore || !client.IsConnected;
				var respawn = Stopwatch.StartNew();
				try {
					ClioServerHandshake fresh = await client.RestartAsync(cancellationToken).ConfigureAwait(false);
					respawn.Stop();
					// Prove the fresh child actually serves calls.
					ClioToolCallResult postCheck = await client.CallToolAsync("list-environments", "{}", cancellationToken).ConfigureAwait(false);
					respawnOk = client.IsConnected && !postCheck.IsError;
					results.Add(("Child-death -> respawn recovery", Ms(respawn),
						$"killedPid={killedPid?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}; deathObserved={deathObserved}; postRespawnCallOk={!postCheck.IsError}; server {fresh.ServerName} {fresh.ServerVersion}."));
					log($"[ipc-proof] respawn: {Ms(respawn)} (deathObserved={deathObserved}, postCallOk={!postCheck.IsError})");
				}
				catch (Exception ex) {
					respawn.Stop();
					results.Add(("Child-death -> respawn recovery", "FAILED", Truncate(ex.Message, 200)));
					log($"[ipc-proof] respawn FAILED: {ex.Message}");
				}
			}

			// 8. Graceful shutdown: stdin close -> child exit (client disposal closes stdin, transport waits).
			var shut = Stopwatch.StartNew();
			try {
				await client.DisposeAsync().ConfigureAwait(false);
				shut.Stop();
				results.Add(("Bounded shutdown (stdin close -> exit)", Ms(shut),
					"DisposeAsync fires the SDK dispose (closes stdin), waits at most a 750ms grace for the owned child, then force-terminates only that child. Bounded so a Ring exit is never blocked; see the 'ipc shutdown: outcome=graceful|forced' log line. This SDK holds stdin until its own timeout, so the real shutdown reports 'forced' at ~750ms."));
				log($"[ipc-proof] shutdown: {Ms(shut)}");
			}
			catch (Exception ex) {
				shut.Stop();
				results.Add(("Graceful shutdown (stdin close -> exit)", "FAILED", Truncate(ex.Message, 200)));
				log($"[ipc-proof] shutdown FAILED: {ex.Message}");
			}
		}
		finally {
			try {
				await client.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception) {
				// Already disposed in the happy path.
			}
		}

		WriteReport(report, settings, handshake, results, catalogCount, destructiveCount, connectedOk, catalogOk, respawnOk);
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
		await File.WriteAllTextAsync(outputPath, report.ToString(), cancellationToken).ConfigureAwait(false);
		log($"[ipc-proof] wrote {outputPath}");

		return connectedOk && catalogOk && respawnOk ? 0 : 1;
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

		sb.AppendLine("## Launch");
		sb.AppendLine();
		sb.AppendLine($"- Command: `{settings.Command} {string.Join(' ', settings.Args)}`");
		sb.AppendLine($"- Working directory: `{settings.WorkingDirectory ?? "(launcher dir)"}`");
		sb.AppendLine();

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

		sb.AppendLine("## Measurements");
		sb.AppendLine();
		sb.AppendLine("| Step | Value | Notes |");
		sb.AppendLine("|------|-------|-------|");
		foreach ((string step, string value, string notes) in results) {
			sb.AppendLine($"| {step} | {value} | {notes} |");
		}
		sb.AppendLine();

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
}
