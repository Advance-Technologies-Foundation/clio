namespace Clio.Common.Skills;

/// <summary>
/// Reads and persists the global managed-skills manifest at <c>~/.clio/managed-skills.json</c>.
/// </summary>
public interface IManagedSkillsManifestStore {
	/// <summary>
	/// Reads the manifest, returning an empty manifest when the file is absent or unreadable.
	/// </summary>
	ManagedSkillsManifest Read();

	/// <summary>
	/// Persists the manifest. When it has no agent entries the file is deleted.
	/// </summary>
	/// <param name="manifest">Manifest to persist.</param>
	void Save(ManagedSkillsManifest manifest);
}
