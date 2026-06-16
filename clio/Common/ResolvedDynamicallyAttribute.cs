using System;

namespace Clio.Common;

/// <summary>
///     Marks a type that is registered in dependency injection but resolved dynamically
///     (for example via reflection, the MCP server, or another assembly) rather than through
///     direct constructor injection or a <c>GetService</c>/<c>GetRequiredService</c> call.
/// </summary>
/// <remarks>
///     The CLIO005 analyzer treats a registered service as dead code when neither the service
///     type nor its implementation is consumed within the compilation. Because a single-compilation
///     analyzer cannot see cross-assembly or reflection-based resolution, apply this attribute to
///     such types to opt out of the diagnostic.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class ResolvedDynamicallyAttribute : Attribute{ }
