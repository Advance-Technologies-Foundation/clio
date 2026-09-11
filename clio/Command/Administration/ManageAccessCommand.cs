using System;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Administration;

/// <summary>Manage IP restrictions, delegation and system-operation permissions.</summary>
[Verb("manage-access", HelpText = "Manage IP restrictions, delegation and system-operation permissions.")]
public sealed class ManageAccessOptions : EnvironmentOptions {
	/// <summary>Priority changes require native rights-cache invalidation from the administration bridge.</summary>
	[RequiresPackage("cliogate", "2.0.0.52")]
	[RequiresCreatioVersion("10.1.585.0", AllowDevelopmentBuild = false)]
	public bool RequiresAdministrationGate => Action == "operation-position";

	/// <summary>Operation: ip-list, ip-create, ip-update, ip-delete, delegations, delegate, revoke-delegation, operations, operation-grants, grant-operation, deny-operation, revoke-operation, operation-position.</summary>
	[Option("action", Required = true, HelpText = "Operation: ip-list, ip-create, ip-update, ip-delete, delegations, delegate, revoke-delegation, operations, operation-grants, grant-operation, deny-operation, revoke-operation, operation-position.")]
	public string Action { get; set; }
	/// <summary>IP rule or operation grant record GUID.</summary>
	[Option("id", HelpText = "IP rule or operation grant record GUID.")]
	public Guid Id { get; set; }
	/// <summary>Target user or role GUID; grantee for delegation and operation permissions.</summary>
	[Option("unit-id", HelpText = "Target user or role GUID; grantee for delegation and operation permissions.")]
	public Guid UnitId { get; set; }
	/// <summary>User or role whose rights are delegated to the target user.</summary>
	[Option("grantor-id", HelpText = "User or role whose rights are delegated to the target user.")]
	public Guid GrantorId { get; set; }
	/// <summary>System operation GUID.</summary>
	[Option("operation-id", HelpText = "System operation GUID.")]
	public Guid OperationId { get; set; }
	/// <summary>Exact operation code filter.</summary>
	[Option("code", HelpText = "Exact operation code filter.")]
	public string Code { get; set; }
	/// <summary>Beginning canonical IPv4 address.</summary>
	[Option("begin-ip", HelpText = "Beginning canonical IPv4 address.")]
	public string BeginIp { get; set; }
	/// <summary>Ending canonical IPv4 address.</summary>
	[Option("end-ip", HelpText = "Ending canonical IPv4 address.")]
	public string EndIp { get; set; }
	/// <summary>Zero-based operation grant priority.</summary>
	[Option("position", HelpText = "Zero-based operation grant priority.")]
	public int? Position { get; set; }
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

/// <summary>Executes explicit native administration access operations.</summary>
public sealed class ManageAccessCommand(IAdministrationService administration, ILogger logger) : Command<ManageAccessOptions> {
	/// <inheritdoc />
	public override int Execute(ManageAccessOptions options) {
		try {
			if (options.Action is not ("ip-list" or "delegations" or "operations" or "operation-grants") && !options.Confirm) {
				throw new ArgumentException("Mutations require --confirm.");
			}
			object result;
			switch (options.Action) {
				case "ip-list": result = administration.GetIpRanges(options.UnitId, options.Offset, options.Limit); break;
				case "ip-create": case "ip-update":
					result = administration.SetIpRange(options.Id, options.UnitId, options.BeginIp, options.EndIp, options.Action == "ip-create"); break;
				case "ip-delete": administration.DeleteIpRange(options.Id, options.UnitId); result = new { id = options.Id, deleted = true }; break;
				case "delegations": result = administration.GetDelegations(options.UnitId, options.Offset, options.Limit); break;
				case "delegate": case "revoke-delegation":
					result = administration.SetDelegation(options.GrantorId, options.UnitId, options.Action == "revoke-delegation"); break;
				case "operations": result = administration.GetOperations(options.Code, options.Offset, options.Limit); break;
				case "operation-grants": result = administration.GetOperationGrants(options.OperationId, options.Offset, options.Limit); break;
				case "grant-operation": case "deny-operation": case "revoke-operation":
					result = administration.SetOperationGrant(options.OperationId, options.UnitId, options.Action == "grant-operation", options.Action == "revoke-operation"); break;
				case "operation-position": result = administration.SetOperationPosition(options.Id,
					options.Position ?? throw new ArgumentException("Supply an explicit position for operation-position.")); break;
				default: throw new ArgumentException("Unknown access action. See manage-access help.");
			}
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception ex) when (ex is ArgumentException or AdministrationStateException) {
			logger.WriteError(ex.Message);
			return 1;
		} catch (Exception) {
			logger.WriteError("The access operation failed. Check permissions and inspect the target before retrying; partial changes may exist.");
			return 1;
		}
	}
}
