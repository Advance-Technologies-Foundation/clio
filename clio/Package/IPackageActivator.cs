using System.Collections.Generic;

using Clio.Package.Responses;

namespace Clio.Package;


/// <summary>
/// Provides interface for package activation.
/// </summary>
public interface IPackageActivator
{

    /// <summary>
    /// Activate package by UId.
    /// </summary>
    /// <param name="packageName"></param>
    IEnumerable<PackageActivationResultDto> Activate(string packageName);
}
