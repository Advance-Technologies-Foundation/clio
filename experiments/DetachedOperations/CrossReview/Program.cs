using System.Reflection;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

// Reviewer-only counterexamples; no changes to Alex's source. Reflection deliberately pauses the
// evidence writer at its existing lock so the terminal-publication ordering is deterministic.
string work = Directory.CreateTempSubdirectory("clio-e3-review-").FullName;
var ledger = new OperationLedger(Path.Combine(work, "overlap.jsonl"));
bool targetGrantedUnderGlobal;
using (var global = ledger.TryEnterSwapWindow()) {
    if (global is null) throw new InvalidOperationException("Control: fresh ledger must be idle.");
    using var target = ledger.TryEnterSwapWindow("envA");
    targetGrantedUnderGlobal = target is not null;
}
var completionLedger = new OperationLedger(Path.Combine(work, "completion.jsonl"));
using var lease = completionLedger.Begin("envA", "10.0.0.0", new object());
object fileLock = typeof(OperationLedger).GetField("_fileLock", BindingFlags.NonPublic | BindingFlags.Instance)!
    .GetValue(completionLedger)!;
bool windowBeforeCompletionPersisted;
bool writerBlocked;
int linesWhileBlocked;
Task writer;
Monitor.Enter(fileLock);
try {
    writer = Task.Run(() => lease.Complete(OperationState.Succeeded));
    if (!SpinWait.SpinUntil(() => completionLedger.Query(lease.Id).State == OperationState.Succeeded,
        TimeSpan.FromSeconds(10))) throw new TimeoutException("Terminal publication was not observed.");
    writerBlocked = !writer.IsCompleted;
    linesWhileBlocked = File.ReadAllLines(Path.Combine(work, "completion.jsonl")).Length;
    using var window = completionLedger.TryEnterSwapWindow();
    windowBeforeCompletionPersisted = window is not null;
}
finally { Monitor.Exit(fileLock); }
await writer;
Console.WriteLine(JsonSerializer.Serialize(new {
    sourceCommit = "e3138962cec2aad53918e791c1fc7d219d38a43d",
    os = Environment.OSVersion.VersionString, framework = Environment.Version.ToString(),
    targetGrantedUnderGlobal,
    windowBeforeCompletionPersisted, writerBlocked, evidenceLinesWhileBlocked = linesWhileBlocked,
    evidenceLinesAfterCompletion = File.ReadAllLines(Path.Combine(work, "completion.jsonl")).Length,
    interpretation = "Exit 0 means both defects reproduced, NOT that the admission barrier is safe. No restart/kill is performed."
}, new JsonSerializerOptions { WriteIndented = true }));
return targetGrantedUnderGlobal && windowBeforeCompletionPersisted && writerBlocked && linesWhileBlocked == 1 ? 0 : 1;
