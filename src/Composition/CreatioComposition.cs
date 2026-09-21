using Clio10.Contracts;
using Clio10.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Clio10.Composition;

/// <summary>Dispatches metadata-only registrations into one scoped, Core-owned root execution.</summary>
public sealed class CreatioComposition : IClioComposition {
    private readonly ICompositionHost _core;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CreatioComposition> _logger;
    private readonly IReadOnlyDictionary<string, WorkflowRegistration> _workflows;
    private readonly bool _duplicates;
    /// <summary>Builds discovery from data without constructing workflow implementations.</summary>
    public CreatioComposition(ICompositionHost core, IServiceScopeFactory scopes, ILogger<CreatioComposition> logger,
        IEnumerable<WorkflowRegistration> workflows) {
        _core = core; _scopes = scopes; _logger = logger;
        var groups = workflows.GroupBy(x => x.Descriptor.Name, StringComparer.Ordinal).ToArray();
        _duplicates = groups.Any(x => x.Count() > 1);
        _workflows = groups.ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    }
    /// <inheritdoc />
    public IReadOnlyCollection<OperationDescriptor> Operations {
        get {
            if (_duplicates) throw new WorkflowContractException("duplicate-operation");
            return _workflows.Values.Select(x => x.Descriptor).ToArray();
        }
    }
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(CompositionRequest request, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_duplicates) return new(false, "duplicate-operation");
        if (!_workflows.TryGetValue(request.Operation, out var registration)) return new(false, "unsupported-operation");
        IReadOnlyDictionary<string, object?> arguments;
        try { arguments = ArgumentValues.Snapshot(request.Arguments); }
        catch (ArgumentException) { return new(false, "invalid-arguments"); }
        if (!ArgumentValues.IsValid(arguments, registration.Descriptor.Arguments)) return new(false, "invalid-arguments");
        try {
            return await _core.RunAsync(request.EnvironmentName, registration.Requirement, async (context, token) => {
                var scope = _scopes.CreateAsyncScope();
                try { return await InvokeAsync(request.Operation, scope.ServiceProvider, context, progress, [], arguments, registration.Requirement.Capabilities ?? [], token); }
                finally {
                    try { await scope.DisposeAsync(); }
                    catch (Exception error) { LogType("workflow-scope-cleanup-failed", error); }
                }
            }, cancellationToken);
        }
        catch (CoreResolutionException error) { return new(false, error.Code); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { LogType("core-failure", error); return new(false, "unexpected-failure"); }
    }
    private async Task<OperationResult> InvokeAsync(string name, IServiceProvider services, IOperationContext operation,
        IProgress<OperationProgress>? progress, IReadOnlyList<string> ancestors,
        IReadOnlyDictionary<string, object?> arguments, IReadOnlyCollection<string> rootCapabilities, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_workflows.TryGetValue(name, out var registration)) return new(false, "unsupported-operation");
        if (ancestors.Contains(name)) return new(false, "recursive-workflow");
        if (!registration.Requirement.Matches(operation.PrimitiveVersion, operation.Capabilities) ||
            !(registration.Requirement.Capabilities ?? []).All(rootCapabilities.Contains)) return new(false, "incompatible-nested-workflow");
        if (!ArgumentValues.IsValid(arguments, registration.Descriptor.Arguments)) return new(false, "invalid-arguments");
        IClioWorkflow workflow;
        try { workflow = services.GetRequiredKeyedService<IClioWorkflow>(name); }
        catch (Exception error) { LogType("workflow-unavailable", error); return new(false, "workflow-unavailable"); }
        var context = new Context(this, services, operation, progress, [.. ancestors, name], arguments, rootCapabilities, registration.Requirement.Capabilities ?? []);
        OperationResult result;
        OperationCanceledException? cancelled = null;
        try { result = await workflow.ExecuteAsync(context, cancellationToken); }
        catch (CoreResolutionException error) { result = new(false, error.Code); }
        catch (WorkflowContractException error) { result = new(false, error.Code); }
        catch (CapabilityUnavailableException) { result = new(false, "capability-unavailable"); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested) { cancelled = error; result = new(false, "cancelled"); }
        catch (Exception error) { LogType("unexpected-failure", error); result = new(false, "unexpected-failure"); }
        bool unfinished = await context.CloseAsync();
        if (cancelled is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancelled).Throw();
        return unfinished ? new(false, "unawaited-child-outcome-unknown", PrimitiveVersion: operation.PrimitiveVersion.ToString()) : result;
    }
    private void LogType(string code, Exception error) {
        try { _logger.LogWarning("{Code} ({ExceptionType}).", code, error.GetType().FullName); }
        catch (Exception) { /* Diagnostics must not replace the result. */ }
    }
    private sealed class Context(CreatioComposition owner, IServiceProvider services, IOperationContext operation,
        IProgress<OperationProgress>? progress, IReadOnlyList<string> ancestors,
        IReadOnlyDictionary<string, object?> arguments, IReadOnlyCollection<string> rootCapabilities, IReadOnlyCollection<string> capabilities) : IWorkflowContext {
        private readonly object _sync = new();
        private Task<OperationResult>? _child;
        private bool _closed;
        public IReadOnlyDictionary<string, object?> Arguments => arguments;
        public ClioEnvironment Environment => operation.Environment;
        public Version PrimitiveVersion => operation.PrimitiveVersion;
        public object? GetCapability(string name) {
            lock (_sync) {
                if (_closed) throw new WorkflowContractException("workflow-context-closed");
                if (_child is { IsCompleted: false }) throw new WorkflowContractException("concurrent-child-not-supported");
                if (!capabilities.Contains(name)) throw new WorkflowContractException("capability-undeclared");
                return operation.GetCapability(name);
            }
        }
        public void Report(string stage) => progress?.Report(new(stage));
        public Task<OperationResult> InvokeAsync(string name, CancellationToken cancellationToken, IReadOnlyDictionary<string, object?>? arguments = null) {
            TaskCompletionSource<OperationResult> completion;
            lock (_sync) {
                if (_closed) return Task.FromResult(new OperationResult(false, "workflow-context-closed"));
                if (_child is { IsCompleted: false }) return Task.FromResult(new OperationResult(false, "concurrent-child-not-supported"));
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _child = completion.Task;
            }
            _ = CompleteAsync(completion, name, cancellationToken, arguments);
            return completion.Task;
        }
        private async Task CompleteAsync(TaskCompletionSource<OperationResult> completion, string name, CancellationToken cancellationToken,
            IReadOnlyDictionary<string, object?>? arguments) {
            try {
                IReadOnlyDictionary<string, object?> input;
                try { input = ArgumentValues.Snapshot(arguments); }
                catch (ArgumentException) { completion.SetResult(new(false, "invalid-arguments")); return; }
                completion.SetResult(await owner.InvokeAsync(name, services, operation, progress, ancestors, input, rootCapabilities, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { completion.SetCanceled(cancellationToken); }
            catch (Exception) { completion.SetResult(new(false, "unexpected-failure")); }
        }
        public async Task<bool> CloseAsync() {
            Task<OperationResult>? child;
            bool unfinished;
            lock (_sync) { _closed = true; child = _child; unfinished = child is { IsCompleted: false }; }
            if (child is not null) {
                try { await child; }
                catch (Exception) { /* Observe completion; the caller's cancellation or failure remains authoritative. */ }
            }
            return unfinished;
        }
    }
}
