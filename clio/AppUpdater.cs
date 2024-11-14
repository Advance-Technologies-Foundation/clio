using System.Diagnostics;
using System.IO;
using System.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Clio.Common;
using Newtonsoft.Json.Linq;

namespace Clio;

public class AppUpdater(ILogger logger) : IAppUpdater {


	#region Properties: Private

	private const string  LastVersionUrl = "https://api.github.com/repos/Advance-Technologies-Foundation/clio/releases/latest";

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
		JsonObject jsonDoc = (JsonObject)JsonValue.Parse(json);
		JsonValue version = jsonDoc["tag_name"];
		return version;
	}

	public string GetLatestVersionFromNuget(){
		return GetLatestPackageVersionAsync("clio").GetAwaiter().GetResult();
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

	#endregion

}