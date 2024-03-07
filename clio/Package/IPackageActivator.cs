using System.Collections.Generic;
using Clio.Package.Responses;

namespace Clio.Package;

#region Interface: IPackageActivator

/// <summary>
/// Provides interface for package activation.
/// </summary>
public interface IPackageActivator {

    #region Methods: Internal

    /// <summary>
    /// Activate package by UId.
    /// </summary>
    /// <param name="packageName"></param>
    IEnumerable<PackageActivationResultDto> Activate(string packageName);

    #endregion
}

#endregion