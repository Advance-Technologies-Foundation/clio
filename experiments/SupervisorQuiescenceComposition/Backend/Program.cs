// A minimal backend, driven entirely over stdin/stdout, standing in for a clio-shaped MCP backend.
// Protocol, one line per message:
//   in  "ping"                                      — readiness handshake; touches nothing, no effect
//   out "pong"                                       — reply, sent before anything else runs
//   in  "start <target> <opId> <workMs> <outcome>" — accept an operation that outlives its response
//   out "accepted <opId>"                          — sent immediately, mirrors a response-deadline reply
//   out "done <opId> <outcome>"                     — sent when the detached work reaches a terminal state
// The effect file is the only durable proof of work done: one line per completed operation, matching
// the shape of a real side effect (e.g. a created section) rather than the operation record itself.
//
// --fail-startup: exits immediately with a nonzero code before reading anything, simulating a backend
// that cannot start (bad config, missing dependency) -- for the failed-startup/fallback scenarios.
if (args.Contains("--fail-startup")) { Console.Error.WriteLine("simulated startup failure"); return 3; }
if (args.Length < 1) { Console.Error.WriteLine("usage: backend <effectFilePath> [--fail-startup]"); return 2; }
string effectPath = args[0];
object fileLock = new();

string? line;
while ((line = await Console.In.ReadLineAsync()) is not null) {
    if (line.Length == 0) continue;
    if (line == "quit") break;
    if (line == "ping") {
        // Deliberately no side effect: this is the readiness handshake, and readiness must not run
        // customer work or mutate shared state before the swap is committed (kirillkrylov's point).
        await Console.Out.WriteLineAsync("pong");
        await Console.Out.FlushAsync();
        continue;
    }
    string[] parts = line.Split(' ');
    if (parts.Length != 5 || parts[0] != "start") continue;
    string target = parts[1], opId = parts[2], outcome = parts[4];
    int workMs = int.Parse(parts[3]);

    await Console.Out.WriteLineAsync($"accepted {opId}");
    await Console.Out.FlushAsync();

    // Fire-and-forget: this is the detached work that outlives the response above, exactly the shape
    // that broke create-app-section — nothing in this process blocks on it, and no caller is waiting.
    _ = Task.Run(async () => {
        await Task.Delay(workMs);
        if (outcome == "succeed") {
            lock (fileLock) {
                File.AppendAllText(effectPath, $"{target}:{opId}:done-by-pid-{Environment.ProcessId}{Environment.NewLine}");
            }
        }
        try {
            await Console.Out.WriteLineAsync($"done {opId} {outcome}");
            await Console.Out.FlushAsync();
        }
        catch (IOException) {
            // The pipe can already be gone if the supervisor killed this process mid-write; that is
            // exactly the case scenario 1 (naive swap) measures, not an error in the backend.
        }
    });
}
return 0;
