using System;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Administration;

/// <summary>Inspect and manage user and role license assignments.</summary>
[Verb("manage-license", HelpText = "Inspect and manage user and role license assignments.")]
public sealed class ManageLicenseOptions : EnvironmentOptions {
	/// <summary>Requires the guarded scheduling endpoint only for explicit redistribution.</summary>
	[RequiresPackage("cliogate", "2.0.0.50", Hint = "Run clio install-gate for this environment.")]
	public bool RequiresAdministrationGate => Action == "role-redistribute";
	/// <summary>Allows redistribution to change manual assignments.</summary>
	[Option("include-manual", HelpText = "Allow role-redistribute to change manually assigned licenses.")]
	public bool IncludeManual { get; set; }
	/// <summary>user-list | user-assign | user-remove | role-list | role-assign | role-remove | role-redistribute</summary>
	[Option("action", Required = true, HelpText = "user-list | user-assign | user-remove | role-list | role-assign | role-remove | role-redistribute")]
	public string Action { get; set; }
	/// <summary>Target user GUID.</summary>
	[Option("user-id", HelpText = "Target user GUID.")]
	public Guid UserId { get; set; }
	/// <summary>Target role GUID.</summary>
	[Option("role-id", HelpText = "Target role GUID.")]
	public Guid RoleId { get; set; }
	/// <summary>License package GUID.</summary>
	[Option("package-id", HelpText = "License package GUID.")]
	public Guid PackageId { get; set; }
	/// <summary>Read offset.</summary>
	[Option("offset", HelpText = "Read offset.")]
	public int Offset { get; set; }
	/// <summary>Read page size from 1 to 200.</summary>
	[Option("limit", Default = 100, HelpText = "Read page size from 1 to 200.")]
	public int Limit { get; set; } = 100;
	/// <summary>Apply a mutation.</summary>
	[Option("confirm", HelpText = "Apply a mutation.")]
	public bool Confirm { get; set; }
}

/// <summary>Executes native license management and reports the verification boundary.</summary>
public sealed class ManageLicenseCommand(IAdministrationService administration, ILogger logger) : Command<ManageLicenseOptions> {
	/// <inheritdoc />
	public override int Execute(ManageLicenseOptions options) {
		try {
			if (options.Action is not ("user-list" or "role-list") && !options.Confirm) {
				throw new ArgumentException("Mutations require --confirm.");
			}
			object result = options.Action switch {
				"user-list" => administration.GetUserLicenses(options.UserId),
				"user-assign" => administration.SetUserLicense(options.UserId, options.PackageId, false),
				"user-remove" => administration.SetUserLicense(options.UserId, options.PackageId, true),
				"role-list" => administration.GetRoleLicenses(options.RoleId, options.Offset, options.Limit),
				"role-redistribute" => administration.RedistributeRoleLicenses(options.RoleId, options.IncludeManual),
				"role-assign" or "role-remove" => new {
					associations = administration.SetRoleLicense(options.RoleId, options.PackageId, options.Action == "role-remove"),
					redistributionRequired = true, userAssignmentsVerified = false
				},
				_ => throw new ArgumentException("Unknown license action. See manage-license help.")
			};
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception ex) when (ex is ArgumentException or AdministrationStateException) {
			logger.WriteError(ex.Message);
			return 1;
		} catch (Exception) {
			logger.WriteError("The license operation failed. Check permissions, license availability and target state before retrying; partial changes may exist.");
			return 1;
		}
	}
}
