using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// Outcome of resolving the package that design-time writes land in.
/// </summary>
/// <remarks>
/// A failure carries which of the two kinds it is: the environment answered and named no usable target, or
/// the environment could not be asked at all — see <see cref="ResolutionFailed"/>.
/// </remarks>
public sealed record PackageTargetResolution {

	/// <summary>Whether a usable target package was resolved.</summary>
	public bool Success { get; private init; }

	/// <summary>Name of the resolved package; null on failure.</summary>
	public string PackageName { get; private init; }

	/// <summary>UId of the resolved package; <see cref="Guid.Empty"/> on failure.</summary>
	public Guid PackageUId { get; private init; }

	/// <summary>The failure message, naming the action that resolves it; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>
	/// Whether the failure is definitive: the environment answered and named no usable target. False when the
	/// environment could not be reached or refused the query.
	/// </summary>
	public bool ResolutionFailed { get; private init; }

	/// <summary>Creates a successful resolution.</summary>
	/// <param name="packageName">Name of the resolved package.</param>
	/// <param name="packageUId">UId of the resolved package.</param>
	public static PackageTargetResolution Resolved(string packageName, Guid packageUId) {
		return new PackageTargetResolution {
			Success = true, PackageName = packageName, PackageUId = packageUId
		};
	}

	/// <summary>
	/// Creates a definitive failure: the environment answered and there is no usable target package.
	/// </summary>
	/// <param name="error">The failure message, naming the action that resolves it.</param>
	public static PackageTargetResolution Unresolvable(string error) {
		return new PackageTargetResolution { Error = error, ResolutionFailed = true };
	}

	/// <summary>
	/// Creates a non-definitive failure: the environment could not be asked, so whether a usable target exists
	/// is unknown.
	/// </summary>
	/// <param name="error">The failure message.</param>
	public static PackageTargetResolution Unavailable(string error) {
		return new PackageTargetResolution { Error = error, ResolutionFailed = false };
	}
}

/// <summary>
/// Resolves the package a run's design-time writes land in.
/// </summary>
public interface IPackageTargetResolver {

	/// <summary>
	/// Resolves the target package.
	/// </summary>
	/// <param name="packageName">
	/// Name of the package to use. Blank means "the package the environment's <c>CurrentPackageId</c> system
	/// setting points at" — the same convention design-time writes follow; a well-known package name is never
	/// silently substituted, and a package the caller named is never silently replaced by another one.
	/// </param>
	/// <param name="requireEditable">
	/// Whether a locked package is refused here rather than left to the write that follows. Request it only
	/// when the caller mutates the environment before delivering, where learning the package is closed from
	/// the write itself leaves the run half-applied. A caller whose only effect is the delivery itself gains
	/// nothing from the extra round-trip and must not have a package refused that the write would accept.
	/// </param>
	/// <returns>
	/// The resolved package, or a classified failure: the named package does not exist, the named or current
	/// package is locked and <paramref name="requireEditable"/> was requested, no package was named and the
	/// current-package setting names none or names one that no longer resolves, or the environment could not
	/// be asked.
	/// </returns>
	PackageTargetResolution Resolve(string packageName, bool requireEditable = false);
}

/// <inheritdoc />
internal sealed class PackageTargetResolver(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	ISysSettingsManager sysSettingsManager) : IPackageTargetResolver {

	/// <summary>The system setting that names the package design-time writes land in when the caller names none.</summary>
	internal const string CurrentPackageSettingCode = "CurrentPackageId";

	private const int EditableInstallType = 0;

	private const string SysPackageSchema = "SysPackage";

	private static readonly IReadOnlyList<SelectQueryHelper.SelectQueryColumnDefinition> PackageColumns = [
		new("Name", "Name"),
		new("UId", "UId"),
		new("InstallType", "InstallType")
	];

	/// <inheritdoc />
	public PackageTargetResolution Resolve(string packageName, bool requireEditable = false) {
		return string.IsNullOrWhiteSpace(packageName)
			? ResolveCurrentPackage(requireEditable)
			: ResolveNamedPackage(packageName.Trim(), requireEditable);
	}

	private PackageTargetResolution ResolveNamedPackage(string packageName, bool requireEditable) {
		List<PackageRowDto> rows;
		try {
			rows = SelectPackages([]);
		} catch (Exception exception) {
			return PackageTargetResolution.Unavailable(DescribeUnavailable(exception));
		}
		PackageRowDto row = rows.FirstOrDefault(candidate =>
			string.Equals(candidate.Name, packageName, StringComparison.OrdinalIgnoreCase));
		if (row is null) {
			return PackageTargetResolution.Unresolvable(
				$"Package '{packageName}' was not found in the environment. Check the name against list-packages.");
		}
		if (requireEditable && IsLocked(row)) {
			return PackageTargetResolution.Unresolvable(
				$"Package '{row.Name}' is locked, so it cannot receive design-time writes. Unlock it with " +
				"unlock-package, or name another package.");
		}
		return Materialize(row, $"Package '{row.Name}'");
	}

	private PackageTargetResolution ResolveCurrentPackage(bool requireEditable) {
		string currentPackageId;
		List<PackageRowDto> rows;
		try {
			currentPackageId = sysSettingsManager.GetSysSettingValueByCode(CurrentPackageSettingCode);
			if (!Guid.TryParse(currentPackageId, out Guid packageId) || packageId == Guid.Empty) {
				return PackageTargetResolution.Unresolvable(
					$"No package was named, and the environment's {CurrentPackageSettingCode} system setting does " +
					"not point at one, so there is nowhere to deliver the package data. Name the package " +
					"explicitly (see list-packages for the available names).");
			}
			rows = SelectPackages([
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"Id", packageId.ToString(), SelectQueryHelper.GuidDataValueType)
			]);
		} catch (Exception exception) {
			return PackageTargetResolution.Unavailable(DescribeUnavailable(exception));
		}
		if (rows.Count > 1) {
			return PackageTargetResolution.Unresolvable(
				$"The environment's {CurrentPackageSettingCode} system setting points at package " +
				$"'{currentPackageId}', which matched {rows.Count} packages, so the delivery target cannot be " +
				"told apart. Name the package explicitly (see list-packages for the available names).");
		}
		PackageRowDto row = rows.FirstOrDefault();
		if (row is null || string.IsNullOrWhiteSpace(row.Name)) {
			return PackageTargetResolution.Unresolvable(
				$"The environment's {CurrentPackageSettingCode} system setting points at package " +
				$"'{currentPackageId}', which could not be resolved to a usable package. Name the package " +
				"explicitly (see list-packages for the available names).");
		}
		if (requireEditable && IsLocked(row)) {
			return PackageTargetResolution.Unresolvable(
				$"The environment's {CurrentPackageSettingCode} system setting points at package '{row.Name}', " +
				"which is locked, so it cannot receive design-time writes. Unlock it with unlock-package, or " +
				"name another package.");
		}
		return Materialize(row, $"The {CurrentPackageSettingCode} package '{row.Name}'");
	}

	private static PackageTargetResolution Materialize(PackageRowDto row, string subject) {
		if (!Guid.TryParse(row.UId, out Guid packageUId) || packageUId == Guid.Empty) {
			return PackageTargetResolution.Unresolvable(
				$"{subject} has no usable UId in the environment, so package data cannot be addressed to it. " +
				"Name another package (see list-packages for the available names).");
		}
		return PackageTargetResolution.Resolved(row.Name, packageUId);
	}

	private static bool IsLocked(PackageRowDto row) {
		return row.InstallType is not null && row.InstallType != EditableInstallType;
	}

	private static string DescribeUnavailable(Exception exception) {
		return "The environment could not be asked which package to deliver the data into: " +
			$"{exception.Message}";
	}

	private List<PackageRowDto> SelectPackages(
		IReadOnlyList<SelectQueryHelper.SelectQueryFilterDefinition> filters) {
		PackageSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<PackageSelectResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(SysPackageSchema, PackageColumns, filters));
		return response.Rows;
	}

	private sealed class PackageSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageRowDto> Rows { get; init; } = [];
	}

	private sealed class PackageRowDto {
		[JsonPropertyName("Name")]
		public string Name { get; init; }

		[JsonPropertyName("UId")]
		public string UId { get; init; }

		[JsonPropertyName("InstallType")]
		public int? InstallType { get; init; }
	}
}
