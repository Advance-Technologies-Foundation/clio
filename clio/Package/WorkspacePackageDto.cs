using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Package;

#region Class: WorkspacePackageDto

/// <summary>
/// Minimal clio-side mirror of the Creatio core <c>WorkspacePackageDto</c> contract used by
/// <c>PackageService.svc/GetPackageProperties</c> and <c>PackageService.svc/SavePackageProperties</c>.
/// </summary>
/// <remarks>
/// Only the fields clio needs to mutate (<see cref="UId"/>, <see cref="Name"/>, <see cref="Version"/> and
/// <see cref="DependsOnPackages"/>) are modelled explicitly. Every other field the server returns is
/// preserved verbatim through <see cref="AdditionalData"/> so the object can be round-tripped back into
/// <c>SavePackageProperties</c> without losing properties such as <c>description</c>, <c>installBehavior</c>,
/// <c>type</c> or the install scripts. The core repository overwrites those values from the posted DTO, so a
/// faithful round-trip is mandatory — sending a sparse object would wipe them.
/// </remarks>
public class WorkspacePackageDto
{

	#region Properties: Public

	/// <summary>
	/// Package unique identifier. The core dependency reconciliation matches dependencies by this value only.
	/// </summary>
	[JsonProperty("uId")]
	public Guid UId { get; set; }

	/// <summary>
	/// Package name.
	/// </summary>
	[JsonProperty("name")]
	public string Name { get; set; }

	/// <summary>
	/// Package version.
	/// </summary>
	[JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
	public string Version { get; set; }

	/// <summary>
	/// Packages this package depends on. Mutating this collection and saving the package adds or removes
	/// package dependencies.
	/// </summary>
	[JsonProperty("dependsOnPackages", NullValueHandling = NullValueHandling.Ignore)]
	public List<WorkspacePackageDto> DependsOnPackages { get; set; }

	/// <summary>
	/// Captures every other field returned by the server so the DTO round-trips losslessly.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JToken> AdditionalData { get; set; }

	#endregion

}

#endregion
