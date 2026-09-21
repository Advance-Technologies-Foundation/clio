using System.Reflection;
namespace Clio10.Contracts;

/// <summary>Stable metadata name for a shared capability interface.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class CapabilityAttribute(string name) : Attribute {
    /// <summary>The capability identity.</summary>
    public string Name { get; } = name;
}

/// <summary>Access to session-owned capabilities. Borrowed capabilities must not be disposed.</summary>
public interface ICapabilityProvider {
    /// <summary>Resolves a declared capability or returns null.</summary>
    object? GetCapability(string name);
}

/// <summary>Typed access without hardcoding capability properties on every layer.</summary>
public static class CapabilityAccess {
    /// <summary>Checks that a named capability implements an interface bearing that identity.</summary>
    public static bool Implements(string name, object? instance) => instance is not null &&
        instance.GetType().GetInterfaces().Any(type => type.GetCustomAttribute<CapabilityAttribute>()?.Name == name);
    /// <summary>Resolves the shared interface or fails explicitly.</summary>
    public static T Get<T>(this ICapabilityProvider provider) where T : class {
        string name = typeof(T).GetCustomAttribute<CapabilityAttribute>()?.Name
            ?? throw new ArgumentException("Capability interfaces must declare their identity.");
        return provider.GetCapability(name) as T ?? throw new CapabilityUnavailableException(name);
    }
}

/// <summary>The selected provider cannot supply the requested shared interface.</summary>
public sealed class CapabilityUnavailableException(string name) : Exception("Required capability is unavailable: " + name);
