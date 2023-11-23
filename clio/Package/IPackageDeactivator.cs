namespace Clio.Package;

#region Interface: IPackageDeactivator

/// <summary>
/// Provides interface for package deactivation.
/// </summary>
public interface IPackageDeactivator {

    #region Methods: Internal

    /// <summary>
    /// Deactivate package by UId.
    /// </summary>
    /// <param name="packageName"></param>
    void Deactivate(string packageName);

    #endregion
}

#endregion