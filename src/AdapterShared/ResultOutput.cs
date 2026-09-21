using System.Text.Json;
using Clio10.Contracts;
namespace Clio10.AdapterShared;

// Shared adapter source, not part of the workflow contract. Never format exception details here.
internal static class ResultOutput {
    internal sealed record Rendered(string Json, bool IsError);
    internal static Rendered Render(OperationResult result) {
        try { return new(JsonSerializer.Serialize(result, JsonArguments.Output), !result.Accepted); }
        catch (Exception) {
            // Preserve the known execution receipt; reporting failure does not undo side effects.
            var receipt = new {
                result.Accepted, result.Code, result.Response, result.PrimitiveVersion,
                AcceptedSteps = Steps(result.AcceptedSteps),
                ReportingError = "result-serialization-failed",
                RetryAdvice = "do-not-retry-automatically"
            };
            return new(JsonSerializer.Serialize(receipt, JsonArguments.Output), !result.Accepted);
        }
    }
    internal static OperationResult UnknownOutcome() => new(false, "execution-failed-outcome-unknown");
    internal static async ValueTask DisposeAsync(IAsyncDisposable resource) {
        try { await resource.DisposeAsync(); }
        catch (Exception error) {
            // Stderr is protocol-safe for stdio MCP. Never print provider exception messages.
            try { Console.Error.WriteLine("adapter-cleanup-failed (" + error.GetType().Name + ")"); }
            catch (Exception) { }
        }
    }
    private static string[]? Steps(IReadOnlyList<string>? steps) {
        try { return steps?.ToArray(); }
        catch (Exception) { return null; }
    }
}
