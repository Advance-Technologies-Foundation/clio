using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Tomlyn;

namespace Clio.Common.DbHub;

/// <summary>Installs, adopts, or repairs the local dbHub HTTP server.</summary>
public interface IDbHubInstallerService {
	/// <summary>Performs an idempotent Windows-first installation or repair.</summary>
	DbHubInstallationResult InstallOrRepair(DbHubInstallRequest request);
}

/// <summary>Creates or repairs the current-user dbHub logon task.</summary>
public interface IDbHubScheduledTaskManager {
	/// <summary>Ensures the task uses the supplied executable and settings, then starts it.</summary>
	bool EnsureAndStart(string nodePath, string dbHubEntryPath, DbHubSettings settings, out string error);
}

/// <inheritdoc />
public sealed class DbHubInstallerService(
	IProcessExecutor processExecutor,
	IDbHubScheduledTaskManager scheduledTaskManager,
	IDbHubHttpClient httpClient,
	IDbHubAtomicFileWriter atomicFileWriter) : IDbHubInstallerService {
	/// <summary>Exact dbHub version installed by this clio release.</summary>
	public const string PinnedDbHubVersion = "0.23.0";
	private static readonly Version MinimumNodeVersion = new(22, 5);
	private readonly IProcessExecutor _processExecutor = processExecutor;
	private readonly IDbHubScheduledTaskManager _scheduledTaskManager = scheduledTaskManager;
	private readonly IDbHubHttpClient _httpClient = httpClient;
	private readonly IDbHubAtomicFileWriter _atomicFileWriter = atomicFileWriter;

	/// <inheritdoc />
	public DbHubInstallationResult InstallOrRepair(DbHubInstallRequest request) {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return Failure("install-dbhub currently supports Windows only.");
		}
		if (!string.Equals(request.Host, DbHubSettings.DefaultHost, StringComparison.Ordinal)) {
			return Failure("dbHub must bind to 127.0.0.1 because its HTTP service is unauthenticated.");
		}
		if (request.Port is < 1 or > 65535) {
			return Failure("The dbHub port must be between 1 and 65535.");
		}

		ProcessExecutionResult node = Run("node", "--version");
		if (!IsSupportedNode(node)) {
			return Failure("Node.js 22.5 or later is required. Install it and ensure 'node' is on PATH.");
		}
		ProcessExecutionResult npm = RunNpm("--version");
		if (!Succeeded(npm)) {
			return Failure("npm is required. Install npm and ensure it is on PATH.");
		}

		DbHubSettings settings;
		try {
			settings = new DbHubSettings {
				Enabled = true,
				ConfigPath = Path.GetFullPath(request.ConfigPath),
				Host = request.Host,
				Port = request.Port,
				SyncLocalEnvironments = request.SyncLocalEnvironments
			};
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException) {
			return Failure("The dbHub config path is invalid.");
		}
		DbHubVerificationResult currentServer = _httpClient.VerifyServer(settings);
		if (currentServer.Verified && HasWildcardListener(request.Port)) {
			return Failure("An existing dbHub server is exposed beyond loopback. Stop it, then rerun install-dbhub so clio can repair it safely.");
		}
		if (!currentServer.Online && IsPortInUse(request.Port)) {
			return Failure($"Port {request.Port} is already in use by another process. Choose another --port.");
		}

		ProcessExecutionResult installed = RunNpm("list -g @bytebase/dbhub --depth=0 --json");
		if (!IsPinnedInstallation(installed)) {
			ProcessExecutionResult install = RunNpm($"install -g @bytebase/dbhub@{PinnedDbHubVersion}",
				TimeSpan.FromMinutes(5));
			if (!Succeeded(install)) {
				return Failure($"dbHub {PinnedDbHubVersion} could not be installed with npm.");
			}
		}

		ProcessExecutionResult prefixResult = RunNpm("prefix -g");
		ProcessExecutionResult nodePathResult = Run("where.exe", "node.exe");
		if (!Succeeded(prefixResult) || !Succeeded(nodePathResult)) {
			return Failure("The installed dbHub or Node.js executable could not be located.");
		}
		string prefix = prefixResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault()?.Trim();
		string nodePath = nodePathResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault()?.Trim();
		string entryPath = Path.Combine(prefix ?? string.Empty, "node_modules", "@bytebase", "dbhub", "dist", "index.js");
		if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(entryPath)) {
			return Failure("The installed dbHub package entry point could not be located.");
		}

		if (!EnsureConfigFile(settings.ConfigPath, out string configError)) {
			return Failure(configError);
		}
		if (!_scheduledTaskManager.EnsureAndStart(nodePath, entryPath, settings, out string taskError)) {
			return Failure(taskError);
		}

		DbHubVerificationResult verification = currentServer.Verified ? currentServer : WaitForServer(settings);
		return verification.Verified
			? new DbHubInstallationResult(true,
				$"dbHub is installed and healthy at http://{settings.Host}:{settings.Port}/mcp.", settings)
			: Failure("dbHub was installed, but its /healthz or MCP endpoint did not become healthy.");
	}

	private DbHubVerificationResult WaitForServer(DbHubSettings settings) {
		DbHubVerificationResult result = null;
		for (int attempt = 0; attempt < 20; attempt++) {
			result = _httpClient.VerifyServer(settings);
			if (result.Verified) {
				return result;
			}
			Thread.Sleep(500);
		}
		return result;
	}

	private bool EnsureConfigFile(string path, out string error) {
		error = null;
		try {
			FileInfo info = new(path);
			if (info.LinkTarget is not null
				|| (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))) {
				error = "The dbHub TOML path resolves through a symbolic link or reparse point and was refused.";
				return false;
			}
			if (info.Exists) {
				string content = File.ReadAllText(path, Encoding.UTF8);
				string runnable = DbHubTomlStore.EnsureRunnableContent(content);
				if (!string.Equals(content, runnable, StringComparison.Ordinal)) {
					_atomicFileWriter.Commit(path, runnable);
				}
				return true;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
			string initial = DbHubTomlStore.EnsureRunnableContent(
				"# dbHub configuration managed jointly by the user and clio.\n");
			_atomicFileWriter.Commit(path, initial);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
			or TomlException or InvalidOperationException) {
			error = "The dbHub TOML file could not be created, read, or validated.";
			return false;
		}
	}

	private ProcessExecutionResult Run(string program, string arguments, TimeSpan? timeout = null) =>
		_processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions(program, arguments) {
			Timeout = timeout ?? TimeSpan.FromSeconds(30),
			SuppressErrors = true
		}).GetAwaiter().GetResult();

	private ProcessExecutionResult RunNpm(string arguments, TimeSpan? timeout = null) =>
		Run("cmd.exe", $"/d /s /c \"npm.cmd {arguments}\"", timeout);

	private static bool IsSupportedNode(ProcessExecutionResult result) {
		string versionText = result.StandardOutput.Trim().TrimStart('v');
		return Succeeded(result) && Version.TryParse(versionText, out Version version)
			&& version >= MinimumNodeVersion;
	}

	private static bool IsPinnedInstallation(ProcessExecutionResult result) {
		if (!Succeeded(result)) {
			return false;
		}
		try {
			return string.Equals(JObject.Parse(result.StandardOutput)
				.SelectToken("dependencies['@bytebase/dbhub'].version")?.Value<string>(), PinnedDbHubVersion,
				StringComparison.Ordinal);
		}
		catch (Newtonsoft.Json.JsonException) {
			return false;
		}
	}

	private static bool Succeeded(ProcessExecutionResult result) =>
		result is { Started: true, ExitCode: 0, TimedOut: false, Canceled: false };

	private static bool IsPortInUse(int port) => IPGlobalProperties.GetIPGlobalProperties()
		.GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);

	private static bool HasWildcardListener(int port) => IPGlobalProperties.GetIPGlobalProperties()
		.GetActiveTcpListeners().Any(endpoint => endpoint.Port == port
			&& (endpoint.Address.Equals(IPAddress.Any) || endpoint.Address.Equals(IPAddress.IPv6Any)));

	private static DbHubInstallationResult Failure(string message) => new(false, message);
}

/// <inheritdoc />
public sealed class DbHubScheduledTaskManager(IProcessExecutor processExecutor) : IDbHubScheduledTaskManager {
	internal const string TaskName = "Clio dbHub MCP Server";
	internal const string LegacyTaskName = "Start dbHub MCP Server";
	private readonly IProcessExecutor _processExecutor = processExecutor;

	/// <inheritdoc />
	public bool EnsureAndStart(string nodePath, string dbHubEntryPath, DbHubSettings settings, out string error) {
		error = null;
		try {
			(string taskName, string redundantTaskName) = SelectTaskNames(
				TaskExists(LegacyTaskName), TaskExists(TaskName));
			string taskDirectory = Path.Combine(SettingsRepository.AppSettingsFolderPath, "dbhub");
			Directory.CreateDirectory(taskDirectory);
			string xmlPath = Path.Combine(taskDirectory, "dbhub-task.xml");
			XDocument document = CreateTaskDocument(nodePath, dbHubEntryPath, settings);
			File.WriteAllText(xmlPath, document.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
			ProcessExecutionResult create = Run($"/Create /TN {Quote(taskName)} /XML {Quote(xmlPath)} /F");
			if (!Succeeded(create)) {
				error = "The current-user dbHub Scheduled Task could not be created or repaired.";
				return false;
			}
			if (redundantTaskName is not null
				&& !Succeeded(Run($"/Delete /TN {Quote(redundantTaskName)} /F"))) {
				error = "A redundant clio dbHub Scheduled Task could not be removed safely.";
				return false;
			}
			_ = Run($"/Run /TN {Quote(taskName)}");
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			or PlatformNotSupportedException) {
			error = "The current-user dbHub Scheduled Task could not be prepared.";
			return false;
		}
	}

	internal static (string Active, string Redundant) SelectTaskNames(bool legacyExists, bool canonicalExists) =>
		legacyExists
			? (LegacyTaskName, canonicalExists ? TaskName : null)
			: (TaskName, null);

	internal static XDocument CreateTaskDocument(string nodePath, string dbHubEntryPath, DbHubSettings settings) {
		XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
		string sid = WindowsIdentity.GetCurrent().User?.Value
			?? throw new PlatformNotSupportedException("The current Windows user SID is unavailable.");
		string arguments = string.Join(" ", Quote(dbHubEntryPath), "--transport http", "--host",
			settings.Host, "--port", settings.Port, "--config", Quote(settings.ConfigPath));
		return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.4"),
			new XElement(ns + "RegistrationInfo", new XElement(ns + "Description",
				"Starts the local clio-managed dbHub HTTP MCP server at user logon.")),
			new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger",
				new XElement(ns + "Enabled", true), new XElement(ns + "UserId", sid))),
			new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
				new XElement(ns + "UserId", sid), new XElement(ns + "LogonType", "InteractiveToken"),
				new XElement(ns + "RunLevel", "LeastPrivilege"))),
			new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
				new XElement(ns + "DisallowStartIfOnBatteries", false),
				new XElement(ns + "StopIfGoingOnBatteries", false), new XElement(ns + "StartWhenAvailable", true),
				new XElement(ns + "Hidden", true), new XElement(ns + "ExecutionTimeLimit", "PT0S")),
			new XElement(ns + "Actions", new XAttribute("Context", "Author"), new XElement(ns + "Exec",
				new XElement(ns + "Command", nodePath), new XElement(ns + "Arguments", arguments)))));
	}

	private bool TaskExists(string taskName) => Succeeded(Run($"/Query /TN {Quote(taskName)}"));

	private ProcessExecutionResult Run(string arguments) => _processExecutor.ExecuteAndCaptureAsync(
		new ProcessExecutionOptions("schtasks.exe", arguments) { Timeout = TimeSpan.FromSeconds(30), SuppressErrors = true })
		.GetAwaiter().GetResult();

	private static bool Succeeded(ProcessExecutionResult result) => result is { Started: true, ExitCode: 0 };

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
