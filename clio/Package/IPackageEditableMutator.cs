namespace Clio.Package;

/// <summary>
/// Provides methods for changing the editability of a package.
/// </summary>
public interface IPackageEditableMutator
{
	#region Methods: Public

	/// <summary>
	/// Starts hotfix state for package.
	/// </summary>
	/// <param name="packageName">Package name.</param>
	void SetPackageHotfix(string packageName, bool state);

	#endregion
}