using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;
using ClioRing.Models;

namespace ClioRing.Services;

/// <summary>
/// Default <see cref="IClioAdapter"/>. Uses <see cref="System.Diagnostics.Process"/> with
/// redirected, event-driven stream reading so the UI thread is never blocked, and kills the
/// full process tree on cancellation.
/// </summary>
public sealed class ClioAdapter : IClioAdapter {
	private readonly ResolvedClioRuntime _runtime;
	private readonly IClioProcessGate _processGate;

	/// <summary>Creates an adapter using the installed release clio.</summary>
	public ClioAdapter() : this(new ResolvedClioRuntime(ClioRuntimeMode.Release, ClioIpcSettings.Default),
		new ClioProcessGate()) { }

	/// <summary>Creates an adapter using the same immutable runtime selected for IPC workflows.</summary>
	/// <param name="runtime">Resolved runtime and child-process launch settings.</param>
	public ClioAdapter(ResolvedClioRuntime runtime) : this(runtime, new ClioProcessGate()) { }

	/// <summary>Creates an adapter coordinated with the application-wide clio process gate.</summary>
	public ClioAdapter(ResolvedClioRuntime runtime, IClioProcessGate processGate) {
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_processGate = processGate ?? throw new ArgumentNullException(nameof(processGate));
	}

	/// <inheritdoc />
	public event EventHandler<ClioOutputLine>? OutputReceived;

	/// <inheritdoc />
	public async Task<IReadOnlyList<ClioEnvironment>> ListEnvironmentsAsync(CancellationToken cancellationToken = default) {
		try {
			ClioRunResult result = await RunAsync(
				new ClioInvocation { Verb = "show-web-app-list" },
				onOutput: null,
				cancellationToken).ConfigureAwait(false);

			return ParseEnvironments(result.RawStdout);
		}
		catch (OperationCanceledException) {
			throw;
		}
		catch (Exception) {
			// Environment discovery is best-effort; the ring falls back to the static catalog.
			return Array.Empty<ClioEnvironment>();
		}
	}

	/// <inheritdoc />
	public async Task<ClioRunResult> RunAsync(
		ClioInvocation invocation,
		Action<ClioOutputLine>? onOutput = null,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(invocation);
		await using IAsyncDisposable? processLease = _runtime.Mode == ClioRuntimeMode.Release
			? await _processGate.AcquireProcessLeaseAsync(cancellationToken).ConfigureAwait(false)
			: null;

		ProcessStartInfo startInfo = BuildStartInfo(invocation);

		using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

		var stdout = new StringBuilder();
		var stderr = new StringBuilder();

		process.OutputDataReceived += (_, e) => HandleLine(e.Data, ClioStream.Stdout, stdout, onOutput);
		process.ErrorDataReceived += (_, e) => HandleLine(e.Data, ClioStream.Stderr, stderr, onOutput);

		try {
			process.Start();
		}
		catch (Exception ex) {
			string message = $"Failed to start '{startInfo.FileName}': {ex.Message}";
			var line = new ClioOutputLine(ClioStream.Stderr, message, Stopwatch.GetTimestamp());
			stderr.AppendLine(message);
			onOutput?.Invoke(line);
			OutputReceived?.Invoke(this, line);
			return new ClioRunResult(-1, stdout.ToString(), stderr.ToString(), Cancelled: false);
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		bool cancelled = false;
		try {
			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			cancelled = true;
			KillTree(process);
		}

		int exitCode;
		try {
			exitCode = cancelled ? -1 : process.ExitCode;
		}
		catch (InvalidOperationException) {
			exitCode = -1;
		}

		return new ClioRunResult(exitCode, stdout.ToString(), stderr.ToString(), cancelled);
	}

	internal ProcessStartInfo BuildStartInfo(ClioInvocation invocation) {
		var startInfo = new ProcessStartInfo {
			FileName = _runtime.LaunchSettings.Command,
			WorkingDirectory = _runtime.LaunchSettings.WorkingDirectory ?? string.Empty,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};

		foreach (string argument in _runtime.LaunchSettings.Args.TakeWhile(argument =>
			!string.Equals(argument, "mcp-server", StringComparison.OrdinalIgnoreCase))) {
			startInfo.ArgumentList.Add(argument);
		}
		startInfo.ArgumentList.Add(invocation.Verb);
		foreach (string arg in invocation.Args) {
			startInfo.ArgumentList.Add(arg);
		}

		if (!string.IsNullOrWhiteSpace(invocation.EnvName)) {
			startInfo.ArgumentList.Add("-e");
			startInfo.ArgumentList.Add(invocation.EnvName);
		}

		return startInfo;
	}

	private void HandleLine(string? data, ClioStream stream, StringBuilder sink, Action<ClioOutputLine>? onOutput) {
		if (data is null) {
			return; // End-of-stream sentinel.
		}

		sink.AppendLine(data);
		var line = new ClioOutputLine(stream, data, Stopwatch.GetTimestamp());
		onOutput?.Invoke(line);
		OutputReceived?.Invoke(this, line);
	}

	private static void KillTree(Process process) {
		try {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException) {
			// Process already exited between the check and the kill.
		}
		catch (System.ComponentModel.Win32Exception) {
			// Access/handle race while tearing down the tree; nothing more we can do.
		}
	}

	/// <summary>
	/// Extracts environments (name + url + .NET flavour) from the JSON emitted by
	/// <c>clio show-web-app-list</c>. The command prints a non-JSON preamble line, so parsing
	/// starts at the first '{'. Credentials in the JSON are deliberately ignored.
	/// </summary>
	internal static IReadOnlyList<ClioEnvironment> ParseEnvironments(string rawStdout) {
		if (string.IsNullOrWhiteSpace(rawStdout)) {
			return Array.Empty<ClioEnvironment>();
		}

		int brace = rawStdout.IndexOf('{');
		if (brace < 0) {
			return Array.Empty<ClioEnvironment>();
		}

		var environments = new List<ClioEnvironment>();
		try {
			using JsonDocument doc = JsonDocument.Parse(rawStdout[brace..]);
			if (doc.RootElement.TryGetProperty("Environments", out JsonElement envs)
				&& envs.ValueKind == JsonValueKind.Object) {
				foreach (JsonProperty env in envs.EnumerateObject()) {
					environments.Add(ParseEnvironment(env));
				}
			}
		}
		catch (JsonException) {
			return Array.Empty<ClioEnvironment>();
		}

		return environments;
	}

	private static ClioEnvironment ParseEnvironment(JsonProperty environment) {
		if (environment.Value.ValueKind != JsonValueKind.Object) {
			return new ClioEnvironment(environment.Name, null, false);
		}
		string? uri = environment.Value.TryGetProperty("Uri", out JsonElement uriElement)
			&& uriElement.ValueKind == JsonValueKind.String
				? uriElement.GetString()
				: null;
		bool isNetCore = environment.Value.TryGetProperty("IsNetCore", out JsonElement netCoreElement)
			&& netCoreElement.ValueKind is JsonValueKind.True or JsonValueKind.False
			&& netCoreElement.GetBoolean();
		return new ClioEnvironment(environment.Name, uri, isNetCore);
	}
}
