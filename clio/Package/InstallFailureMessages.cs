using System.Collections.Generic;
using System.Linq;

namespace Clio.Package;

/// <summary>
/// The closing error of a command whose installation the installer reported as failed.
/// </summary>
/// <remarks>
/// <see cref="IPackageInstaller.Install"/> and <see cref="IApplicationInstaller.Install"/> answer
/// <see langword="false"/> after logging "Package installation failed: &lt;reason&gt;", so the closing line names
/// only what may not have been installed (see
/// <c>docs/knowledge/Package/package-installers-report-a-failed-installation-by-returning-false.md</c>).
/// </remarks>
internal static class InstallFailureMessages
{
	/// <summary>Most names a closing line lists before it counts the rest.</summary>
	internal const int MaxNamed = 10;

	/// <summary>
	/// <c>"&lt;subject&gt; may not have been installed: A, B and 3 more."</c>, or
	/// <c>"&lt;subject&gt; may not have been installed."</c> when there are no names. "May not": a failure part-way
	/// through a multi-package archive can leave some of its packages installed.
	/// </summary>
	/// <param name="subject">What failed, e.g. "Workspace packages".</param>
	/// <param name="names">The names of what was not installed.</param>
	internal static string NotInstalled(string subject, IEnumerable<string> names) {
		List<string> list = names?.ToList() ?? [];
		if (list.Count == 0) {
			return $"{subject} may not have been installed.";
		}
		string named = string.Join(", ", list.Take(MaxNamed));
		return list.Count > MaxNamed
			? $"{subject} may not have been installed: {named} and {list.Count - MaxNamed} more."
			: $"{subject} may not have been installed: {named}.";
	}
}
