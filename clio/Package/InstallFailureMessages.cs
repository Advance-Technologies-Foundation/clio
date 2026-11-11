using System.Collections.Generic;
using System.Linq;

namespace Clio.Package;

/// <summary>
/// The closing error of a command whose installation the installer reported as failed.
/// </summary>
/// <remarks>
/// <see cref="IPackageInstaller.Install"/> and <see cref="IApplicationInstaller.Install"/> answer
/// <see langword="false"/> after logging "Package installation failed: &lt;reason&gt;", so the closing line names
/// only what was not installed (see
/// <c>docs/knowledge/Package/package-installers-report-a-failed-installation-by-returning-false.md</c>).
/// </remarks>
internal static class InstallFailureMessages
{
	/// <summary>Most names a closing line lists before it counts the rest.</summary>
	internal const int MaxNamed = 10;

	/// <summary>
	/// <c>"&lt;subject&gt; were not installed: A, B and 3 more."</c>, or <c>"&lt;subject&gt; were not installed."</c> when
	/// there are no names.
	/// </summary>
	/// <param name="subject">What failed, e.g. "Workspace packages".</param>
	/// <param name="names">The names of what was not installed.</param>
	internal static string NotInstalled(string subject, IEnumerable<string> names) {
		List<string> list = names?.ToList() ?? [];
		if (list.Count == 0) {
			return $"{subject} were not installed.";
		}
		string named = string.Join(", ", list.Take(MaxNamed));
		return list.Count > MaxNamed
			? $"{subject} were not installed: {named} and {list.Count - MaxNamed} more."
			: $"{subject} were not installed: {named}.";
	}
}
