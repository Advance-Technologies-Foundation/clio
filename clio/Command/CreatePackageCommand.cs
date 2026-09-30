using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

#region Class: CreatePackageOptions

/// <summary>
/// Command-line options for the <c>create-package</c> command.
/// </summary>
[Verb("create-package", HelpText = "Create a new package in a Creatio environment")]
public class CreatePackageOptions : RemoteCommandOptions
{

	#region Properties: Public

	/// <summary>
	/// Name of the package to create. The environment's <c>SchemaNamePrefix</c> is prepended when missing.
	/// </summary>
	[Option("package-name", Required = true,
		HelpText = "Name of the package to create; the environment's SchemaNamePrefix is prepended when missing")]
	public string PackageName { get; set; }

	/// <summary>
	/// Optional package description.
	/// </summary>
	[Option("description", Required = false, HelpText = "Package description")]
	public string Description { get; set; }

	/// <summary>
	/// Names of installed packages the new package depends on.
	/// </summary>
	[Option("dependencies", Required = false, Separator = ',',
		HelpText = "Installed packages the new package depends on. Example: --dependencies CrtBase,CrtUIv2")]
	public IEnumerable<string> Dependencies { get; set; }

	/// <summary>
	/// Optional code of an installed application to create the package in.
	/// </summary>
	[Option("application-code", Required = false,
		HelpText = "Code of an installed application to create the package in; omit for a standalone package")]
	public string ApplicationCode { get; set; }

	#endregion

}

#endregion

#region Class: CreatePackageResponse

/// <summary>
/// Structured result of the <c>create-package</c> command.
/// </summary>
public sealed record CreatePackageResponse
{

	/// <summary>Gets whether every requested step succeeded.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Gets whether the package exists in the environment after the call. <see langword="false"/> means the
	/// request was refused and nothing changed; <see langword="null"/> means the outcome is unknown because the
	/// create request failed in transport and the environment could not be asked afterwards.
	/// </summary>
	[JsonPropertyName("package-created")]
	public bool? PackageCreated { get; init; }

	/// <summary>Gets the failure detail; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Gets the created package identifier.</summary>
	[JsonPropertyName("package-uid")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageUId { get; init; }

	/// <summary>Gets the name the package was created with, including the prefix clio added.</summary>
	[JsonPropertyName("package-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageName { get; init; }

	/// <summary>Gets the maintainer the environment assigned.</summary>
	[JsonPropertyName("maintainer")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Maintainer { get; init; }

	/// <summary>Gets the stored package description.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Description { get; init; }

	/// <summary>Gets the direct dependencies the package declares.</summary>
	[JsonPropertyName("dependencies")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Dependencies { get; init; }

	/// <summary>Gets the stored install type; <c>0</c> is an editable package.</summary>
	[JsonPropertyName("install-type")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? InstallType { get; init; }

	/// <summary>Gets whether the package can receive design-time changes.</summary>
	[JsonPropertyName("editable")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Editable { get; init; }

	/// <summary>Gets the application the package was created in; omitted for a standalone package.</summary>
	[JsonPropertyName("application-code")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ApplicationCode { get; init; }

}

#endregion

#region Class: CreatePackageCommand

/// <summary>
/// Creates a new package in a Creatio environment, applies its dependencies and reports the stored package.
/// </summary>
public class CreatePackageCommand : RemoteCommand<CreatePackageOptions>
{

	#region Fields: Private

	private readonly IRemotePackageCreator _packageCreator;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatePackageCommand"/> class.
	/// </summary>
	/// <param name="packageCreator">Service that creates the package.</param>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	public CreatePackageCommand(IRemotePackageCreator packageCreator, EnvironmentSettings environmentSettings)
		: base(environmentSettings) {
		_packageCreator = packageCreator;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Creates the package described by <paramref name="options"/>.
	/// </summary>
	/// <param name="options">Package to create.</param>
	/// <returns>
	/// The structured result: the readback on success; the refusal with <c>package-created=false</c>; or,
	/// when the package was created but a later step failed, the readback with <c>success=false</c>.
	/// </returns>
	public virtual CreatePackageResponse CreatePackage(CreatePackageOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		RemotePackageCreateResult result;
		try {
			result = _packageCreator.Create(new RemotePackageCreateRequest(options.PackageName, options.Description,
				[.. options.Dependencies ?? []], options.ApplicationCode));
		} catch (PackageCreationOutcomeUnknownException exception) {
			return new CreatePackageResponse { Success = false, PackageCreated = null, Error = exception.Message };
		} catch (Exception exception) {
			return new CreatePackageResponse { Success = false, PackageCreated = false, Error = exception.Message };
		}
		return new CreatePackageResponse {
			Success = result.IncompleteReason is null,
			PackageCreated = true,
			Error = result.IncompleteReason,
			PackageUId = result.PackageUId.ToString(),
			PackageName = result.PackageName,
			Maintainer = result.Maintainer,
			Description = result.Description,
			Dependencies = result.Dependencies,
			InstallType = result.InstallType,
			Editable = result.InstallType is null ? null : result.Editable,
			ApplicationCode = string.IsNullOrWhiteSpace(result.ApplicationCode) ? null : result.ApplicationCode
		};
	}

	/// <summary>
	/// Executes the create-package command.
	/// </summary>
	/// <param name="options">Parsed command options.</param>
	/// <returns>0 when the package was created with every requested dependency; otherwise 1.</returns>
	public override int Execute(CreatePackageOptions options) {
		CreatePackageResponse response = CreatePackage(options);
		if (response.PackageCreated == true) {
			Logger.WriteInfo($"Package \"{response.PackageName}\" created (UId {response.PackageUId}, "
				+ $"maintainer {response.Maintainer ?? "unknown"}, editable {response.Editable?.ToString() ?? "unknown"}).");
			if (response.Dependencies is { Count: > 0 }) {
				Logger.WriteInfo($"Depends on: {string.Join(", ", response.Dependencies)}");
			}
		}
		if (!response.Success) {
			Logger.WriteError(response.Error);
			return 1;
		}
		Logger.WriteInfo("Done");
		return 0;
	}

	#endregion

}

#endregion
