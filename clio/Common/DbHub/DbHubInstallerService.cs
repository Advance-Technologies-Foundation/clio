using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Clio.Common.DbHub;

/// <summary>Installs, adopts, or repairs the local dbHub HTTP server.</summary>
public interface IDbHubInstallerService {
	/// <summary>Performs an idempotent Windows-first installation or repair.</summary>
	DbHubInstallationResult InstallOrRepair(DbHubInstallRequest request);
}

/// <summary>Creates or repairs the current-user dbHub logon task.</summary>
public interface IDbHubScheduledTaskManager {
	/// <summary>Checks whether an existing clio-owned task has the exact requested process identity.</summary>
	bool IsCompatible(string nodePath, string dbHubEntryPath, DbHubSettings settings);

	/// <summary>Stops the adopted task before repair so listener ownership can be proven.</summary>
	bool StopIfExists(out string error);

	/// <summary>Ensures the task uses the supplied executable and settings, then starts it.</summary>
	bool EnsureAndStart(string nodePath, string dbHubEntryPath, DbHubSettings settings, out string error);
}

/// <inheritdoc />
public sealed class DbHubInstallerService(
	IProcessExecutor processExecutor,
	IDbHubScheduledTaskManager scheduledTaskManager,
	IDbHubHttpClient httpClient,
	IDbHubTomlStore tomlStore) : IDbHubInstallerService {
	/// <summary>Exact dbHub version installed by this clio release.</summary>
	public const string PinnedDbHubVersion = "0.23.0";
	private static readonly Version MinimumNodeVersion = new(22, 5);
	private readonly IProcessExecutor _processExecutor = processExecutor;
	private readonly IDbHubScheduledTaskManager _scheduledTaskManager = scheduledTaskManager;
	private readonly IDbHubHttpClient _httpClient = httpClient;
	private readonly IDbHubTomlStore _tomlStore = tomlStore;

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
		DbHubSyncResult preflight = _tomlStore.ValidateForInstallation(settings.ConfigPath);
		if (preflight.Warning is not null) {
			return Failure(preflight.Warning.Detail ?? "The dbHub TOML file could not be validated.");
		}

		ProcessExecutionResult node = Run("node", "--version");
		if (!IsSupportedNode(node)) {
			return Failure("Node.js 22.5 or later is required. Install it and ensure 'node' is on PATH.");
		}
		ProcessExecutionResult npm = RunNpm("--version");
		if (!Succeeded(npm)) {
			return Failure("npm is required. Install npm and ensure it is on PATH.");
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
		DbHubVerificationResult currentServer = _httpClient.VerifyServer(settings);
		if (currentServer.Online && HasUnsafeListener(request.Port)) {
			return Failure("An existing dbHub server is exposed beyond loopback. Stop it, then rerun install-dbhub so clio can repair it safely.");
		}
		if (currentServer.Verified && !_scheduledTaskManager.IsCompatible(nodePath, entryPath, settings)) {
			return Failure("A healthy dbHub listener is not owned by the expected clio Scheduled Task. Stop it before installing or repairing dbHub.");
		}
		if (!currentServer.Verified && IsPortInUse(request.Port)) {
			return Failure($"Port {request.Port} is already in use by another process. Choose another --port.");
		}

		ProcessExecutionResult installed = RunNpm("list -g @bytebase/dbhub --depth=0 --json");
		bool packageChanged = !IsPinnedInstallation(installed);
		if (packageChanged) {
			ProcessExecutionResult install = RunNpm($"install -g @bytebase/dbhub@{PinnedDbHubVersion}",
				TimeSpan.FromMinutes(5));
			if (!Succeeded(install)) {
				return Failure($"dbHub {PinnedDbHubVersion} could not be installed with npm.");
			}
		}

		if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(entryPath)) {
			return Failure("The installed dbHub package entry point could not be located.");
		}

		DbHubSyncResult runnable = _tomlStore.EnsureRunnable(settings.ConfigPath);
		if (runnable.Warning is not null) {
			return Failure(runnable.Warning.Detail ?? "The dbHub TOML file could not be prepared.");
		}
		if (currentServer.Verified && !packageChanged && !runnable.Changed) {
			DbHubVerificationResult adopted = _httpClient.VerifyServer(settings);
			return adopted.Verified && !HasUnsafeListener(request.Port)
				? Success(settings)
				: Failure("The adopted dbHub endpoint did not remain healthy exclusively on loopback.");
		}
		if (currentServer.Verified) {
			if (!_scheduledTaskManager.StopIfExists(out string stopError)) {
				return Failure(stopError);
			}
			if (!WaitForPortAvailable(request.Port)) {
				return Failure("The healthy listener remained active after the owned clio task stopped. Stop the unowned process before repairing dbHub.");
			}
		}

		if (!_scheduledTaskManager.EnsureAndStart(nodePath, entryPath, settings, out string taskError)) {
			return Failure(taskError);
		}

		DbHubVerificationResult verification = WaitForServer(settings);
		if (verification.Verified && !HasUnsafeListener(request.Port)) {
			return Success(settings);
		}
		_ = _scheduledTaskManager.StopIfExists(out _);
		return Failure("dbHub was installed, but its endpoint did not become healthy exclusively on loopback.");
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

	private static bool WaitForPortAvailable(int port) {
		for (int attempt = 0; attempt < 20; attempt++) {
			if (!IsPortInUse(port)) {
				return true;
			}
			Thread.Sleep(100);
		}
		return false;
	}

	private static bool HasUnsafeListener(int port) => IPGlobalProperties.GetIPGlobalProperties()
		.GetActiveTcpListeners().Any(endpoint => endpoint.Port == port
			&& !IPAddress.IsLoopback(endpoint.Address));

	private static DbHubInstallationResult Failure(string message) => new(false, message);

	private static DbHubInstallationResult Success(DbHubSettings settings) => new(true,
		$"dbHub is installed and healthy at http://{settings.Host}:{settings.Port}/mcp.", settings);
}

/// <inheritdoc />
public sealed class DbHubScheduledTaskManager(IProcessExecutor processExecutor,
	IDbHubAtomicFileWriter atomicFileWriter) : IDbHubScheduledTaskManager {
	internal const string TaskName = "Clio dbHub MCP Server";
	internal const string LegacyTaskName = "Start dbHub MCP Server";
	private readonly IProcessExecutor _processExecutor = processExecutor;
	private readonly IDbHubAtomicFileWriter _atomicFileWriter = atomicFileWriter;

	/// <inheritdoc />
	public bool IsCompatible(string nodePath, string dbHubEntryPath, DbHubSettings settings) {
		try {
			bool legacyExists = TaskExists(LegacyTaskName);
			bool canonicalExists = TaskExists(TaskName);
			if (legacyExists == canonicalExists) {
				return false;
			}
			(string taskName, _) = SelectTaskNames(legacyExists, canonicalExists);
			ProcessExecutionResult query = Run($"/Query /TN {Quote(taskName)} /XML");
			if (!Succeeded(query) || string.IsNullOrWhiteSpace(query.StandardOutput)) {
				return false;
			}
			XDocument actual = XDocument.Parse(query.StandardOutput);
			XDocument expected = CreateTaskDocument(nodePath, dbHubEntryPath, settings);
			return IsTaskDocumentCompatible(actual, expected) && IsTaskRunning(taskName);
		}
		catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException
			or PlatformNotSupportedException) {
			return false;
		}
	}

	/// <inheritdoc />
	public bool StopIfExists(out string error) {
		error = null;
		try {
			bool legacyExists = TaskExists(LegacyTaskName);
			bool canonicalExists = TaskExists(TaskName);
			if (!legacyExists && !canonicalExists) {
				return true;
			}
			(string taskName, _) = SelectTaskNames(legacyExists, canonicalExists);
			if (!Succeeded(Run($"/End /TN {Quote(taskName)}"))) {
				error = "The current-user dbHub Scheduled Task could not be stopped for safe repair.";
				return false;
			}
			return true;
		}
		catch (Exception exception) when (exception is PlatformNotSupportedException) {
			error = "The current-user dbHub Scheduled Task could not be inspected safely.";
			return false;
		}
	}

	/// <inheritdoc />
	public bool EnsureAndStart(string nodePath, string dbHubEntryPath, DbHubSettings settings, out string error) {
		error = null;
		try {
			bool legacyExists = TaskExists(LegacyTaskName);
			bool canonicalExists = TaskExists(TaskName);
			(string taskName, string redundantTaskName) = SelectTaskNames(legacyExists, canonicalExists);
			if (legacyExists || canonicalExists) {
				_ = Run($"/End /TN {Quote(taskName)}");
			}
			string taskDirectory = Path.Combine(SettingsRepository.AppSettingsFolderPath, "dbhub");
			Directory.CreateDirectory(taskDirectory);
			string xmlPath = Path.Combine(taskDirectory, "dbhub-task.xml");
			XDocument document = CreateTaskDocument(nodePath, dbHubEntryPath, settings);
			_atomicFileWriter.Commit(xmlPath, document.ToString(SaveOptions.DisableFormatting));
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
			ProcessExecutionResult start = Run($"/Run /TN {Quote(taskName)}");
			if (!Succeeded(start)) {
				error = "The current-user dbHub Scheduled Task was repaired but could not be started.";
				return false;
			}
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

	internal static bool IsTaskDocumentCompatible(XDocument actual, XDocument expected) {
		XNamespace ns = expected.Root?.Name.Namespace ?? XNamespace.None;
		XElement actualTrigger = SingleChild(SingleChild(actual.Root, ns + "Triggers"), ns + "LogonTrigger");
		XElement expectedTrigger = SingleChild(SingleChild(expected.Root, ns + "Triggers"), ns + "LogonTrigger");
		XElement actualPrincipal = SingleChild(SingleChild(actual.Root, ns + "Principals"), ns + "Principal");
		XElement expectedPrincipal = SingleChild(SingleChild(expected.Root, ns + "Principals"), ns + "Principal");
		XElement actualSettings = SingleChild(actual.Root, ns + "Settings");
		XElement expectedSettings = SingleChild(expected.Root, ns + "Settings");
		XElement actualExec = SingleChild(SingleChild(actual.Root, ns + "Actions"), ns + "Exec");
		XElement expectedExec = SingleChild(SingleChild(expected.Root, ns + "Actions"), ns + "Exec");
		if (actualTrigger is null || expectedTrigger is null || actualPrincipal is null || expectedPrincipal is null
			|| actualSettings is null || expectedSettings is null || actualExec is null || expectedExec is null) {
			return false;
		}
		return ValuesMatch(actualTrigger, expectedTrigger, ns + "Enabled", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualTrigger, expectedTrigger, ns + "UserId", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualPrincipal, expectedPrincipal, ns + "UserId", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualPrincipal, expectedPrincipal, ns + "LogonType", StringComparison.Ordinal)
			&& ValuesMatch(actualPrincipal, expectedPrincipal, ns + "RunLevel", StringComparison.Ordinal)
			&& ValuesMatch(actualSettings, expectedSettings, ns + "MultipleInstancesPolicy", StringComparison.Ordinal)
			&& ValuesMatch(actualSettings, expectedSettings, ns + "DisallowStartIfOnBatteries", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualSettings, expectedSettings, ns + "StopIfGoingOnBatteries", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualSettings, expectedSettings, ns + "StartWhenAvailable", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualSettings, expectedSettings, ns + "Hidden", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualSettings, expectedSettings, ns + "ExecutionTimeLimit", StringComparison.Ordinal)
			&& ValuesMatch(actualExec, expectedExec, ns + "Command", StringComparison.OrdinalIgnoreCase)
			&& ValuesMatch(actualExec, expectedExec, ns + "Arguments", StringComparison.Ordinal);
	}

	private bool TaskExists(string taskName) => Succeeded(Run($"/Query /TN {Quote(taskName)}"));

	private bool IsTaskRunning(string taskName) {
		string script = "$service=New-Object -ComObject 'Schedule.Service';$service.Connect();"
			+ $"$service.GetFolder('\\').GetTask('{taskName.Replace("'", "''")}').State";
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions(
			"powershell.exe", $"-NoProfile -NonInteractive -Command {Quote(script)}") {
			Timeout = TimeSpan.FromSeconds(30), SuppressErrors = true
		}).GetAwaiter().GetResult();
		return Succeeded(result) && string.Equals(result.StandardOutput?.Trim(), "4", StringComparison.Ordinal);
	}

	private static XElement SingleChild(XElement parent, XName name) => parent?.Elements(name).SingleOrDefault();

	private static bool ValuesMatch(XElement actual, XElement expected, XName name,
		StringComparison comparison) => string.Equals(SingleChild(actual, name)?.Value,
		SingleChild(expected, name)?.Value, comparison);

	private ProcessExecutionResult Run(string arguments) => _processExecutor.ExecuteAndCaptureAsync(
		new ProcessExecutionOptions("schtasks.exe", arguments) { Timeout = TimeSpan.FromSeconds(30), SuppressErrors = true })
		.GetAwaiter().GetResult();

	private static bool Succeeded(ProcessExecutionResult result) => result is { Started: true, ExitCode: 0 };

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
