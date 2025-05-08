namespace Clio.Package;

/// <summary>
///     Provides interface for package deactivation.
/// </summary>
public interface IPackageDeactivator
{
    /// <summary>
    ///     Deactivate package by name.
    /// </summary>
    /// <param name="packageName"></param>
    void Deactivate(string packageName);
}
