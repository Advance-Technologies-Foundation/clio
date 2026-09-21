namespace Partner.Workflow;

using Clio10.DetachedOperations;

/// <summary>
/// A third-party workflow. It references the contract and nothing else of the vendor's — no release
/// assembly, no host — and returns portable data so nothing of ours is retained by its caller.
/// </summary>
public sealed class ReportingWorkflow : IPartnerWorkflow {
    /// <inheritdoc />
    public string Name => "partner.reporting";

    /// <inheritdoc />
    public (string Partner, string RuntimeVersion, string Outcome) Compose(IDetachedRuntime runtime) {
        ArgumentNullException.ThrowIfNull(runtime);
        var steps = new List<string>();
        runtime.ReportProgressTo(steps.Add, 2);
        (string code, _) = runtime.TryRuntimeDefinedError();
        return (Name, runtime.Version, $"{steps.Count} steps, probe={code}");
    }
}
