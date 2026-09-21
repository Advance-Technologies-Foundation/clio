using Clio10.Contracts;
namespace Clio10.Core;

/// <summary>Core configuration. A null bundle directory selects the shipped default bundle.</summary>
public sealed record CoreOptions(IReadOnlyDictionary<string, ClioEnvironment> Environments,
    string? BundleDirectory = null, Version? ExactVersion = null, string? SettingsPath = null, BundleUpdateOptions? Updates = null, bool RuntimeComposition = false) {
    /// <summary>Explicit typed contracts shared with static compositions. Complete runtimes always keep these private.</summary>
    public IReadOnlyList<System.Reflection.Assembly> SharedCapabilityAssemblies { get; init; } = [];
}
