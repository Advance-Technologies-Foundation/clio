using System;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Administration;

/// <summary>Role hierarchy and membership operations.</summary>
[Verb("manage-role", HelpText = "Inspect and manage organizational, functional and manager roles.")]
public sealed class ManageRoleOptions : EnvironmentOptions {
	/// <summary>Requires the guarded server deletion endpoint only when removing functional associations.</summary>
	[RequiresPackage("cliogate", "2.0.0.50", Hint = "Run clio install-gate for this environment.")]
	public bool RequiresAdministrationGate => Action == "remove-functional";
	/// <summary>Operation to perform.</summary>
	[Option("action", Required = true, HelpText = "list | create | update | delete | ensure-manager | add-member | remove-member | memberships | members | functional-roles | add-functional | remove-functional")]
	public string Action { get; set; }
	/// <summary>Role identity.</summary>
	[Option("id", HelpText = "Role GUID; supply a new GUID for create.")]
	public Guid Id { get; set; }
	/// <summary>Exact name filter or new name.</summary>
	[Option("name", HelpText = "Exact name filter, or name for create/update.")]
	public string Name { get; set; }
	/// <summary>Role type.</summary>
	[Option("type", HelpText = "0 organization, 1 division, 2 manager (list only), 3 team, 6 functional.")]
	public int? Type { get; set; }
	/// <summary>Parent role identity.</summary>
	[Option("parent-id", HelpText = "Parent role GUID for create/update; organizational role for ensure-manager.")]
	public Guid? ParentId { get; set; }
	/// <summary>User identity for membership operations.</summary>
	[Option("user-id", HelpText = "User GUID for add-member/remove-member/memberships.")]
	public Guid UserId { get; set; }
	/// <summary>Functional role to associate with the organizational role.</summary>
	[Option("functional-id", HelpText = "Functional role GUID for add-functional/remove-functional.")]
	public Guid FunctionalId { get; set; }
	/// <summary>Reads effective rather than direct memberships.</summary>
	[Option("effective", HelpText = "Read effective memberships, including inheritance and delegation.")]
	public bool Effective { get; set; }
	/// <summary>Read offset.</summary>
	[Option("offset", Default = 0, HelpText = "List offset.")]
	public int Offset { get; set; }
	/// <summary>Read page size.</summary>
	[Option("limit", Default = 100, HelpText = "List page size, from 1 to 200.")]
	public int Limit { get; set; } = 100;
	/// <summary>Explicit CLI apply intent.</summary>
	[Option("confirm", HelpText = "Apply a mutation.")]
	public bool Confirm { get; set; }
}

/// <summary>Executes role operations through native administration services.</summary>
public sealed class ManageRoleCommand(IAdministrationService administration, ILogger logger) : Command<ManageRoleOptions> {
	/// <inheritdoc />
	public override int Execute(ManageRoleOptions options) {
		try {
			if (options.Action is not ("list" or "memberships" or "members" or "functional-roles") && !options.Confirm) {
				throw new ArgumentException("Mutations require --confirm.");
			}
			object result;
			switch (options.Action) {
				case "list": result = administration.ListUnits(options.Id == Guid.Empty ? null : options.Id, options.Name, options.Type, options.Offset, options.Limit, rolesOnly: true); break;
				case "create": result = administration.CreateRole(options.Id, options.Name, options.Type ?? -1, options.ParentId ?? Guid.Empty); break;
				case "update": result = administration.UpdateRole(options.Id, options.Name, options.ParentId); break;
				case "delete": administration.DeleteRole(options.Id); result = new { id = options.Id, deleted = true }; break;
				case "ensure-manager": result = administration.EnsureManager(options.ParentId ?? Guid.Empty); break;
				case "add-member": result = administration.SetMembership(options.UserId, options.Id, false); break;
				case "remove-member": result = administration.SetMembership(options.UserId, options.Id, true); break;
				case "memberships": result = administration.GetMemberships(options.UserId, options.Effective, options.Offset, options.Limit); break;
				case "members": result = administration.GetMembers(options.Id, options.Effective, options.Offset, options.Limit); break;
				case "functional-roles": result = administration.GetFunctionalRoles(options.Id, options.Offset, options.Limit); break;
				case "add-functional": result = administration.SetFunctionalRole(options.Id, options.FunctionalId, false); break;
				case "remove-functional": result = administration.SetFunctionalRole(options.Id, options.FunctionalId, true); break;
				default: throw new ArgumentException("Unknown role action. See manage-role help for supported actions.");
			}
			logger.WriteInfo(JsonSerializer.Serialize(result));
			if (result is JsonElement state && state.ValueKind == JsonValueKind.Object
				&& state.TryGetProperty("completed", out JsonElement completed) && !completed.GetBoolean()) {
				logger.WriteError("The association was removed but follow-up processing failed. Inspect the receipt and effective membership; use role-redistribute if license reconciliation is required.");
				return 1;
			}
			return 0;
		} catch (Exception ex) when (ex is ArgumentException or AdministrationStateException) {
			logger.WriteError(ex.Message);
			return 1;
		} catch (Exception) {
			logger.WriteError("The role operation failed. Check permissions and inspect the role before retrying; partial changes may exist.");
			return 1;
		}
	}
}
