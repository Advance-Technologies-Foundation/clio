using System.Reflection;

namespace Clio.Common;

/// <summary>
/// The version of the running clio build, as it is named in caller-facing diagnostics.
/// </summary>
/// <remarks>
/// One helper rather than a copy per call site: two diagnostics that disagree about which build is
/// running are worse than either alone, and a version skew is exactly what these messages are about.
/// </remarks>
internal static class ClioAssemblyVersion {

	/// <summary>The running assembly version, or <c>unknown</c> when it cannot be read.</summary>
	internal static string Current =>
		typeof(ClioAssemblyVersion).Assembly.GetName().Version?.ToString() ?? "unknown";
}
