using System;
using System.Collections.Generic;

namespace Clio.Package;

#region Class: RemotePackageCreateRequest

/// <summary>
/// Describes a package to create in a Creatio environment.
/// </summary>
/// <param name="PackageName">
/// Requested package name. The environment's <c>SchemaNamePrefix</c> is prepended when the name does not
/// already start with it.
/// </param>
/// <param name="Description">Optional package description.</param>
/// <param name="Dependencies">Names of installed packages the new package depends on; may be empty.</param>
/// <param name="ApplicationCode">
/// Optional code of an installed application. When set, the package is created inside that application;
/// otherwise it is a standalone package.
/// </param>
public sealed record RemotePackageCreateRequest(
	string PackageName,
	string Description,
	IReadOnlyList<string> Dependencies,
	string ApplicationCode);

#endregion

#region Class: RemotePackageCreateResult

/// <summary>
/// Readback of a package that <see cref="IRemotePackageCreator.Create"/> created.
/// </summary>
/// <param name="PackageUId">Identifier of the created package.</param>
/// <param name="PackageName">Name the package was created with, including the prefix clio added.</param>
/// <param name="Maintainer">Maintainer the environment assigned, from its <c>Maintainer</c> system setting.</param>
/// <param name="Description">Stored package description.</param>
/// <param name="Dependencies">Direct dependencies the package declares after creation.</param>
/// <param name="InstallType">Stored <c>InstallType</c>; <c>0</c> means the package is editable.</param>
/// <param name="ApplicationCode">Application the package was created in, or <see langword="null"/>.</param>
/// <param name="IncompleteReason">
/// <see langword="null"/> when every requested step succeeded. Otherwise the package EXISTS in the
/// environment but a later step failed (dependencies not applied, or the readback failed), and this text
/// says which.
/// </param>
public sealed record RemotePackageCreateResult(
	Guid PackageUId,
	string PackageName,
	string Maintainer,
	string Description,
	IReadOnlyList<string> Dependencies,
	int? InstallType,
	string ApplicationCode,
	string IncompleteReason) {

	/// <summary>
	/// Gets whether the stored package can receive design-time changes (<c>InstallType</c> is <c>0</c>).
	/// </summary>
	public bool Editable => InstallType == 0;
}

#endregion

#region Class: PackageCreationOutcomeUnknownException

/// <summary>
/// Thrown when the create request failed in transport and the environment could not be asked afterwards
/// whether the package was stored, so it may or may not exist.
/// </summary>
public sealed class PackageCreationOutcomeUnknownException(string message, Exception innerException)
	: Exception(message, innerException);

#endregion

#region Interface: IRemotePackageCreator

/// <summary>
/// Creates a new package in a Creatio environment through the platform package services.
/// </summary>
public interface IRemotePackageCreator
{

	#region Methods: Public

	/// <summary>
	/// Creates the package described by <paramref name="request"/>, applies its dependencies and reads it
	/// back.
	/// </summary>
	/// <remarks>
	/// Every check that can refuse the request runs before anything is written: the name is prefixed and
	/// checked against the installed packages, every dependency is resolved, and the application is resolved.
	/// A refusal therefore changes nothing. The platform ignores the dependency list on creation, so the
	/// dependencies are saved by a second request; when that request or the readback fails, the package
	/// already exists and the result reports it through <see cref="RemotePackageCreateResult.IncompleteReason"/>
	/// instead of throwing.
	/// </remarks>
	/// <param name="request">Package to create.</param>
	/// <returns>The readback of the created package.</returns>
	/// <exception cref="ArgumentException">The package name is empty.</exception>
	/// <exception cref="InvalidOperationException">
	/// The request was refused and nothing was created: the name is taken, a dependency or the application
	/// does not exist, or the environment rejected the creation.
	/// </exception>
	/// <exception cref="PackageCreationOutcomeUnknownException">
	/// The create request failed in transport and whether the package exists could not be checked.
	/// </exception>
	RemotePackageCreateResult Create(RemotePackageCreateRequest request);

	#endregion

}

#endregion
