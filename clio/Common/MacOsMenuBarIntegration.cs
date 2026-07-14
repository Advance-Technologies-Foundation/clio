using System;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>
/// Installs and removes the macOS menu bar (status bar) companion app for clio. The app shows a
/// status item in the top menu bar offering "Deploy Creatio…" and, for every registered Creatio
/// host, Start / Stop / Open-folder actions sourced from <c>clio hosts --json</c>.
/// </summary>
public interface IMacOsMenuBarIntegration {

	/// <summary>
	/// Compiles the bundled Swift menu bar app (when needed) and installs a per-user LaunchAgent so
	/// the app starts at login. The operation is idempotent and recompiles only when the bundled
	/// source is newer than the installed binary.
	/// </summary>
	Task InstallAsync();

	/// <summary>
	/// Stops the menu bar app, removes its LaunchAgent, and deletes the compiled binary.
	/// </summary>
	Task UninstallAsync();

	/// <summary>
	/// Gets a value indicating whether the menu bar app LaunchAgent is currently installed.
	/// </summary>
	bool IsInstalled();

}

/// <summary>
/// Default <see cref="IMacOsMenuBarIntegration"/> implementation. Compiles
/// <c>finder/menubar/ClioMenuBar.swift</c> (bundled next to the clio assembly) with <c>swiftc</c>
/// into <c>~/Library/Application Support/clio</c> and registers a LaunchAgent that runs it.
/// </summary>
public class MacOsMenuBarIntegration : IMacOsMenuBarIntegration {

	private const string SourceFolderName = "finder";
	private const string SourceSubFolderName = "menubar";
	private const string SourceFileName = "ClioMenuBar.swift";
	private const string BinaryName = "ClioMenuBar";
	private const string LaunchAgentLabel = "com.creatio.clio.menubar";
	private const string SupportRelativePath = "Library/Application Support/clio";
	private const string LaunchAgentsRelativePath = "Library/LaunchAgents";

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;

	/// <summary>
	/// Initializes a new instance of the <see cref="MacOsMenuBarIntegration"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for all path and file operations.</param>
	/// <param name="logger">Logger used to report installation outcomes.</param>
	/// <param name="processExecutor">Process executor used to compile the app and manage the LaunchAgent.</param>
	public MacOsMenuBarIntegration(IFileSystem fileSystem, ILogger logger, IProcessExecutor processExecutor) {
		_fileSystem = fileSystem;
		_logger = logger;
		_processExecutor = processExecutor;
	}

	private string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	private string SourcePath =>
		_fileSystem.Combine(AppContext.BaseDirectory, SourceFolderName, SourceSubFolderName, SourceFileName);

	private string BinaryPath =>
		_fileSystem.Combine(Home, SupportRelativePath, BinaryName);

	private string LaunchAgentPath =>
		_fileSystem.Combine(Home, LaunchAgentsRelativePath, LaunchAgentLabel + ".plist");

	/// <inheritdoc/>
	public bool IsInstalled() =>
		_fileSystem.ExistsFile(LaunchAgentPath);

	/// <inheritdoc/>
	public Task InstallAsync() {
		try {
			if (!_fileSystem.ExistsFile(SourcePath)) {
				_logger.WriteWarning(
					"Bundled clio menu bar app source was not found; skipping menu bar installation.");
				return Task.CompletedTask;
			}
			if (!IsSwiftcAvailable()) {
				_logger.WriteWarning(
					"swiftc was not found; skipping menu bar app. Install Xcode Command Line Tools " +
					"('xcode-select --install') and run 'clio register' to enable it.");
				return Task.CompletedTask;
			}
			bool agentMissing = !_fileSystem.ExistsFile(LaunchAgentPath);
			CompileResult compileResult = CompileIfNeeded();
			if (compileResult == CompileResult.Failed) {
				return Task.CompletedTask;
			}
			if (compileResult == CompileResult.Compiled || agentMissing) {
				WriteLaunchAgent();
				LoadLaunchAgent();
				_logger.WriteInfo("macOS clio menu bar app installed and started.");
			}
		} catch (Exception exception) {
			_logger.WriteWarning($"Could not install the clio menu bar app: {exception.Message}");
		}
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task UninstallAsync() {
		try {
			if (_fileSystem.ExistsFile(LaunchAgentPath)) {
				UnloadLaunchAgent();
				_fileSystem.DeleteFileIfExists(LaunchAgentPath);
			}
			_fileSystem.DeleteFileIfExists(BinaryPath);
			_logger.WriteInfo("macOS clio menu bar app removed.");
		} catch (Exception exception) {
			_logger.WriteWarning($"Could not remove the clio menu bar app: {exception.Message}");
		}
		return Task.CompletedTask;
	}

	private bool IsSwiftcAvailable() {
		string result = RunShell("command -v swiftc");
		return !string.IsNullOrWhiteSpace(result);
	}

	private CompileResult CompileIfNeeded() {
		string binaryDirectory = _fileSystem.Combine(Home, SupportRelativePath);
		_fileSystem.CreateDirectoryIfNotExists(binaryDirectory);
		if (_fileSystem.ExistsFile(BinaryPath) && !IsSourceNewer()) {
			return CompileResult.UpToDate;
		}
		RunShell("swiftc -O '" + SourcePath + "' -o '" + BinaryPath + "'");
		if (!_fileSystem.ExistsFile(BinaryPath)) {
			_logger.WriteWarning("Could not compile the clio menu bar app; skipping menu bar installation.");
			return CompileResult.Failed;
		}
		return CompileResult.Compiled;
	}

	private enum CompileResult {
		Compiled,
		UpToDate,
		Failed
	}

	private bool IsSourceNewer() {
		DateTime sourceTime = _fileSystem.GetFilesInfos(SourcePath).LastWriteTimeUtc;
		DateTime binaryTime = _fileSystem.GetFilesInfos(BinaryPath).LastWriteTimeUtc;
		return sourceTime > binaryTime;
	}

	private void WriteLaunchAgent() {
		string clioPath = ResolveClioPath();
		string agentsDirectory = _fileSystem.Combine(Home, LaunchAgentsRelativePath);
		_fileSystem.CreateDirectoryIfNotExists(agentsDirectory);
		_fileSystem.WriteAllTextToFile(LaunchAgentPath, BuildLaunchAgentPlist(clioPath));
	}

	private string ResolveClioPath() {
		string dotnetToolClio = _fileSystem.Combine(Home, ".dotnet", "tools", "clio");
		return _fileSystem.ExistsFile(dotnetToolClio) ? dotnetToolClio : "clio";
	}

	private string BuildLaunchAgentPlist(string clioPath) =>
		"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
		"<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " +
		"\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
		"<plist version=\"1.0\">\n" +
		"<dict>\n" +
		"\t<key>Label</key>\n\t<string>" + LaunchAgentLabel + "</string>\n" +
		"\t<key>ProgramArguments</key>\n\t<array>\n" +
		"\t\t<string>" + BinaryPath + "</string>\n" +
		"\t\t<string>" + clioPath + "</string>\n" +
		"\t</array>\n" +
		"\t<key>RunAtLoad</key>\n\t<true/>\n" +
		"\t<key>ProcessType</key>\n\t<string>Interactive</string>\n" +
		"</dict>\n</plist>\n";

	private void LoadLaunchAgent() {
		RunShell("launchctl unload '" + LaunchAgentPath + "' 2>/dev/null; " +
			"launchctl load '" + LaunchAgentPath + "'");
	}

	private void UnloadLaunchAgent() =>
		RunShell("launchctl unload '" + LaunchAgentPath + "' 2>/dev/null");

	private string RunShell(string script) =>
		_processExecutor.Execute("/bin/bash", "-c \"" + script + "\"", waitForExit: true,
			suppressErrors: true);

}
