using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Requests;
using Clio.UserEnvironment;
using AbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Common.IIS;

/// <summary>Immutable scope selected from the current environment registration.</summary>
/// <param name="EnvironmentName">Owning environment.</param>
/// <param name="CreatioPath">Owning CRM directory.</param>
/// <param name="Attachment">Exact recorded attachment.</param>
public sealed record IdentityRemovalPlan(string EnvironmentName, string CreatioPath,
	IdentityServiceAttachment Attachment);

/// <summary>Validates and removes the explicitly recorded local identity component.</summary>
public interface IIdentityServiceLifecycle {
	/// <summary>Validates and records a deployment target before artifacts can be created.</summary>
	void RecordDeployment(string environmentName, IdentityServiceAttachment attachment, bool overwrite);
	/// <summary>Resolves and prevalidates the recorded component, or returns null for an empty attachment.</summary>
	IdentityRemovalPlan PrepareRemoval(string environmentName);
	/// <summary>Checks that the registration and local targets still match the selected scope.</summary>
	void Validate(IdentityRemovalPlan plan);
	/// <summary>Persists completion of the CRM reference stage before identity is stopped.</summary>
	IdentityRemovalPlan MarkReferencesCleared(IdentityRemovalPlan plan);
	/// <summary>Removes only recorded local artifacts and then clears the attachment and matching credentials.</summary>
	void Remove(IdentityRemovalPlan plan);
}

/// <inheritdoc />
public sealed class IdentityServiceLifecycle(ISettingsRepository settingsRepository,
	IIisScanner iisScanner, AbstractionsFileSystem fileSystem, ILogger logger) : IIdentityServiceLifecycle {

	/// <inheritdoc />
	public void RecordDeployment(string environmentName, IdentityServiceAttachment attachment, bool overwrite) {
		EnvironmentSettings environment = settingsRepository.FindCurrentEnvironment(environmentName)
			?? throw new InvalidOperationException("The identity owner is no longer registered.");
		IdentityServiceAttachment previous = environment.IdentityService;
		if (!previous.IsEmpty && (!SamePath(previous.EnvironmentPath, attachment.EnvironmentPath)
			|| !SameName(previous.IisTarget, attachment.IisTarget))) {
			throw new InvalidOperationException("The environment already owns a different identity. Remove it first.");
		}
		IdentityRemovalPlan plan = new(environmentName, environment.EnvironmentPath, attachment);
		ValidateScope(plan);
		if (fileSystem.Directory.Exists(attachment.EnvironmentPath)
			&& (!overwrite || (fileSystem.Directory.EnumerateFileSystemEntries(attachment.EnvironmentPath).Any()
				&& (!fileSystem.File.Exists(Path.Combine(attachment.EnvironmentPath, "IdentityService.dll"))
					|| !fileSystem.File.Exists(Path.Combine(attachment.EnvironmentPath, "appsettings.json")))))) {
			throw new InvalidOperationException("Identity deployment requires an empty or recognized target and explicit overwrite.");
		}
		UnregisteredSite target = ReadAndValidateTargets(plan);
		if (target is not null && !target.Uris.Any(uri => SameUri(uri.AbsoluteUri, attachment.Uri))) {
			throw new InvalidOperationException("The existing identity IIS binding does not match the requested address.");
		}
		if (!settingsRepository.UpdateIdentityAttachment(environmentName, environment.EnvironmentPath, previous, attachment)) {
			throw new InvalidOperationException("The identity registration changed before deployment. No files were deployed.");
		}
	}

	/// <inheritdoc />
	public IdentityRemovalPlan PrepareRemoval(string environmentName) {
		EnvironmentSettings environment = settingsRepository.FindCurrentEnvironment(environmentName)
			?? throw new InvalidOperationException("The identity owner is no longer registered.");
		if (environment.IdentityService.IsEmpty) {
			return null;
		}
		IdentityRemovalPlan plan = new(environmentName, environment.EnvironmentPath, environment.IdentityService);
		Validate(plan);
		logger.WriteInfo($"Identity removal target: {plan.Attachment.IisTarget}; directory: {plan.Attachment.EnvironmentPath}");
		return plan;
	}

	/// <inheritdoc />
	public void Validate(IdentityRemovalPlan plan) {
		ValidateAndFindTarget(plan);
	}

	private UnregisteredSite ValidateAndFindTarget(IdentityRemovalPlan plan) {
		EnvironmentSettings current = settingsRepository.FindCurrentEnvironment(plan.EnvironmentName);
		if (current is null || current.IdentityService != plan.Attachment
			|| !SamePath(current.EnvironmentPath, plan.CreatioPath)) {
			throw new InvalidOperationException("The identity attachment changed. Cleanup was stopped; retry with the current registration.");
		}
		ValidateScope(plan);
		return ReadAndValidateTargets(plan);
	}

	/// <inheritdoc />
	public IdentityRemovalPlan MarkReferencesCleared(IdentityRemovalPlan plan) {
		IdentityServiceAttachment updated = plan.Attachment with { CrmReferencesCleared = true };
		if (!settingsRepository.UpdateIdentityAttachment(plan.EnvironmentName, plan.CreatioPath, plan.Attachment, updated)) {
			throw new InvalidOperationException("The identity attachment changed while clearing CRM references.");
		}
		return plan with { Attachment = updated };
	}

	/// <inheritdoc />
	public void Remove(IdentityRemovalPlan plan) {
		IdentityServiceAttachment attachment = plan.Attachment;
		UnregisteredSite target = ValidateAndFindTarget(plan);
		if (target is not null) {
			if (!iisScanner.TryStopIisTarget(attachment.IisTarget, attachment.EnvironmentPath, attachment.ApplicationPool)
				|| iisScanner.StopAppPoolIfOwnedByTargets(attachment.ApplicationPool, [attachment.IisTarget])
					== IisAppPoolMutationResult.Failed) {
				throw new InvalidOperationException("Identity IIS target could not be stopped safely. The attachment was retained.");
			}
			if (!iisScanner.TryDeleteIisTarget(attachment.IisTarget, attachment.EnvironmentPath, attachment.ApplicationPool)) {
				throw new InvalidOperationException("Identity IIS target could not be removed safely. The attachment was retained.");
			}
		}
		IisAppPoolMutationResult poolResult = iisScanner.DeleteAppPoolIfUnused(attachment.ApplicationPool);
		if (poolResult == IisAppPoolMutationResult.Failed) {
			throw new InvalidOperationException("Identity application pool cleanup failed. The attachment was retained.");
		}
		if (poolResult == IisAppPoolMutationResult.PreservedShared) {
			logger.WriteInfo($"Shared application pool '{attachment.ApplicationPool}' was preserved.");
		}
		if (ValidateAndFindTarget(plan) is not null) {
			throw new InvalidOperationException("The identity IIS target is still present. Identity files were preserved.");
		}
		if (fileSystem.Directory.Exists(attachment.EnvironmentPath)) {
			fileSystem.Directory.Delete(attachment.EnvironmentPath, recursive: true);
		}
		if (fileSystem.Directory.Exists(attachment.EnvironmentPath)) {
			throw new IOException("Identity files could not be removed. The attachment was retained.");
		}
		if (!settingsRepository.UpdateIdentityAttachment(plan.EnvironmentName, plan.CreatioPath,
			attachment, new IdentityServiceAttachment(), clearMatchingCredentials: true)) {
			throw new InvalidOperationException("Identity artifacts were removed but its registration changed. Inspect the attachment before retrying.");
		}
		logger.WriteInfo("IdentityService was removed; its database was not modified.");
	}

	private void ValidateScope(IdentityRemovalPlan plan) {
		IdentityServiceAttachment attachment = plan.Attachment;
		if (string.IsNullOrWhiteSpace(attachment.EnvironmentPath) || string.IsNullOrWhiteSpace(attachment.IisTarget)
			|| string.IsNullOrWhiteSpace(attachment.ApplicationPool) || !Uri.TryCreate(attachment.Uri, UriKind.Absolute, out Uri uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
			|| !string.IsNullOrEmpty(uri.UserInfo)) {
			throw new InvalidOperationException("The identity attachment is incomplete. Supply its absolute path, IIS target, pool and URL.");
		}
		ValidatePath(attachment.EnvironmentPath);
		if (Overlaps(attachment.EnvironmentPath, plan.CreatioPath)) {
			throw new InvalidOperationException("Identity and CRM deployment directories must not overlap.");
		}
		foreach ((string name, EnvironmentSettings environment) in settingsRepository.GetAllEnvironments()) {
			if (!string.IsNullOrWhiteSpace(environment.EnvironmentPath)
				&& Overlaps(attachment.EnvironmentPath, environment.EnvironmentPath)) {
				throw new InvalidOperationException("The identity directory overlaps a registered CRM. Cleanup is not safe.");
			}
			if (SameName(name, plan.EnvironmentName)) {
				continue;
			}
			IdentityServiceAttachment other = environment.IdentityService;
			if ((!string.IsNullOrWhiteSpace(other.EnvironmentPath) && Overlaps(attachment.EnvironmentPath, other.EnvironmentPath))
				|| SameName(attachment.IisTarget, other.IisTarget)
				|| SameUri(attachment.Uri, other.Uri)
				|| SameUri(attachment.Uri.TrimEnd('/') + "/connect/token", environment.AuthAppUri)) {
				throw new InvalidOperationException("Another environment references this identity. The identity and CRM were preserved.");
			}
		}
	}

	private UnregisteredSite ReadAndValidateTargets(IdentityRemovalPlan plan) {
		if (!iisScanner.TryFindAllIisTargets(out IReadOnlyList<UnregisteredSite> targets)) {
			throw new InvalidOperationException("A complete IIS inventory is required to validate the recorded identity target.");
		}
		IdentityServiceAttachment attachment = plan.Attachment;
		UnregisteredSite selected = null;
		if (!iisScanner.TryFindAllVirtualDirectories(out IReadOnlyList<IisVirtualDirectory> directories)) {
			throw new InvalidOperationException("The IIS virtual-directory inventory could not be validated.");
		}
		string rootName = attachment.IisTarget.TrimEnd('/') + "/";
		foreach (IisVirtualDirectory directory in directories) {
			bool isRoot = SameName(directory.Name, rootName);
			if ((isRoot && !SamePath(directory.PhysicalPath, attachment.EnvironmentPath))
				|| (!isRoot && (directory.Name.StartsWith(rootName, StringComparison.OrdinalIgnoreCase)
					|| Overlaps(directory.PhysicalPath, attachment.EnvironmentPath)))) {
				throw new InvalidOperationException("An unrelated IIS virtual directory uses the identity target or folder. Cleanup was stopped.");
			}
		}
		foreach (UnregisteredSite target in targets) {
			if (SameName(target.siteBinding.name, attachment.IisTarget)) {
				if (selected is not null || !SamePath(target.siteBinding.path, attachment.EnvironmentPath)
					|| !SameName(target.siteBinding.appPoolName, attachment.ApplicationPool)
					|| !iisScanner.IsIisTargetExclusive(attachment.IisTarget)) {
					throw new InvalidOperationException("The recorded identity IIS target changed or contains unrelated applications.");
				}
				selected = target;
			}
			else if (Overlaps(target.siteBinding.path, attachment.EnvironmentPath)) {
				throw new InvalidOperationException("Another IIS target uses the identity directory. Files were preserved.");
			}
		}
		return selected;
	}

	private void ValidatePath(string path) {
		if (!Path.IsPathFullyQualified(path) || path.Contains('%') || path.Contains('"')
			|| SamePath(path, Path.GetPathRoot(path))) {
			throw new InvalidOperationException("Identity requires an absolute non-root deployment directory.");
		}
		for (string current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current)) {
			if ((fileSystem.Directory.Exists(current) || fileSystem.File.Exists(current))
				&& fileSystem.File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) {
				throw new InvalidOperationException("Identity cleanup does not follow filesystem links.");
			}
		}
	}

	private static bool SameName(string first, string second) => !string.IsNullOrWhiteSpace(first)
		&& string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

	private static bool SameUri(string first, string second) => Uri.TryCreate(first?.TrimEnd('/'), UriKind.Absolute, out Uri left)
		&& Uri.TryCreate(second?.TrimEnd('/'), UriKind.Absolute, out Uri right) && left.Equals(right);

	private static bool SamePath(string first, string second) => !string.IsNullOrWhiteSpace(first)
		&& !string.IsNullOrWhiteSpace(second) && string.Equals(DirectoryPathIdentity.Normalize(first),
			DirectoryPathIdentity.Normalize(second), StringComparison.OrdinalIgnoreCase);

	private static bool Overlaps(string first, string second) {
		if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) {
			return false;
		}
		string left = DirectoryPathIdentity.Normalize(first, expandEnvironmentVariables: true).TrimEnd(Path.DirectorySeparatorChar);
		string right = DirectoryPathIdentity.Normalize(second, expandEnvironmentVariables: true).TrimEnd(Path.DirectorySeparatorChar);
		return SameName(left, right) || left.StartsWith(right + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
