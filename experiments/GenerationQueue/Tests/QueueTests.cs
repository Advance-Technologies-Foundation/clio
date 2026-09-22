using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;

namespace GenerationQueue.Tests;

/// <summary>Black-box scenarios using a persistent client pipe and real backend processes.</summary>
[TestFixture, NonParallelizable]
public sealed class QueueTests {
    private Session session = null!;

    /// <summary>Starts a fresh, exclusively owned supervisor per scenario.</summary>
    [SetUp]
    public async Task SetUp() {
        session = new Session(TestContext.CurrentContext.Test.Name);
        await session.StartAsync();
    }

    /// <summary>Stops only owned processes and retains transcript/effect evidence.</summary>
    [TearDown]
    public async Task TearDown() => await session.DisposeAsync();

    /// <summary>Exercises queued V1, executing V1 cleanup and held V2 across replacement.</summary>
    [Test, Description("Q1: cutoff drains all V1 ownership before V2 on the same supervisor connection")]
    public async Task Generation_cutoff_drains_queue_and_cleanup_before_replacement() {
        // Arrange
        await session.SubmitAsync("old-running", "V1", holdWork: true, holdCleanup: true);
        await session.EventAsync("Started", "old-running");
        await session.SubmitAsync("old-queued", "V1");
        // Act
        await session.CallAsync("update", budgetMs: 10000);
        await session.SubmitAsync("new-queued", "V2");
        var stale = await session.SubmitAsync("stale", "V1");
        var before = await session.CallAsync("query");
        await session.CallAsync("release-work", id: "old-running");
        await session.EventAsync("Outcome", "old-running");
        var cleaning = await session.CallAsync("query");
        await session.CallAsync("release-cleanup", id: "old-running");
        await session.EventAsync("Released", "new-queued");
        // Assert
        stale.GetProperty("Code").GetString().Should().Be("incompatible-generation", because: "cutoff must stop new V1 admissions");
        Status(before, "old-queued").Should().Be("Queued", because: "the held operation makes old backlog observable");
        Status(before, "new-queued").Should().Be("Queued", because: "V2 cannot execute while V1 owns work");
        Find(cleaning, "old-running").GetProperty("Owns").GetBoolean().Should().BeTrue(because: "published success must retain cleanup ownership");
        session.HasEvent("OldDrained", cleaning).Should().BeFalse(because: "cleanup is part of draining, not merely result publication");
        var effects = session.Effects();
        effects.Select(e => e.GetProperty("Id").GetString()).Should().Equal(["old-running", "old-queued", "new-queued"], because: "both old entries finish before the new generation executes");
        effects.Select(e => e.GetProperty("Generation").GetString()).Should().Equal(["V1", "V1", "V2"], because: "each accepted request must retain its generation");
        effects.Select(e => e.GetProperty("Configuration").GetString()).Should().Equal(["cfg-1", "cfg-1", "cfg-2"], because: "fixture configuration travels with its generation");
        effects[0].GetProperty("Pid").GetInt32().Should().NotBe(effects[2].GetProperty("Pid").GetInt32(), because: "the backend must really be replaced");
        effects[0].GetProperty("Value").GetString().Should().Be("mixed", because: "V1 behavior must remain intact");
        effects[2].GetProperty("Value").GetString().Should().Be("MIXED", because: "V2 exercises incompatible behavior, not merely a different label");
        session.Events.Select(e => e.GetProperty("SupervisorPid").GetInt32()).Distinct().Should().ContainSingle(because: "the same supervisor and client pipes survive handover");
        session.Sequence("Released", "old-queued").Should().BeLessThan(session.Sequence("OldDrained"), because: "retirement follows all old ownership release");
        session.Sequence("OldDrained").Should().BeLessThan(session.Sequence("Started", "new-queued"), because: "new execution follows the drain boundary");
        var after = await session.CallAsync("query");
        Status(after, "old-running").Should().Be("Succeeded", because: "the surviving supervisor retains old outcomes");
    }

    /// <summary>Proves waiting V2 work is not silently routed to fallback V1.</summary>
    [Test, Description("Q2: hung V1 defers update and resolves V2 backlog as not started")]
    public async Task Drain_timeout_does_not_retag_new_requests() {
        // Arrange
        await session.SubmitAsync("held", "V1", holdWork: true);
        await session.EventAsync("Started", "held");
        // Act
        await session.CallAsync("update", budgetMs: 800);
        await session.SubmitAsync("v2", "V2");
        await session.EventAsync("UpdateDeferred");
        var state = await session.CallAsync("query");
        await session.CallAsync("release-work", id: "held");
        await session.EventAsync("Released", "held");
        await session.SubmitAsync("resumed", "V1");
        await session.EventAsync("Released", "resumed");
        // Assert
        Status(state, "v2").Should().Be("NotStarted", because: "an incompatible accepted request needs a truthful exit when activation is deferred");
        Find(state, "v2").GetProperty("Generation").GetString().Should().Be("V2", because: "fallback must not rewrite caller intent");
        Status(state, "held").Should().Be("Running", because: "a drain timeout must not kill active work");
        session.Effects().Select(e => e.GetProperty("Id").GetString()).Should().Equal(["held", "resumed"], because: "V2 work must never execute on retained V1");
    }

    /// <summary>Checks fallback after a real child fails before readiness.</summary>
    [Test, Description("Q3: failed V2 startup rejects its queue and starts retained V1")]
    public async Task Failed_candidate_resolves_queue_and_falls_back() {
        // Arrange
        await session.RestartAsync("--fail-v2");
        await session.SubmitAsync("held", "V1", holdWork: true);
        await session.EventAsync("Started", "held");
        await session.CallAsync("update");
        await session.SubmitAsync("v2", "V2");
        // Act
        await session.CallAsync("release-work", id: "held");
        await session.EventAsync("Fallback");
        await session.EventAsync("Ready", occurrence: 2);
        var state = await session.CallAsync("query");
        await session.SubmitAsync("fallback", "V1");
        await session.EventAsync("Released", "fallback");
        // Assert
        Status(state, "v2").Should().Be("NotStarted", because: "V2 startup failure cannot authorize incompatible execution");
        session.Effects().Select(e => e.GetProperty("Id").GetString()).Should().Equal(["held", "fallback"], because: "only the retained compatible generation may resume");
    }

    /// <summary>Checks that retained V1 restart is an attempt, not a success guarantee.</summary>
    [Test, Description("Q4: failed candidate and fallback yield unavailable without running queued V2")]
    public async Task Failed_fallback_is_explicitly_unavailable() {
        // Arrange
        await session.RestartAsync("--fail-v2", "--fail-fallback");
        await session.SubmitAsync("held", "V1", holdWork: true);
        await session.EventAsync("Started", "held");
        await session.CallAsync("update");
        await session.SubmitAsync("v2", "V2");
        // Act
        await session.CallAsync("release-work", id: "held");
        await session.EventAsync("Unavailable");
        var rejected = await session.SubmitAsync("later", "V1");
        // Assert
        rejected.GetProperty("Code").GetString().Should().Be("unavailable", because: "bounded fallback attempts cannot guarantee recovery");
        session.Effects().Should().ContainSingle(because: "neither failed backend may perform queued work");
    }

    /// <summary>Checks queue cancellation, expiry, capacity and continued service.</summary>
    [Test, Description("Q5: bounded queue cancels and expires never-dispatched work")]
    public async Task Queue_limits_and_waiting_cancellation_are_real() {
        // Arrange
        await session.RestartAsync("--capacity", "2");
        await session.SubmitAsync("held", "V1", holdWork: true);
        await session.EventAsync("Started", "held");
        await session.SubmitAsync("cancel", "V1");
        await session.SubmitAsync("expire", "V1", deadlineMs: 500);
        // Act
        var overflow = await session.SubmitAsync("overflow", "V1");
        await session.CallAsync("cancel", id: "cancel");
        await session.EventAsync("NotStarted", "expire");
        await session.CallAsync("release-work", id: "held");
        await session.EventAsync("Released", "held");
        var query = await session.CallAsync("query");
        // Assert
        overflow.GetProperty("Code").GetString().Should().Be("queue-full", because: "always accept must be bounded by capacity");
        Status(query, "cancel").Should().Be("NotStarted", because: "cancelled queued work must never be dispatched");
        Find(query, "expire").GetProperty("Reason").GetString().Should().Be("Expired", because: "waiting requests have a finite deadline");
        session.Effects().Should().ContainSingle(because: "cancelled, expired and rejected requests cannot create effects");
    }

    /// <summary>Tests crash uncertainty and no automatic re-execution.</summary>
    [Test, Description("Q6: executor loss preserves uncertainty and rejects untouched backlog")]
    public async Task Crash_does_not_infer_failure_or_replay() {
        // Arrange
        await session.SubmitAsync("lost", "V1", holdWork: true);
        await session.EventAsync("Started", "lost");
        await session.SubmitAsync("waiting", "V1");
        // Act
        await session.CallAsync("crash");
        await session.EventAsync("Unavailable");
        var state = await session.CallAsync("query");
        // Assert
        Status(state, "lost").Should().Be("Unknown", because: "losing an executor cannot establish the remote outcome");
        Status(state, "waiting").Should().Be("NotStarted", because: "the supervisor knows it never dispatched that request");
        session.Effects().Should().BeEmpty(because: "this fixture was killed before its explicit effect release and does not replay");
    }

    /// <summary>Tests retaining known success after the executor dies during cleanup.</summary>
    [Test, Description("Q7: known success survives worker death during retained cleanup")]
    public async Task Known_success_survives_executor_loss() {
        // Arrange
        await session.SubmitAsync("completed", "V1", holdCleanup: true);
        await session.EventAsync("Outcome", "completed");
        // Act
        await session.CallAsync("crash");
        await session.EventAsync("Unavailable");
        var state = await session.CallAsync("query");
        // Assert
        Status(state, "completed").Should().Be("Succeeded", because: "executor death does not erase a known outcome");
        Find(state, "completed").GetProperty("Owns").GetBoolean().Should().BeFalse(because: "the dead process no longer owns local execution resources");
        session.Effects().Should().ContainSingle(because: "a completed effect is not automatically replayed");
    }

    /// <summary>Checks contract validation and duplicate identifiers before dispatch.</summary>
    [Test, Description("Q8: wrong contracts and duplicate operation IDs never reach a backend")]
    public async Task Contract_and_identity_are_checked_at_receipt() {
        // Arrange
        await session.SubmitAsync("one", "V1", holdWork: true);
        await session.EventAsync("Started", "one");
        // Act
        var wrong = await session.CallAsync("submit", id: "wrong", generation: "V1", contract: "write/v2", payload: "mixed");
        var duplicate = await session.SubmitAsync("one", "V1");
        var missing = await session.CallAsync("submit", id: "missing", payload: "mixed");
        // Assert
        wrong.GetProperty("Code").GetString().Should().Be("incompatible-generation", because: "a generation alone cannot authorize a different contract");
        duplicate.GetProperty("Code").GetString().Should().Be("duplicate-id", because: "a duplicate must not create a second execution");
        missing.GetProperty("Code").GetString().Should().Be("invalid-request", because: "this probe requires explicit caller version intent");
        session.Effects().Should().BeEmpty(because: "all rejected calls remain outside execution");
    }

    /// <summary>Proves that an observable effect does not substitute for an operation outcome.</summary>
    [Test, Description("Q9: an effect followed by death before outcome remains Unknown, with no replay")]
    public async Task Effect_without_published_outcome_remains_unknown() {
        // Arrange
        await session.CallAsync("submit", id: "uncertain", generation: "V1", contract: "write/v1", payload: "mixed", holdOutcome: true);
        await session.EventAsync("EffectWritten", "uncertain");
        // Act
        await session.CallAsync("crash");
        await session.EventAsync("Unavailable");
        var state = await session.CallAsync("query");
        // Assert
        Status(state, "uncertain").Should().Be("Unknown", because: "a side-effect artifact is not the backend's authoritative terminal outcome");
        session.Effects().Should().ContainSingle(because: "the effect happened but cannot authorize retry or inferred success");
    }

    /// <summary>Checks cancellation and capacity reclamation on the future-generation queue.</summary>
    [Test, Description("Q10: cancelled V2 request never runs and its slot is reusable before handover")]
    public async Task New_generation_cancel_and_duplicate_update_do_not_break_cutoff() {
        // Arrange
        await session.RestartAsync("--capacity", "1");
        await session.SubmitAsync("held", "V1", holdWork: true);
        await session.EventAsync("Started", "held");
        await session.CallAsync("update");
        await session.SubmitAsync("cancelled-v2", "V2");
        // Act
        var repeated = await session.CallAsync("update");
        await session.CallAsync("cancel", id: "cancelled-v2");
        var next = await session.SubmitAsync("next-v2", "V2");
        await session.CallAsync("release-work", id: "held");
        await session.EventAsync("Released", "next-v2");
        // Assert
        repeated.GetProperty("Code").GetString().Should().Be("update-unavailable", because: "one pending transition cannot be overwritten by another");
        next.GetProperty("Code").GetString().Should().Be("queued-volatile", because: "cancellation frees capacity while the old generation drains");
        session.Effects().Select(e => e.GetProperty("Id").GetString()).Should().Equal(["held", "next-v2"], because: "cancelled future work is not dispatched after readiness");
    }

    private static JsonElement Find(JsonElement reply, string id) => reply.GetProperty("Data").EnumerateArray().Single(e => e.GetProperty("Id").GetString() == id);
    private static string? Status(JsonElement reply, string id) => Find(reply, id).GetProperty("Status").GetString();
}

internal sealed class Session(string name) : IAsyncDisposable {
    private Process? process;
    private Task? reader;
    private Task<string>? errors;
    private readonly List<JsonElement> messages = [];
    private readonly object sync = new();
    private int request;
    private int attempt;
    private string directory = "";
    private string effects = "";
    internal JsonElement[] Events { get { lock (sync) return messages.Where(e => e.GetProperty("Type").GetString() == "event").ToArray(); } }
    internal async Task StartAsync(params string[] flags) {
        directory = Path.Combine(Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../artifacts")), name, (++attempt) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        effects = Path.Combine(directory, "effects-" + Guid.NewGuid().ToString("N") + ".jsonl");
        string dll = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../Probe/bin/Release/net10.0/Probe.dll"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { dll, "--effects", effects }.Concat(flags)) start.ArgumentList.Add(arg);
        // A real mutation arm executes the same suite with one safety rule disabled.
        if (Environment.GetEnvironmentVariable("QUEUE_PROBE_MUTATION") == "unsafe-fallback") start.ArgumentList.Add("--unsafe-fallback");
        process = Process.Start(start)!;
        await File.WriteAllTextAsync(Path.Combine(directory, "supervisor-pid.txt"), process.Id.ToString());
        errors = process.StandardError.ReadToEndAsync();
        reader = Task.Run(async () => {
            while (await process.StandardOutput.ReadLineAsync() is { } line) {
                var message = JsonDocument.Parse(line).RootElement.Clone();
                lock (sync) messages.Add(message);
                await File.AppendAllTextAsync(Path.Combine(directory, "transcript.jsonl"), line + Environment.NewLine);
            }
        });
        await EventAsync("Ready");
    }
    internal async Task RestartAsync(params string[] flags) { await DisposeAsync(); lock(sync) messages.Clear(); await StartAsync(flags); }
    internal Task<JsonElement> SubmitAsync(string id, string generation, bool holdWork = false, bool holdCleanup = false, int deadlineMs = 0) =>
        CallAsync("submit", id, generation, generation == "V1" ? "write/v1" : "write/v2", "mixed", holdWork, holdCleanup, deadlineMs);
    internal async Task<JsonElement> CallAsync(string kind, string? id = null, string? generation = null, string? contract = null,
        string? payload = null, bool holdWork = false, bool holdCleanup = false, int deadlineMs = 0, int budgetMs = 0, bool holdOutcome = false) {
        string key = (++request).ToString();
        await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { Kind = kind, Request = key, Id = id,
            Generation = generation, Contract = contract, Payload = payload, HoldWork = holdWork, HoldCleanup = holdCleanup,
            DeadlineMs = deadlineMs, BudgetMs = budgetMs, HoldOutcome = holdOutcome }));
        await process.StandardInput.FlushAsync();
        return await WaitAsync(e => e.GetProperty("Type").GetString() == "reply" && e.GetProperty("Request").GetString() == key);
    }
    internal Task<JsonElement> EventAsync(string kind, string? id = null, int occurrence = 1) => WaitAsync(e => IsEvent(e, kind, id), occurrence);
    private static bool IsEvent(JsonElement e, string kind, string? id) => e.GetProperty("Type").GetString() == "event"
        && e.GetProperty("Kind").GetString() == kind && (id is null || e.GetProperty("Data").GetProperty("Id").GetString() == id);
    private async Task<JsonElement> WaitAsync(Func<JsonElement, bool> predicate, int occurrence = 1) {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(12)) {
            lock (sync) { var found = messages.Where(predicate).ToArray(); if (found.Length >= occurrence) return found[occurrence - 1]; }
            // Polling observes explicit protocol handshakes; it does not manufacture the tested overlap.
            await Task.Delay(10);
        }
        throw new TimeoutException("Protocol event missing; see " + directory);
    }
    internal long Sequence(string kind, string? id = null) => Events.First(e => IsEvent(e, kind, id)).GetProperty("Sequence").GetInt64();
    internal bool HasEvent(string kind, JsonElement beforeReply) {
        lock (sync) return messages.TakeWhile(e => e.GetRawText() != beforeReply.GetRawText()).Any(e => IsEvent(e, kind, null));
    }
    internal JsonElement[] Effects() => !File.Exists(effects) ? [] : File.ReadAllLines(effects).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToArray();
    public async ValueTask DisposeAsync() {
        if (process is null) return;
        if (!process.HasExited) {
            await process.StandardInput.WriteLineAsync("{\"Kind\":\"shutdown\"}");
            await process.StandardInput.FlushAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        if (reader is not null) await reader;
        if (errors is not null) await File.WriteAllTextAsync(Path.Combine(directory, "stderr.txt"), await errors);
        var ownedPids = Events.Where(e => IsEvent(e, "Starting", null))
            .Select(e => e.GetProperty("Data").GetProperty("WorkerPid"))
            .Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).Distinct().ToArray();
        foreach (var pid in ownedPids) {
            bool alive;
            try { using var child = Process.GetProcessById(pid); alive = !child.HasExited; }
            catch (ArgumentException) { alive = false; }
            alive.Should().BeFalse(because: "every exclusively owned backend, including failed startups, must be gone after cleanup");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "cleanup.json"), JsonSerializer.Serialize(new {
            SupervisorPid = process.Id, SupervisorExited = process.HasExited,
            WorkerPids = ownedPids, AllWorkersExited = true
        }));
        process.Dispose(); process = null;
    }
}
