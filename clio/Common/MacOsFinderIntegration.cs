using System;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Common;

/// <summary>
/// Installs and removes the macOS Finder "Deploy Creatio" Quick Action. The Quick Action lets a
/// user right-click a folder (containing the Creatio binaries) or a Creatio <c>.zip</c> in Finder
/// and run <c>clio deploy-creatio</c> against it.
/// </summary>
public interface IMacOsFinderIntegration {

	/// <summary>
	/// Installs or updates the bundled Finder Quick Action in the current user's Services folder.
	/// The operation is idempotent and refreshes the installed copy only when the bundled version is newer.
	/// </summary>
	Task InstallAsync();

	/// <summary>
	/// Removes the Finder Quick Action from the current user's Services folder, if present.
	/// </summary>
	Task UninstallAsync();

	/// <summary>
	/// Gets a value indicating whether the Finder Quick Action is currently installed.
	/// </summary>
	bool IsInstalled();

}

/// <summary>
/// Default <see cref="IMacOsFinderIntegration"/> implementation. Copies the Quick Action bundled next
/// to the clio assembly (<c>finder/DeployCreatio.workflow</c>) into <c>~/Library/Services</c>.
/// </summary>
public class MacOsFinderIntegration : IMacOsFinderIntegration {

	private const string WorkflowFolderName = "finder";
	private const string WorkflowName = "DeployCreatio.workflow";
	private const string ServicesRelativePath = "Library/Services";

	// Matches CFBundleIdentifier, NSMenuItem>default and NSMessage in the bundled Info.plist.
	// macOS stores the per-service enable state in pbs (NSServicesStatus) under this key.
	private const string ServiceStatusKey =
		"com.creatio.clio.DeployCreatio - Deploy Creatio - runWorkflowAsService";

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;

	/// <summary>
	/// Initializes a new instance of the <see cref="MacOsFinderIntegration"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for all path and copy operations.</param>
	/// <param name="logger">Logger used to report installation outcomes.</param>
	/// <param name="processExecutor">Process executor used to enable the Quick Action in the Finder context menu.</param>
	public MacOsFinderIntegration(IFileSystem fileSystem, ILogger logger, IProcessExecutor processExecutor) {
		_fileSystem = fileSystem;
		_logger = logger;
		_processExecutor = processExecutor;
	}

	private string BundledWorkflowPath =>
		_fileSystem.Combine(AppContext.BaseDirectory, WorkflowFolderName, WorkflowName);

	private string InstalledWorkflowPath {
		get {
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			return _fileSystem.Combine(home, ServicesRelativePath, WorkflowName);
		}
	}

	/// <inheritdoc/>
	public bool IsInstalled() =>
		_fileSystem.ExistsDirectory(InstalledWorkflowPath);

	/// <inheritdoc/>
	public Task InstallAsync() {
		try {
			string source = BundledWorkflowPath;
			if (!_fileSystem.ExistsDirectory(source)) {
				_logger.WriteWarning(
					"Bundled Deploy Creatio Finder quick action was not found; skipping installation.");
				return Task.CompletedTask;
			}
			string destination = InstalledWorkflowPath;
			if (_fileSystem.ExistsDirectory(destination) && !IsNewer(source, destination)) {
				return Task.CompletedTask;
			}
			_fileSystem.DeleteDirectoryIfExists(destination);
			_fileSystem.CopyDirectory(source, destination, overwrite: true);
			PreconfigureAndRefresh();
			_logger.WriteInfo("macOS Finder 'Deploy Creatio' quick action installed or updated.");
			_logger.WriteInfo(
				"One-time step: enable it via Finder right-click > Quick Actions > Customize... " +
				"(or System Settings > Keyboard > Keyboard Shortcuts > Services > Files and Folders). " +
				"macOS requires this confirmation before the action appears in the menu.");
		} catch (Exception exception) {
			_logger.WriteWarning(
				$"Could not install the Deploy Creatio Finder quick action: {exception.Message}");
		}
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task UninstallAsync() {
		try {
			string destination = InstalledWorkflowPath;
			if (_fileSystem.ExistsDirectory(destination)) {
				_fileSystem.DeleteDirectoryIfExists(destination);
				RefreshFinderServices();
				_logger.WriteInfo("macOS Finder 'Deploy Creatio' quick action removed.");
			}
		} catch (Exception exception) {
			_logger.WriteWarning(
				$"Could not remove the Deploy Creatio Finder quick action: {exception.Message}");
		}
		return Task.CompletedTask;
	}

	private bool IsNewer(string source, string destination) {
		DateTime sourceTime = _fileSystem.GetDirectoryInfo(source).LastWriteTimeUtc;
		DateTime destinationTime = _fileSystem.GetDirectoryInfo(destination).LastWriteTimeUtc;
		return sourceTime > destinationTime;
	}

	/// <summary>
	/// Pre-seeds the enabled state stored by <c>pbs</c> (<c>NSServicesStatus</c>) using the exact
	/// format System Settings writes, then rebuilds the Services cache so the action is discoverable
	/// in the Finder "Quick Actions &gt; Customize…" list. Note: on modern macOS the operating system
	/// only honors this state in the context menu once the user confirms it through the trusted
	/// System Settings / Customize UI, so this call cannot fully auto-enable the menu item by itself.
	/// </summary>
	private void PreconfigureAndRefresh() {
		// macOS 13+ honors only the presentation_modes dict here (the legacy
		// enabled_context_menu / enabled_services_menu keys are ignored). This mirrors
		// exactly what System Settings writes when the Service is toggled on.
		string script =
			"defaults write pbs NSServicesStatus -dict-add '" + ServiceStatusKey + "' " +
			"'{ presentation_modes = { ContextMenu = 1; FinderPreview = 1; " +
			"ServicesMenu = 1; TouchBar = 0; }; }' && " +
			RefreshFinderServicesScript();
		RunShell(script);
	}

	/// <summary>
	/// Rebuilds the Services cache so context-menu changes are picked up.
	/// </summary>
	private void RefreshFinderServices() => RunShell(RefreshFinderServicesScript());

	private static string RefreshFinderServicesScript() =>
		"/System/Library/CoreServices/pbs -flush";

	private void RunShell(string script) {
		_processExecutor.Execute("/bin/bash", "-c \"" + script + "\"", waitForExit: true,
			suppressErrors: true);
	}

}
