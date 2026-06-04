using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="IManagedSkillsManifestStore"/> backed by <see cref="IFileSystem"/>.
/// </summary>
public sealed class ManagedSkillsManifestStore(IFileSystem fileSystem, IUserHomeProvider userHomeProvider)
	: IManagedSkillsManifestStore {
	private const string ManifestFileName = "managed-skills.json";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IFileSystem _fileSystem = fileSystem;
	private readonly IUserHomeProvider _userHomeProvider = userHomeProvider;

	/// <inheritdoc />
	public ManagedSkillsManifest Read() {
		string manifestPath = GetManifestPath();
		if (!_fileSystem.ExistsFile(manifestPath)) {
			return new ManagedSkillsManifest();
		}

		try {
			string content = _fileSystem.ReadAllText(manifestPath);
			return JsonSerializer.Deserialize<ManagedSkillsManifest>(content, JsonOptions)
				?? new ManagedSkillsManifest();
		}
		catch (JsonException) {
			// A corrupt manifest must not break a lifecycle command; treat as empty.
			return new ManagedSkillsManifest();
		}
	}

	/// <inheritdoc />
	public void Save(ManagedSkillsManifest manifest) {
		manifest.CheckArgumentNull(nameof(manifest));
		string manifestPath = GetManifestPath();
		if (manifest.Agents is null || manifest.Agents.Count == 0) {
			_fileSystem.DeleteFileIfExists(manifestPath);
			return;
		}

		_fileSystem.CreateDirectoryIfNotExists(_userHomeProvider.GetClioDir());
		_fileSystem.WriteAllTextToFile(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
	}

	private string GetManifestPath() =>
		_fileSystem.Combine(_userHomeProvider.GetClioDir(), ManifestFileName);
}
