namespace Clio.Package.Responses;

using System.Runtime.Serialization;
using Clio.Common.Responses;

#region Class: PackagePropertiesResponse

/// <summary>
/// Response of <c>PackageService.svc/GetPackageProperties</c>; carries the full package descriptor.
/// </summary>
[DataContract]
public class PackagePropertiesResponse : BaseResponse
{

	#region Properties: Public

	/// <summary>
	/// The package properties, including its current dependency list.
	/// </summary>
	[DataMember(Name = "package")]
	public WorkspacePackageDto Package { get; set; }

	#endregion

}

#endregion

#region Class: SavePackagePropertiesResponse

/// <summary>
/// Response of <c>PackageService.svc/SavePackageProperties</c>.
/// </summary>
[DataContract]
public class SavePackagePropertiesResponse : BaseResponse
{

	#region Properties: Public

	/// <summary>
	/// Indicates that the saved change requires a configuration compilation.
	/// </summary>
	[DataMember(Name = "compilationRequired")]
	public bool CompilationRequired { get; set; }

	/// <summary>
	/// Validation errors reported by the package storage when the save is rejected.
	/// </summary>
	[DataMember(Name = "validationErrors")]
	public PackageValidationErrorDto[] ValidationErrors { get; set; }

	#endregion

}

#endregion

#region Class: PackageValidationErrorDto

/// <summary>
/// Single package-properties validation error returned by the server.
/// </summary>
[DataContract]
public class PackageValidationErrorDto
{

	#region Properties: Public

	/// <summary>Package the error refers to.</summary>
	[DataMember(Name = "packageName")]
	public string PackageName { get; set; }

	/// <summary>Configuration item the error refers to.</summary>
	[DataMember(Name = "itemName")]
	public string ItemName { get; set; }

	/// <summary>Type of the configuration item.</summary>
	[DataMember(Name = "itemType")]
	public string ItemType { get; set; }

	/// <summary>Manager that owns the configuration item.</summary>
	[DataMember(Name = "itemManagerName")]
	public string ItemManagerName { get; set; }

	/// <summary>Human-readable validation message.</summary>
	[DataMember(Name = "message")]
	public string Message { get; set; }

	#endregion

}

#endregion
