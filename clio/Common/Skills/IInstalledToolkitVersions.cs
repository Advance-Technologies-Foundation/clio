using System.Collections.Generic;

namespace Clio.Common.Skills;

/// <summary>Reads toolkit versions from local coding-agent installations without running agent CLIs.</summary>
public interface IInstalledToolkitVersions {
	/// <summary>Returns a version or an explicit unavailable status for each supported agent.</summary>
	IReadOnlyDictionary<string, string> Read();
}
