using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton(new Options(args));
services.AddSingleton<IWire, Wire>();
services.AddSingleton<IBackendFactory, BackendFactory>();
services.AddSingleton<ISupervisor, Supervisor>();
services.AddSingleton<IWorker, Worker>();
await using var provider = services.BuildServiceProvider();
if (args.Contains("--worker")) await provider.GetRequiredService<IWorker>().RunAsync();
else await provider.GetRequiredService<ISupervisor>().RunAsync();

internal sealed record Options(string[] Args) {
    internal bool Has(string name) => Args.Contains(name);
    internal string Value(string name, string fallback) {
        int i = Array.IndexOf(Args, name);
        return i < 0 ? fallback : Args[i + 1];
    }
}
internal interface IWire { Task SendAsync(object value); }
internal sealed class Wire : IWire {
    private readonly SemaphoreSlim gate = new(1);
    public async Task SendAsync(object value) {
        await gate.WaitAsync();
        try { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); }
        finally { gate.Release(); }
    }
}
internal sealed record Command(string Kind, string? Request = null, string? Id = null,
    string? Generation = null, string? Contract = null, string? Payload = null,
    bool HoldWork = false, bool HoldCleanup = false, int DeadlineMs = 0, int BudgetMs = 0,
    bool HoldOutcome = false);
internal sealed record BackendEvent(int Epoch, string Kind, string? Id = null, int Pid = 0, string? Error = null);
internal sealed record Envelope(Command? Command = null, BackendEvent? Backend = null,
    string? Timer = null, int Epoch = 0, string? Id = null);
internal sealed record Snapshot(string Id, string Generation, string Configuration, string Contract,
    string Payload, string Status, bool Owns, int? WorkerPid, string? Reason);
internal sealed class Entry(Command command, string generation) {
    internal readonly Command Command = command;
    internal readonly string Generation = generation;
    internal readonly string Configuration = generation == "V1" ? "cfg-1" : "cfg-2";
    internal readonly DateTimeOffset? Deadline = command.DeadlineMs > 0
        ? DateTimeOffset.UtcNow.AddMilliseconds(command.DeadlineMs) : null;
    internal string Status = "Queued";
    internal bool Owns;
    internal int? WorkerPid;
    internal string? Reason;
    internal Snapshot Snapshot() => new(Command.Id!, Generation, Configuration, Command.Contract!,
        Command.Payload!, Status, Owns, WorkerPid, Reason);
}

internal interface IBackendFactory {
    Process Start(string generation, int epoch, bool fail, Action<BackendEvent> publish);
}
internal sealed class BackendFactory(Options options) : IBackendFactory {
    public Process Start(string generation, int epoch, bool fail, Action<BackendEvent> publish) {
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { Assembly.GetExecutingAssembly().Location, "--worker", "--generation", generation,
                     "--effects", options.Value("--effects", "effects.jsonl") }) start.ArgumentList.Add(arg);
        if (fail) start.ArgumentList.Add("--fail-start");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start worker");
        _ = ReadAsync();
        return process;
        async Task ReadAsync() {
            // Drain stderr as well: a child cannot block on a full redirected error pipe.
            Task<string> errors = process.StandardError.ReadToEndAsync();
            try {
                while (await process.StandardOutput.ReadLineAsync() is { } line) {
                    var message = JsonSerializer.Deserialize<BackendEvent>(line)!;
                    publish(message with { Epoch = epoch });
                }
                await process.WaitForExitAsync();
                publish(new(epoch, "Exited", Pid: process.Id, Error: await errors));
            }
            catch (Exception error) { publish(new(epoch, "Exited", Error: error.Message)); }
        }
    }
}

internal interface ISupervisor { Task RunAsync(); }
internal sealed class Supervisor(Options options, IWire wire, IBackendFactory factory) : ISupervisor {
    private readonly Channel<Envelope> inbox = Channel.CreateUnbounded<Envelope>(new() { SingleReader = true });
    private readonly Dictionary<string, Entry> entries = [];
    private readonly List<string> queue = [];
    private Process? worker;
    private string active = "V1", accepting = "V1", phase = "Starting";
    private string? running;
    private int epoch, updateEpoch;
    private bool update;
    private readonly int capacity = int.Parse(options.Value("--capacity", "16"));
    private long sequence;

    public async Task RunAsync() {
        // Console's synchronized reader may block synchronously despite the Async suffix.
        _ = Task.Run(ReadInputAsync);
        await StartAsync("V1", false);
        try {
            await foreach (var message in inbox.Reader.ReadAllAsync()) {
                if (message.Command is { } command) {
                    if (command.Kind == "shutdown") break;
                    await HandleAsync(command);
                }
                else if (message.Backend is { } backend && backend.Epoch == epoch) await ObserveAsync(backend);
                else if (message.Timer == "deadline" && entries.TryGetValue(message.Id!, out var expiring)
                         && expiring.Status == "Queued") await FinishQueuedAsync(expiring, "Expired");
                else if (message.Timer == "drain" && update && message.Epoch == updateEpoch && phase == "Draining")
                    await AbortDrainAsync();
                else if (message.Timer == "startup" && message.Epoch == epoch && phase is "Starting" or "StartingV2" or "Fallback")
                    await LostAsync("startup-timeout");
                await PumpAsync();
            }
        }
        finally { await StopAsync(); }
    }

    private async Task ReadInputAsync() {
        while (await Console.In.ReadLineAsync() is { } line) {
            try {
                var command = JsonSerializer.Deserialize<Command>(line) ?? throw new JsonException();
                await inbox.Writer.WriteAsync(new(Command: command));
            }
            catch (JsonException) { await wire.SendAsync(new { Type = "protocol-error" }); }
        }
        inbox.Writer.TryWrite(new(Command: new("shutdown")));
    }
    private Task EmitAsync(string kind, object? data = null) => wire.SendAsync(new {
        Type = "event", Kind = kind, Sequence = ++sequence, SupervisorPid = Environment.ProcessId, Data = data
    });
    private Task ReplyAsync(Command c, string code, object? data = null) => wire.SendAsync(new {
        Type = "reply", c.Request, Code = code, Data = data
    });
    private void Timer(string kind, int ms, int token, string? id = null) {
        _ = Task.Run(async () => { await Task.Delay(ms); inbox.Writer.TryWrite(new(Timer: kind, Epoch: token, Id: id)); });
    }
    private async Task StartAsync(string generation, bool fail) {
        active = generation;
        int token = ++epoch;
        try { worker = factory.Start(generation, token, fail, e => inbox.Writer.TryWrite(new(Backend: e))); }
        catch (Exception error) { inbox.Writer.TryWrite(new(Backend: new(token, "Exited", Error: error.Message))); }
        Timer("startup", 5000, token);
        await EmitAsync("Starting", new { Generation = generation, Epoch = token, WorkerPid = worker?.Id });
    }
    private async Task StopAsync() {
        if (worker is null) return;
        // Only this supervisor's exclusively owned child is terminated; never a shared process.
        if (!worker.HasExited) {
            worker.StandardInput.Close();
            try { await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { worker.Kill(entireProcessTree: true); await worker.WaitForExitAsync(); }
        }
        worker.Dispose();
        worker = null;
    }
    private async Task HandleAsync(Command c) {
        switch (c.Kind) {
            case "discover":
                await ReplyAsync(c, "ok", new { Accepting = accepting, Active = active, Phase = phase,
                    Contract = Contract(accepting), SupervisorPid = Environment.ProcessId });
                break;
            case "submit":
                if (string.IsNullOrWhiteSpace(c.Id) || c.Payload is null || c.Contract is null || c.Generation is null) {
                    await ReplyAsync(c, "invalid-request"); break;
                }
                if (entries.ContainsKey(c.Id)) { await ReplyAsync(c, "duplicate-id"); break; }
                if (phase is "Unavailable" or "Starting" or "Fallback") { await ReplyAsync(c, "unavailable"); break; }
                if (c.Generation != accepting || c.Contract != Contract(accepting)) {
                    await ReplyAsync(c, "incompatible-generation"); break;
                }
                if (queue.Count >= capacity) { await ReplyAsync(c, "queue-full"); break; }
                var entry = new Entry(c, accepting);
                entries.Add(c.Id, entry);
                queue.Add(c.Id);
                await ReplyAsync(c, "queued-volatile", entry.Snapshot());
                await EmitAsync("Queued", entry.Snapshot());
                if (c.DeadlineMs > 0) Timer("deadline", c.DeadlineMs, 0, c.Id);
                break;
            case "update":
                if (update || active != "V1" || phase != "Ready") { await ReplyAsync(c, "update-unavailable"); break; }
                // Fixture descriptor is prepared data, not an executing V2 backend.
                update = true; phase = "Draining"; accepting = "V2"; updateEpoch++;
                Timer("drain", c.BudgetMs > 0 ? c.BudgetMs : 5000, updateEpoch);
                await ReplyAsync(c, "cutoff", new { Accepting = accepting });
                await EmitAsync("Cutoff", new { Old = "V1", New = "V2" });
                break;
            case "cancel":
                if (!entries.TryGetValue(c.Id ?? "", out var cancel)) { await ReplyAsync(c, "not-found"); break; }
                if (cancel.Status != "Queued") { await ReplyAsync(c, "already-dispatched-or-terminal"); break; }
                await FinishQueuedAsync(cancel, "Cancelled");
                await ReplyAsync(c, "cancelled-not-started");
                break;
            case "query":
                await ReplyAsync(c, "ok", entries.Values.Select(e => e.Snapshot()).ToArray()); break;
            case "release-work": case "release-cleanup": case "release-outcome":
                if (c.Id != running || worker is null) { await ReplyAsync(c, "not-running"); break; }
                await SendWorkerAsync(c); await ReplyAsync(c, "signalled"); break;
            case "crash":
                if (worker is not null && !worker.HasExited) worker.Kill(entireProcessTree: true);
                await ReplyAsync(c, "crash-injected"); break;
            default: await ReplyAsync(c, "unknown-command"); break;
        }
    }
    private static string Contract(string generation) => generation == "V1" ? "write/v1" : "write/v2";
    private async Task SendWorkerAsync(Command c) {
        try { await worker!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(c)); await worker.StandardInput.FlushAsync(); }
        catch (IOException error) { inbox.Writer.TryWrite(new(Backend: new(epoch, "Exited", Error: error.Message))); }
    }
    private async Task PumpAsync() {
        if (running is not null || phase is not ("Ready" or "Draining")) return;
        var id = queue.FirstOrDefault(id => entries[id].Generation == active);
        if (id is not null) {
            var entry = entries[id];
            if (entry.Deadline is { } deadline && DateTimeOffset.UtcNow >= deadline) {
                await FinishQueuedAsync(entry, "Expired");
                await PumpAsync();
                return;
            }
            queue.Remove(id);
            running = id; entry.Status = "Dispatched"; entry.Owns = true; entry.WorkerPid = worker!.Id;
            await EmitAsync("Dispatched", entry.Snapshot());
            await SendWorkerAsync(entry.Command with { Kind = "execute", Generation = entry.Generation });
        }
        else if (update) {
            await EmitAsync("OldDrained");
            await StopAsync();
            phase = "StartingV2";
            await StartAsync("V2", options.Has("--fail-v2"));
        }
    }
    private async Task ObserveAsync(BackendEvent e) {
        if (e.Kind == "Ready") {
            if (phase == "StartingV2") { update = false; accepting = "V2"; }
            phase = "Ready";
            await EmitAsync("Ready", new { Generation = active, WorkerPid = e.Pid });
        }
        else if (e.Kind == "Exited") await LostAsync(e.Error ?? "backend-exited");
        else if (e.Id == running && entries.TryGetValue(e.Id!, out var entry)) {
            if (e.Kind == "Started") entry.Status = "Running";
            if (e.Kind == "Outcome") entry.Status = "Succeeded";
            if (e.Kind == "Rejected") { entry.Status = "NotStarted"; entry.Reason = "backend-contract-rejected"; }
            if (e.Kind == "Released") { entry.Owns = false; running = null; }
            await EmitAsync(e.Kind, entry.Snapshot());
        }
    }
    private async Task FinishQueuedAsync(Entry entry, string reason) {
        queue.Remove(entry.Command.Id!); entry.Status = "NotStarted"; entry.Reason = reason;
        await EmitAsync("NotStarted", entry.Snapshot());
    }
    private async Task FailQueueAsync(string? generation, string reason) {
        foreach (var id in queue.ToArray())
            if (generation is null || entries[id].Generation == generation) await FinishQueuedAsync(entries[id], reason);
    }
    private async Task AbortDrainAsync() {
        // This mutation deliberately illustrates why silently running V2 work on V1 is wrong.
        if (options.Has("--unsafe-fallback")) {
            foreach (var id in queue.Where(id => entries[id].Generation == "V2").ToArray()) {
                var original = entries[id];
                entries[id] = new Entry(original.Command with { Generation = "V1", Contract = "write/v1" }, "V1");
            }
        }
        else await FailQueueAsync("V2", "UpdateDeferred");
        accepting = "V1"; update = false; phase = "Ready";
        await EmitAsync("UpdateDeferred");
    }
    private async Task LostAsync(string reason) {
        string previous = phase;
        // Ignore duplicate exit notifications after reaching a terminal unavailable state.
        if (previous == "Unavailable") return;
        if (running is { } id) {
            var entry = entries[id];
            if (entry.Status is "Dispatched" or "Running") entry.Status = "Unknown";
            entry.Owns = false; entry.Reason = "ExecutorLost"; running = null;
            await EmitAsync("ExecutorLost", entry.Snapshot());
        }
        await StopAsync();
        if (previous == "StartingV2") {
            await FailQueueAsync("V2", "UpdateStartupFailed");
            update = false; accepting = "V1"; phase = "Fallback";
            await EmitAsync("Fallback");
            await StartAsync("V1", options.Has("--fail-fallback"));
        }
        else {
            update = false; phase = "Unavailable";
            await FailQueueAsync(null, "BackendUnavailable");
            await EmitAsync("Unavailable", new { Reason = reason });
        }
    }
}

internal interface IWorker { Task RunAsync(); }
internal sealed class Worker(Options options, IWire wire) : IWorker {
    private Command? current;
    private bool completed, effected;
    public async Task RunAsync() {
        if (options.Has("--fail-start")) return;
        await EventAsync("Ready");
        while (await Console.In.ReadLineAsync() is { } line) {
            var command = JsonSerializer.Deserialize<Command>(line)!;
            if (command.Kind == "execute") {
                current = command; completed = false; effected = false;
                string generation = options.Value("--generation", "V1");
                if (command.Generation != generation || command.Contract != (generation == "V1" ? "write/v1" : "write/v2")) {
                    await EventAsync("Rejected"); await ReleaseAsync(); continue;
                }
                await EventAsync("Started");
                if (!command.HoldWork) await CompleteAsync();
            }
            else if (command.Id == current?.Id && command.Kind == "release-work" && !effected) await CompleteAsync();
            else if (command.Id == current?.Id && command.Kind == "release-outcome" && effected && !completed) await PublishAsync();
            else if (command.Id == current?.Id && command.Kind == "release-cleanup" && completed) await ReleaseAsync();
        }
    }
    private Task EventAsync(string kind) => wire.SendAsync(new BackendEvent(0, kind, current?.Id, Environment.ProcessId));
    private async Task CompleteAsync() {
        var c = current!;
        string generation = options.Value("--generation", "V1");
        // Independent side-effect witness. V2 intentionally has different behavior for the same payload.
        await File.AppendAllTextAsync(options.Value("--effects", "effects.jsonl"), JsonSerializer.Serialize(new {
            c.Id, Generation = generation, Configuration = generation == "V1" ? "cfg-1" : "cfg-2",
            Pid = Environment.ProcessId, Value = generation == "V1" ? c.Payload : c.Payload!.ToUpperInvariant()
        }) + Environment.NewLine);
        effected = true;
        // Test-only handshake: this is not an authoritative outcome and never advances ledger status.
        await EventAsync("EffectWritten");
        if (!c.HoldOutcome) await PublishAsync();
    }
    private async Task PublishAsync() {
        completed = true;
        await EventAsync("Outcome");
        if (!current!.HoldCleanup) await ReleaseAsync();
    }
    private async Task ReleaseAsync() { await EventAsync("Released"); current = null; }
}
