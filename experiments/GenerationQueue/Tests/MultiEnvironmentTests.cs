using FluentAssertions;
using NUnit.Framework;

namespace GenerationQueue.Tests;

/// <summary>Small two-target extension of the shared supervisor/backend fixture.</summary>
public sealed partial class QueueTests {
    /// <summary>Proves two environments overlap in one backend and both retain ownership across cutoff.</summary>
    [Test, Description("Q11: two targets overlap, drain their old queues and cleanup, then share one replacement")]
    public async Task Two_targets_overlap_and_both_drain_before_shared_replacement() {
        // Arrange: neither held operation can finish until this client explicitly releases it.
        await session.SubmitAsync("a-running", "V1", holdWork: true, holdCleanup: true, target: "A");
        await session.EventAsync("Started", "a-running");
        var receipt = await session.SubmitAsync("b-running", "V1", holdWork: true, target: "B");
        var overlap = await session.CallAsync("query");
        // Assert overlap immediately so the serialization mutation fails an oracle, not a timeout.
        Find(overlap, "b-running").GetProperty("Owns").GetBoolean().Should().BeTrue(because: "B must be dispatched while A is explicitly held; global serialization invalidates the concurrency claim");
        await session.EventAsync("Started", "b-running");
        await session.SubmitAsync("a-queued", "V1", target: "A");
        await session.SubmitAsync("b-queued", "V1", holdCleanup: true, target: "B");

        // Act: cutoff is for this shared Clio backend, not for either Creatio environment.
        await session.CallAsync("update", budgetMs: 10000);
        await session.SubmitAsync("a-new", "V2", target: "A");
        await session.SubmitAsync("b-new", "V2", target: "B");
        var stale = await session.SubmitAsync("stale-b", "V1", target: "B");
        await session.CallAsync("release-work", id: "b-running");
        await session.EventAsync("Outcome", "b-queued");
        var bProgress = await session.CallAsync("query");
        await session.CallAsync("release-work", id: "a-running");
        await session.EventAsync("Outcome", "a-running");
        var aCleanup = await session.CallAsync("query");
        await session.CallAsync("release-cleanup", id: "a-running");
        await session.EventAsync("Released", "a-queued");
        var bCleanup = await session.CallAsync("query");
        // Assert retirement boundary while only B still owns cleanup, before releasing it.
        session.HasEvent("OldDrained", bCleanup).Should().BeFalse(because: "B cleanup must block shared retirement even after A is idle");
        await session.CallAsync("release-cleanup", id: "b-queued");
        await session.EventAsync("Released", "a-new");
        await session.EventAsync("Released", "b-new");
        var final = await session.CallAsync("query");

        // Assert: observable overlap, one process per generation, target FIFO and global drain.
        receipt.GetProperty("Code").GetString().Should().Be("accepted-until-supervisor-exit", because: "receipt must state its session lifetime");
        Status(bProgress, "a-running").Should().Be("Running", because: "B must finish actual effects while A still waits");
        Status(bProgress, "b-queued").Should().Be("Succeeded", because: "B's backlog progresses independently of held A");
        Status(aCleanup, "a-queued").Should().Be("Queued", because: "A's next operation cannot overtake A's cleanup");
        Find(bCleanup, "b-queued").GetProperty("Owns").GetBoolean().Should().BeTrue(because: "success must not end B's cleanup ownership");
        stale.GetProperty("Data").GetProperty("Accepting").GetString().Should().Be("V2", because: "stale callers need the current generation in the rejection");
        stale.GetProperty("Data").GetProperty("Contract").GetString().Should().Be("write/v2", because: "stale callers need the current contract without guessing a discovery action");
        var effects = session.Effects();
        foreach (string target in new[] { "A", "B" }) {
            string prefix = target.ToLowerInvariant();
            var targetEffects = effects.Where(e => e.GetProperty("Target").GetString() == target).ToArray();
            targetEffects.Select(e => e.GetProperty("Id").GetString()).Should().Equal([prefix + "-running", prefix + "-queued", prefix + "-new"], because: "each target must retain FIFO and its own exact effects");
            targetEffects.Select(e => e.GetProperty("Generation").GetString()).Should().Equal(["V1", "V1", "V2"], because: "old and new work must never be reinterpreted across cutoff");
            targetEffects.Select(e => e.GetProperty("Configuration").GetString()).Should().Equal(["cfg-1", "cfg-1", "cfg-2"], because: "configuration remains paired with its accepted generation");
            targetEffects.Select(e => e.GetProperty("Value").GetString()).Should().Equal(["mixed", "mixed", "MIXED"], because: "generation routing must preserve independently observed behavior");
            foreach (var effect in targetEffects) {
                var record = Find(final, effect.GetProperty("Id").GetString()!);
                record.GetProperty("Target").GetString().Should().Be(target, because: "the retained record and external target must agree");
                record.GetProperty("Status").GetString().Should().Be("Succeeded", because: "both generations' published results survive replacement");
            }
        }
        effects.Where(e => e.GetProperty("Generation").GetString() == "V1").Select(e => e.GetProperty("Pid").GetInt32()).Distinct().Should().ContainSingle(because: "both targets share one V1 process");
        effects.Where(e => e.GetProperty("Generation").GetString() == "V2").Select(e => e.GetProperty("Pid").GetInt32()).Distinct().Should().ContainSingle(because: "both targets share one V2 process");
        effects.Select(e => e.GetProperty("Pid").GetInt32()).Distinct().Should().HaveCount(2, because: "one backend is replaced once, without per-target hosts");
        session.Sequence("Started", "b-running").Should().BeLessThan(session.Sequence("Released", "a-running"), because: "the overlap is established by handshakes rather than duration");
        session.Sequence("Released", "b-queued").Should().BeLessThan(session.Sequence("OldDrained"), because: "the last target's cleanup determines shared retirement");
        session.Sequence("OldDrained").Should().BeLessThan(session.Sequence("Started", "a-new"), because: "new A work must follow global drain");
        session.Sequence("OldDrained").Should().BeLessThan(session.Sequence("Started", "b-new"), because: "new B work must follow global drain");
        session.Events.Select(e => e.GetProperty("SupervisorPid").GetInt32()).Distinct().Should().ContainSingle(because: "one supervisor and connection serve both environments through replacement");
    }

    /// <summary>Shows the intentional shared-update delay when only one environment will not drain.</summary>
    [Test, Description("Q12: held B defers shared update, resolves both V2 queues and permits V1 A to continue")]
    public async Task Held_target_defers_shared_update_without_stopping_other_target() {
        // Arrange
        await session.SubmitAsync("held-b", "V1", holdWork: true, target: "B");
        await session.EventAsync("Started", "held-b");
        await session.SubmitAsync("a-before", "V1", target: "A");
        await session.EventAsync("Released", "a-before");
        // Act
        await session.CallAsync("update", budgetMs: 800);
        await session.SubmitAsync("a-v2", "V2", target: "A");
        await session.SubmitAsync("b-v2", "V2", target: "B");
        await session.EventAsync("UpdateDeferred");
        await session.SubmitAsync("a-after", "V1", target: "A");
        await session.EventAsync("Released", "a-after");
        var state = await session.CallAsync("query");
        await session.CallAsync("release-work", id: "held-b");
        await session.EventAsync("Released", "held-b");
        // Assert
        Status(state, "held-b").Should().Be("Running", because: "update timeout must neither kill nor interrupt B");
        Status(state, "a-after").Should().Be("Succeeded", because: "retaining busy B must not serialize unrelated A work");
        foreach (var id in new[] { "a-v2", "b-v2" }) {
            Status(state, id).Should().Be("NotStarted", because: "the deferred shared update cannot run either target's V2 work");
            Find(state, id).GetProperty("Generation").GetString().Should().Be("V2", because: "deferral must preserve caller intent for both environments");
        }
        session.Effects().Select(e => e.GetProperty("Id").GetString()).Should().Equal(["a-before", "a-after", "held-b"], because: "A makes progress while B waits and neither V2 entry executes");
        session.Events.Count(e => e.GetProperty("Kind").GetString() == "Starting").Should().Be(1, because: "deferral retains the original backend instead of replacing it");
    }

    /// <summary>Checks that one shared executor failure reconciles both targets independently.</summary>
    [Test, Description("Q13: shared crash preserves A success, B uncertainty and untouched queues for both targets")]
    public async Task Shared_crash_preserves_each_targets_distinct_outcome() {
        // Arrange
        await session.SubmitAsync("known-a", "V1", holdCleanup: true, target: "A");
        await session.EventAsync("Outcome", "known-a");
        await session.CallAsync("submit", id: "unknown-b", generation: "V1", contract: "write/v1", payload: "mixed", holdOutcome: true, target: "B");
        await session.EventAsync("EffectWritten", "unknown-b");
        await session.SubmitAsync("queued-a", "V1", target: "A");
        await session.SubmitAsync("queued-b", "V1", target: "B");
        // Act
        await session.CallAsync("crash");
        await session.EventAsync("Unavailable");
        var state = await session.CallAsync("query");
        // Assert
        Status(state, "known-a").Should().Be("Succeeded", because: "shared process loss cannot erase A's published outcome");
        Status(state, "unknown-b").Should().Be("Unknown", because: "B's effect without outcome must remain uncertain");
        Status(state, "queued-a").Should().Be("NotStarted", because: "A backlog was never dispatched");
        Status(state, "queued-b").Should().Be("NotStarted", because: "B backlog was never dispatched");
        state.GetProperty("Data").EnumerateArray().Should().OnlyContain(e => !e.GetProperty("Owns").GetBoolean(), because: "no entry can retain local ownership of the dead worker");
        session.Effects().Select(e => e.GetProperty("Target").GetString()).Should().Equal(["A", "B"], because: "each target performed one distinct effect and neither was replayed");
    }
}
