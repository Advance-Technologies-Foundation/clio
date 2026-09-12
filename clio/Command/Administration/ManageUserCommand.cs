using System;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Administration;

/// <summary>Explicit user lifecycle operations; secrets are referenced, never command arguments.</summary>
[Verb("manage-user", HelpText = "Inspect and manage Creatio accounts, passwords and login lockout.")]
public sealed class ManageUserOptions : EnvironmentOptions {
	/// <summary>Only verified native implementations may receive password payloads.</summary>
	[RequiresCreatioVersion("10.1.585.0", AllowDevelopmentBuild = false,
		Hint = "Password writes require the verified native logging behavior; older implementations may log plaintext passwords.")]
	public bool RequiresSafePasswordLogging => Action is "create" or "password";
	/// <summary>Operation to perform.</summary>
	[Option("action", Required = true, HelpText = "list | create | update | delete | lock-status | unlock | password")]
	public string Action { get; set; }
	/// <summary>Exact account identity.</summary>
	[Option("id", HelpText = "User GUID; required except for list. Supply a new GUID for create.")]
	public Guid Id { get; set; }
	/// <summary>Exact login filter or new login.</summary>
	[Option("user-login", HelpText = "Exact login filter for list, or login for create/update.")]
	public string UserLogin { get; set; }
	/// <summary>Existing contact identity.</summary>
	[Option("contact-id", HelpText = "Existing contact GUID for create or update.")]
	public Guid? ContactId { get; set; }
	/// <summary>Explicit activation state.</summary>
	[Option("active", HelpText = "Explicit true/false for update; activation can also clear second-factor lockout.")]
	public bool? Active { get; set; }
	/// <summary>Creates an external account.</summary>
	[Option("external", HelpText = "Create an external-user account.")]
	public bool External { get; set; }
	/// <summary>Name of the process environment variable containing the new password.</summary>
	[Option("password-env", HelpText = "Name of a populated CLIO_ADMIN_PASSWORD_<SUFFIX> variable; suffix uses uppercase letters, digits or underscores. Never pass the password itself.")]
	public string PasswordEnvironmentVariable { get; set; }
	/// <summary>Requires a password change on next login.</summary>
	[Option("force-change-password", Default = true, HelpText = "Require password change on next login; applies to create/password.")]
	public bool ForceChangePassword { get; set; } = true;
	/// <summary>Read offset.</summary>
	[Option("offset", Default = 0, HelpText = "List offset.")]
	public int Offset { get; set; }
	/// <summary>Read page size.</summary>
	[Option("limit", Default = 100, HelpText = "List page size, from 1 to 200.")]
	public int Limit { get; set; } = 100;
	/// <summary>Explicit CLI apply intent.</summary>
	[Option("confirm", HelpText = "Apply a mutation without an interactive prompt.")]
	public bool Confirm { get; set; }
}

/// <summary>Executes validated account operations with safe structured output.</summary>
public sealed class ManageUserCommand(IAdministrationService administration, ILogger logger)
	: Command<ManageUserOptions> {
	/// <inheritdoc />
	public override int Execute(ManageUserOptions options) {
		try {
			if (options.Action is not ("list" or "lock-status") && !options.Confirm) {
				throw new ArgumentException("Mutations require --confirm.");
			}
			object result;
			switch (options.Action) {
				case "list":
					result = administration.ListUnits(options.Id == Guid.Empty ? null : options.Id, options.UserLogin, null, options.Offset, options.Limit, rolesOnly: false);
					break;
				case "create":
					result = administration.CreateUser(options.Id, options.UserLogin, options.ContactId ?? Guid.Empty,
						options.External, ReadPassword(options), options.ForceChangePassword);
					break;
				case "update": result = administration.UpdateUser(options.Id, options.UserLogin, options.ContactId, options.Active); break;
				case "delete": administration.DeleteUser(options.Id); result = new { id = options.Id, deleted = true }; break;
				case "lock-status": result = new { id = options.Id, blocked = administration.IsUserBlocked(options.Id) }; break;
				case "unlock": result = administration.UnlockUser(options.Id); break;
				case "password":
					administration.ChangePassword(options.Id, ReadPassword(options), options.ForceChangePassword);
					result = new { id = options.Id, passwordAccepted = true, authenticationVerified = false, options.ForceChangePassword };
					break;
				default: throw new ArgumentException("Unknown user action. Use list, create, update, delete, lock-status, unlock or password.");
			}
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception ex) when (ex is ArgumentException or AdministrationStateException) {
			logger.WriteError(ex.Message);
			return 1;
		} catch (Exception) {
			// Transport and server errors can echo credentials. Never expose their body or inner exception.
			logger.WriteError("The user operation failed. Check permissions and inspect the account before retrying; partial changes may exist.");
			return 1;
		}
	}

	private static string ReadPassword(ManageUserOptions options) {
		const string prefix = "CLIO_ADMIN_PASSWORD_";
		const string invalidReference = "Supply a populated password reference named CLIO_ADMIN_PASSWORD_<SUFFIX> using uppercase letters, digits or underscores.";
		string reference = options.PasswordEnvironmentVariable;
		if (reference is null || !reference.StartsWith(prefix, StringComparison.Ordinal)
			|| reference.Length == prefix.Length
			|| reference[prefix.Length..].Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))) {
			throw new ArgumentException(invalidReference);
		}
		string password = Environment.GetEnvironmentVariable(reference);
		if (string.IsNullOrEmpty(password)) { throw new ArgumentException(invalidReference); }
		return password;
	}
}
