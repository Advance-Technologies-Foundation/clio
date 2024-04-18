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
	void StartPackageHotfix(string packageName);

	/// <summary>
	/// Finishes hotfix state for package.
	/// </summary>
	/// <param name="packageName">Package name.</param>
	void FinishPackageHotfix(string packageName);

	#endregion
}