using System;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for resolving the package a run's design-time writes land in. The hidden verb documents the
/// canonical name without registering it in the CLI parser.
/// </summary>
[Verb("get-target-package", Hidden = true,
	HelpText = "Resolve the package design-time writes land in (MCP probe surface)")]
public class GetTargetPackageOptions : RemoteCommandOptions {

	/// <summary>
	/// Name of the package to resolve. Blank means "the package the environment's <c>CurrentPackageId</c>
	/// system setting points at".
	/// </summary>
	public string PackageName { get; set; }
}

/// <summary>
/// Structured response of the <c>get-target-package</c> probe.
/// </summary>
public sealed class GetTargetPackageResponse : EnvironmentProbeResponse {

	/// <summary>
	/// Gets the name of the package the run's design-time writes land in; omitted on failure.
	/// </summary>
	[JsonPropertyName("package-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageName { get; init; }
}

/// <summary>
/// Resolves the package a run's design-time writes land in and verifies it can receive them.
/// </summary>
public class GetTargetPackageCommand : Command<GetTargetPackageOptions> {

	private readonly IPackageTargetResolver _targetResolver;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="GetTargetPackageCommand"/> class.
	/// </summary>
	public GetTargetPackageCommand(IPackageTargetResolver targetResolver, ILogger logger) {
		_targetResolver = targetResolver;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(GetTargetPackageOptions options) {
		if (!TryGetTargetPackage(options, out GetTargetPackageResponse response)) {
			_logger.WriteError(response.Error ?? "Failed to resolve the target package.");
			return 1;
		}
		_logger.WriteInfo(string.IsNullOrWhiteSpace(options.PackageName)
			? $"Target package: {response.PackageName} " +
				$"(from the {PackageTargetResolver.CurrentPackageSettingCode} system setting)"
			: $"Target package: {response.PackageName}");
		return 0;
	}

	/// <summary>
	/// Resolves the target package named by <paramref name="options"/>, or the environment's current package
	/// when none is named.
	/// </summary>
	/// <param name="options">The package to resolve and the environment settings.</param>
	/// <param name="response">
	/// The structured probe response: the resolved package name, or the failure with
	/// <see cref="EnvironmentProbeResponse.ResolutionFailed"/> set.
	/// </param>
	/// <returns><see langword="true"/> when a usable target package was resolved.</returns>
	public virtual bool TryGetTargetPackage(
		GetTargetPackageOptions options, out GetTargetPackageResponse response) {
		ArgumentNullException.ThrowIfNull(options);
		PackageTargetResolution resolution = _targetResolver.Resolve(options.PackageName, requireEditable: true);
		if (!resolution.Success) {
			response = new GetTargetPackageResponse {
				Success = false,
				ResolutionFailed = resolution.ResolutionFailed,
				Error = resolution.Error
			};
			return false;
		}
		response = new GetTargetPackageResponse {
			Success = true,
			PackageName = resolution.PackageName
		};
		return true;
	}
}
