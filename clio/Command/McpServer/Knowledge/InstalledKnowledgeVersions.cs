using System.Collections.Generic;
using System.Linq;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>Reads locally recorded knowledge bundle versions without contacting publishers.</summary>
public interface IInstalledKnowledgeVersions {
	/// <summary>Returns configured source aliases and their installed versions, including disabled sources.</summary>
	/// <returns>Versions from readable local activation markers; absent or unreadable markers are omitted.</returns>
	IReadOnlyDictionary<string, string> Read();
}

internal sealed class InstalledKnowledgeVersions(
	ISettingsRepository settingsRepository,
	IKnowledgeSourceInstallationStore installationStore) : IInstalledKnowledgeVersions {
	public IReadOnlyDictionary<string, string> Read() {
		Dictionary<string, string> versions = new();
		foreach ((string alias, KnowledgeSourceConfiguration source) in
			settingsRepository.GetKnowledgeConfiguration().Sources.OrderBy(entry => entry.Key)) {
			if (source.Type == KnowledgeSourceType.Git) {
				continue;
			}
			KnowledgeSourceStartupState? state = installationStore.TryReadStartupState(alias);
			if (state is not null && state.Active.LibraryId == source.LibraryId) {
				versions.Add(alias, state.Active.LibraryVersion);
			}
		}
		return versions;
	}
}
