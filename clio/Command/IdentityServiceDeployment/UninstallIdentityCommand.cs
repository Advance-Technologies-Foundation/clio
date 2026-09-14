using System;
using Clio.Common;
using Clio.Common.IIS;
using CommandLine;

namespace Clio.Command.IdentityServiceDeployment;

/// <summary>Options for removing the identity component while retaining its CRM.</summary>
[Verb("uninstall-identity", HelpText = "Remove a recorded local IdentityService while preserving Creatio and its database")]
[FeatureToggle("deploy-identity")]
public sealed class UninstallIdentityOptions : EnvironmentNameOptions {
	/// <summary>Explicitly leave CRM system settings untouched when identity authentication is unavailable.</summary>
	[Option("skip-crm-cleanup", Default = false,
		HelpText = "Recovery only: leave CRM identity system settings untouched and report a warning")]
	public bool SkipCrmCleanup { get; set; }
}

/// <summary>Clears matching CRM references before stopping the authentication service.</summary>
public interface IIdentityReferenceCleanup {
	/// <summary>Clears only the CRM configuration still pointing at the supplied identity.</summary>
	void Clear(IdentityServiceAttachment attachment);
}

/// <inheritdoc />
public sealed class IdentityReferenceCleanup(ISysSettingsManager settingsManager) : IIdentityReferenceCleanup {
	private const string IdentityUrlSetting = "OAuth20IdentityServerUrl";
	private const string IdentityClientSetting = "OAuth20IdentityServerClientId";
	private const string IdentitySecretSetting = "OAuth20IdentityServerClientSecret";

	/// <inheritdoc />
	public void Clear(IdentityServiceAttachment attachment) {
		string currentUrl = settingsManager.GetAllUsersDefaultByCode(IdentityUrlSetting);
		if (!Uri.TryCreate(currentUrl?.TrimEnd('/'), UriKind.Absolute, out Uri current)
			|| !Uri.TryCreate(attachment.Uri?.TrimEnd('/'), UriKind.Absolute, out Uri expected)
			|| !current.Equals(expected)) {
			return;
		}
		// Clear the URL last: it may still be needed to validate this request's bearer token.
		ClearSetting(IdentitySecretSetting, "SecureText");
		ClearSetting(IdentityClientSetting, "Text");
		ClearSetting(IdentityUrlSetting, "Text");
	}

	private void ClearSetting(string code, string type) {
		if (!settingsManager.UpdateSysSetting(code, string.Empty, type)) {
			throw new InvalidOperationException("CRM identity references could not be cleared. Identity was not removed; retry or explicitly use --skip-crm-cleanup.");
		}
	}
}

/// <summary>Uninstalls the recorded local IdentityService without dropping a database.</summary>
public sealed class UninstallIdentityCommand(IIdentityServiceLifecycle lifecycle,
	IIdentityReferenceCleanup referenceCleanup, IDeploymentTargetReservation reservation,
	ILogger logger) : Command<UninstallIdentityOptions> {

	/// <inheritdoc />
	public override int Execute(UninstallIdentityOptions options) {
		try {
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new ArgumentException("An explicit environment name is required.");
			}
			using IDisposable environmentLease = reservation.AcquireEnvironment(options.Environment);
			IdentityRemovalPlan plan = lifecycle.PrepareRemoval(options.Environment);
			if (plan is null) {
				logger.WriteInfo("No local IdentityService is recorded for this environment. Nothing was removed.");
				return 0;
			}
			using IDisposable pathLease = reservation.Acquire(plan.Attachment.EnvironmentPath);
			lifecycle.Validate(plan);
			if (options.SkipCrmCleanup) {
				logger.WriteWarning("CRM identity system settings were intentionally left unchanged (--skip-crm-cleanup). Repair those references manually.");
			}
			if (!plan.Attachment.CrmReferencesCleared) {
				if (!options.SkipCrmCleanup) {
					referenceCleanup.Clear(plan.Attachment);
				}
				plan = lifecycle.MarkReferencesCleared(plan);
			}
			lifecycle.Remove(plan);
			return 0;
		}
		catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
			or System.IO.IOException or UnauthorizedAccessException or AggregateException) {
			logger.WriteError(exception.GetReadableMessageException());
			return 1;
		}
	}
}
