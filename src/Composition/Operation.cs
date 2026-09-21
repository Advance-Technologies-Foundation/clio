namespace Clio10.Composition;

/// <summary>Vendor operation identifiers; partners may use their own names.</summary>
public static class Operation {
    /// <summary>Requests an application restart.</summary>
    public const string Restart = "restart";
    /// <summary>Clears the configured Creatio Redis database.</summary>
    public const string FlushRedis = "flush-redis";
}
