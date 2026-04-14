using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Clio.Common;
using Newtonsoft.Json.Linq;

namespace Clio;

public class AppUpdater(ILogger logger) : IAppUpdater {


	#region Properties: Private

	private const string  LastVersionUrl = "https://api.github.com/repos/Advance-Technologies-Foundation/clio/releases/latest";
	private static readonly Regex SemanticVersionRegex =
		new(@"\b(?<version>\d+\.\d+\.\d+\.\d+)(?:\+[0-9A-Za-z\.-]+)?\b", RegexOptions.Compiled);

	#endregion

	#region Properties: Public

	public bool Checked { get; private set; }

	#endregion

	#region Methods: Private

	private async Task<string> GetLatestPackageVersionAsync(string packageName){
		string searchUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLower()}/index.json";

		try {
			using HttpClient client = new();
			HttpResponseMessage response = await client.GetAsync(searchUrl);
			response.EnsureSuccessStatusCode();

			string responseBody = await response.Content.ReadAsStringAsync();
			JObject data = JObject.Parse(responseBody);

			// Extracting the latest version from the response
			string latestVersion = data["versions"].Last.ToString();

			return latestVersion;
		} catch (HttpRequestException e) {
			logger.WriteError($"Error fetching data: {e.Message}");
			return null;
		}
	}

	private void ShowNugetUpdateMessage(){
		logger.WriteWarning("You can update the package via the \'dotnet tool update clio -g\' command.");
	}

	#endregion

	#region Methods: Public

	public void CheckUpdate(){
		Checked = true;
		string currentVersion = GetCurrentVersion();
		string latestVersion = GetLatestVersionFromNuget();
		if (currentVersion != latestVersion) {
			logger.WriteInfo(
				$"You are using clio version {currentVersion}, however version {latestVersion} is available.");
			ShowNugetUpdateMessage();
		}
	}

	public string GetCurrentVersion(){
		Assembly assembly = Assembly.GetExecutingAssembly();
		FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
		return fileVersionInfo.FileVersion;
	}

	public string GetLatestVersionFromGitHub(){
		Task<byte[]> body;
		using (HttpClient client = new()) {
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
			using (HttpResponseMessage response = client.GetAsync(LastVersionUrl).Result) {
				response.EnsureSuccessStatusCode();
				body = response.Content.ReadAsByteArrayAsync();
			}
		}
		MemoryStream jsonStream = new(body.Result) {Position = 0};
		using StreamReader reader = new(jsonStream, Encoding.UTF8);
		string json = reader.ReadToEnd();
		JsonObject jsonDoc = (JsonObject)JsonNode.Parse(json);
		JsonNode version = jsonDoc["tag_name"];
		return version.ToString();
	}

	public string GetLatestVersionFromNuget(){
		return GetLatestPackageVersionAsync("clio").GetAwaiter().GetResult();
	}

	/// <inheritdoc/>
	public async Task<bool> IsUpdateAvailableAsync(){
		try {
			string currentVersion = GetCurrentVersion();
			string latestVersion = GetLatestVersionFromNuget();

			if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(latestVersion)) {
				return false;
			}

			return CompareVersions(currentVersion, latestVersion) < 0;
		} catch (Exception e) {
			logger.WriteError($"Error checking for updates: {e.Message}");
			return false;
		}
	}

	/// <inheritdoc/>
	public async Task<int> ExecuteUpdateAsync(bool global = true){
		try {
			string arguments = "tool update clio";
			if (global) {
				arguments += " -g";
			}

			ProcessStartInfo psi = new ProcessStartInfo {
				FileName = "dotnet",
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using (Process process = Process.Start(psi)) {
				if (process == null) {
					logger.WriteError("Failed to start dotnet tool update process");
					return 1;
				}

				string output = await process.StandardOutput.ReadToEndAsync();
				string error = await process.StandardError.ReadToEndAsync();

				process.WaitForExit();

				if (process.ExitCode != 0) {
					logger.WriteError($"Update failed: {error}");
					return 1;
				}

				logger.WriteDebug(output.Trim());
				return 0;
			}
		} catch (Exception e) {
			logger.WriteError($"Error executing update: {e.Message}");
			return 1;
		}
	}

	/// <inheritdoc/>
	public async Task<bool> VerifyInstallationAsync(string expectedVersion){
		try {
			ProcessStartInfo psi = new ProcessStartInfo {
				FileName = "clio",
				Arguments = "info --clio",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using (Process process = Process.Start(psi)) {
				if (process == null) {
					logger.WriteError("Failed to verify clio installation");
					return false;
				}

				string output = await process.StandardOutput.ReadToEndAsync();
				string error = await process.StandardError.ReadToEndAsync();
				process.WaitForExit();

				if (process.ExitCode != 0) {
					logger.WriteError($"Verification command failed: {error.Trim()}");
					return false;
				}

				string installedVersion = NormalizeInstalledVersion(output, error);
				bool isVerified = installedVersion.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase);

				if (!isVerified) {
					logger.WriteWarning($"Version mismatch: expected {expectedVersion}, got {installedVersion}");
				}

				return isVerified;
			}
		} catch (Exception e) {
			logger.WriteError($"Error verifying installation: {e.Message}");
			return false;
		}
	}

	/// <inheritdoc/>
	public string GetUpdateType(string currentVersion, string latestVersion) {
		try {
			var current = new Version(currentVersion);
			var latest = new Version(latestVersion);
			if (latest.Major != current.Major) return "MAJOR";
			if (latest.Minor != current.Minor) return "minor";
			if (latest.Build != current.Build) return "patch";
			return "build";
		} catch {
			return "update";
		}
	}

	/// <inheritdoc/>
	public async Task<string> GetReleaseNotesAsync(string version) {
		try {
			using HttpClient client = new();
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			client.DefaultRequestHeaders.UserAgent.TryParseAdd("clio");

			string url = $"https://api.github.com/repos/Advance-Technologies-Foundation/clio/releases/tags/{version}";
			using HttpResponseMessage response = await client.GetAsync(url);

			if (!response.IsSuccessStatusCode) {
				return null;
			}

			string json = await response.Content.ReadAsStringAsync();
			JsonObject jsonDoc = (JsonObject)JsonNode.Parse(json);
			string body = jsonDoc["body"]?.ToString();
			return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
		} catch {
			return null;
		}
	}

	/// <summary>
	/// Compares two version strings.
	/// </summary>
	/// <param name="version1">First version string</param>
	/// <param name="version2">Second version string</param>
	/// <returns>Negative if version1 &lt; version2, 0 if equal, positive if version1 &gt; version2</returns>
	private int CompareVersions(string version1, string version2){
		try {
			var v1 = new Version(version1);
			var v2 = new Version(version2);
			return v1.CompareTo(v2);
		} catch {
			// Fallback to string comparison if version parsing fails
			return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
		}
	}

	internal static string NormalizeInstalledVersion(string standardOutput, string standardError = ""){
		string combinedOutput = $"{standardOutput}{Environment.NewLine}{standardError}".Trim();
		if (string.IsNullOrWhiteSpace(combinedOutput)) {
			return string.Empty;
		}

		string[] lines = combinedOutput
			.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string rawLine in lines) {
			if (TryExtractSemanticVersion(rawLine.Trim(), out string normalizedVersion)) {
				return normalizedVersion;
			}
		}

		return string.Empty;
	}

	private static bool TryExtractSemanticVersion(string line, out string normalizedVersion) {
		normalizedVersion = string.Empty;
		if (string.IsNullOrWhiteSpace(line)) {
			return false;
		}

		string candidate = line;
		int infoPrefixIndex = candidate.IndexOf("clio:", StringComparison.OrdinalIgnoreCase);
		if (infoPrefixIndex > 0) {
			candidate = candidate[infoPrefixIndex..];
		} else {
			int barePrefixIndex = candidate.IndexOf("clio ", StringComparison.OrdinalIgnoreCase);
			if (barePrefixIndex > 0) {
				candidate = candidate[barePrefixIndex..];
			}
		}

		Match match = SemanticVersionRegex.Match(candidate);
		if (!match.Success) {
			return false;
		}

		normalizedVersion = match.Groups["version"].Value;
		return true;
	}

	#endregion

}

public interface IAppUpdater {

	#region Properties: Public

	bool Checked { get; }

	#endregion

	#region Methods: Public

	void CheckUpdate();

	string GetCurrentVersion();

	string GetLatestVersionFromGitHub();

	string GetLatestVersionFromNuget();

	/// <summary>
	/// Checks if an update is available for clio by comparing current version with latest on NuGet.
	/// </summary>
	/// <returns>True if newer version is available, false if already on latest or on error</returns>
	Task<bool> IsUpdateAvailableAsync();

	/// <summary>
	/// Executes the dotnet tool update command to update clio.
	/// </summary>
	/// <param name="global">If true, installs globally (-g flag). Default: true</param>
	/// <returns>Exit code: 0 on success, 1 on failure</returns>
	Task<int> ExecuteUpdateAsync(bool global = true);

	/// <summary>
	/// Verifies that the installed clio version matches the expected version.
	/// </summary>
	/// <param name="expectedVersion">Expected version string to verify</param>
	/// <returns>True if verification successful, false otherwise</returns>
	Task<bool> VerifyInstallationAsync(string expectedVersion);

	/// <summary>
	/// Determines the type of version change (MAJOR, minor, patch, build).
	/// </summary>
	string GetUpdateType(string currentVersion, string latestVersion);

	/// <summary>
	/// Fetches release notes from GitHub for the specified version tag.
	/// </summary>
	/// <returns>Release notes body or null if not available</returns>
	Task<string> GetReleaseNotesAsync(string version);

	#endregion

}
