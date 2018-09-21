using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using CommandLine;

namespace bpmcli
{

	class Program
	{
		private static string _userName;
		private static string _userPassword;
		private static string _url; // Необходимо получить из конфига
		private static string LoginUrl => _url + @"/ServiceModel/AuthService.svc/Login";
		private static string ExecutorUrl => _url + @"/0/IDE/ExecuteScript";
		private static string UnloadAppDomainUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
		private static string DownloadPackageUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem";
		private static string UploadPackageUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/LoadPackagesToDB";
		private static string UploadUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private static string InstallUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/InstallPackage";
		public static CookieContainer AuthCookie = new CookieContainer();

		private static void Configure(BaseOptions options) {
			var settingsRepository = new SettingsRepository();
			var settings = settingsRepository.GetEnvironment(options.Environment);
			_url = string.IsNullOrEmpty(options.Uri) ? settings.Uri : options.Uri;
			_userName = string.IsNullOrEmpty(options.Login) ? settings.Login : options.Login;
			_userPassword = string.IsNullOrEmpty(options.Password) ? settings.Password : options.Password;
		}

		public static void Login() {
			var authRequest = HttpWebRequest.Create(LoginUrl) as HttpWebRequest;
			authRequest.Method = "POST";
			authRequest.ContentType = "application/json";
			authRequest.CookieContainer = AuthCookie;
			using (var requestStream = authRequest.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(@"{
						""UserName"":""" + _userName + @""",
						""UserPassword"":""" + _userPassword + @"""
					}");
				}
			}
			using (var response = (HttpWebResponse)authRequest.GetResponse()) {
				string authName = ".ASPXAUTH";
				string headerCookies = response.Headers["Set-Cookie"];
				string authCookeValue = GetCookieValueByName(headerCookies, authName);
				AuthCookie.Add(new Uri(_url), new Cookie(authName, authCookeValue));
			}
		}

		private static string GetCookieValueByName(string headerCookies, string name) {
			string tokens = headerCookies.Replace("HttpOnly,", string.Empty);
			string[] cookies = tokens.Split(';');
			foreach (var cookie in cookies) {
				if (cookie.Contains(name)) {
					return cookie.Split('=')[1];
				}
			}
			return string.Empty;
		}

		private static void AddCsrfToken(HttpWebRequest request) {
			var bpmcsrf = request.CookieContainer.GetCookies(new Uri(_url))["BPMCSRF"];
			if (bpmcsrf != null) {
				request.Headers.Add("BPMCSRF", bpmcsrf.Value);
			}
		}

		private static void ExecuteScript(ExecuteOptions options) {
			string filePath = options.FilePath;
			string executorType = options.ExecutorType;
			var fileContent = File.ReadAllBytes(filePath);
			string body = Convert.ToBase64String(fileContent);
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ExecutorUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(@"{
						""Body"":""" + body + @""",
						""LibraryType"":""" + executorType + @"""
					}");
				}
			}
			request.ContentType = "application/json";
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static void UnloadAppDomain() {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UnloadAppDomainUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			request.ContentType = "application/json";
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(@"{}");
				}
			}
			request.GetResponse();
		}

		private static int ConfigureEnvironment(ConfigureOptions options) {
			var repository = new SettingsRepository();
			var environment = new EnvironmentSettings() {
				Login = options.Login,
				Password = options.Password,
				Uri = options.Uri
			};
			if (!String.IsNullOrEmpty(options.ActiveEnvironment)) {
				repository.SetActiveEnvironment(options.ActiveEnvironment);
			}
			repository.ConfigureEnvironment(options.Environment, environment);
			return 0;
		}

		private static int RemoveEnvironment(RemoveOptions options) {
			var repository = new SettingsRepository();
			repository.RemoveEnvironment(options.Environment);
			return 0;
		}

		private static void DownloadPackages(string packageName) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(DownloadPackageUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write("[\"" + packageName + "\"]");
				}
			}
			request.ContentType = "application/json";
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static void Uploadpackages(string packageName) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UploadPackageUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write("[\"" + packageName + "\"]");
				}
			}
			request.ContentType = "application/json";
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static void CompressionProject(string sourcePath, string destinationPath) {
			if (File.Exists(destinationPath)) {
				File.Delete(destinationPath);
			}

			string[] files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories);
			int directoryPathLength = sourcePath.Length;
			using (Stream fileStream =
				File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Compress)) {
					foreach (string filePath in files) {
						CompressionUtilities.ZipFile(filePath, directoryPathLength, zipStream);
					}
				}
			}

		}

		private static void InstallPackage(string fileName) {
			try {
				UploadPackage(fileName);
			}
			catch (Exception) {
				return;
			}
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(InstallUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			request.ContentType = "application/json";
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write("\"" + fileName + "\"");
				}
			}
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static void UploadPackage(string fileName) {
			string boundary = DateTime.Now.Ticks.ToString("x");
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UploadUrl);
			request.ContentType = "multipart/form-data; boundary=" + boundary;
			request.Method = "POST";
			request.KeepAlive = true;
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			Stream memStream = new MemoryStream();
			var boundarybytes = Encoding.ASCII.GetBytes("\r\n--" + boundary + "\r\n");
			var endBoundaryBytes = Encoding.ASCII.GetBytes("\r\n--" + boundary + "--");
			string headerTemplate =
				"Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\"\r\n" +
				"Content-Type: application/octet-stream\r\n\r\n";
			memStream.Write(boundarybytes, 0, boundarybytes.Length);
			var header = string.Format(headerTemplate, "files", fileName);
			var headerbytes = Encoding.UTF8.GetBytes(header);
			memStream.Write(headerbytes, 0, headerbytes.Length);
			using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read)) {
				var buffer = new byte[1024];
				var bytesRead = 0;
				while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0) {
					memStream.Write(buffer, 0, bytesRead);
				}
			}
			memStream.Write(endBoundaryBytes, 0, endBoundaryBytes.Length);
			request.ContentLength = memStream.Length;
			using (Stream requestStream = request.GetRequestStream()) {
				memStream.Position = 0;
				byte[] tempBuffer = new byte[memStream.Length];
				memStream.Read(tempBuffer, 0, tempBuffer.Length);
				memStream.Close();
				requestStream.Write(tempBuffer, 0, tempBuffer.Length);
			}
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static int Execute(ExecuteOptions options) {
			Configure(options);
			Login();
			ExecuteScript(options);
			return 0;
		}

		private static int Restart(RestartOptions options) {
			Configure(options);
			Login();
			UnloadAppDomain();
			return 0;
		}

		private static int Download(DownloadOptions options) {
			Configure(options);
			Login();
			DownloadPackages(options.PackageName);
			return 0;
		}

		private static int Upload(UploadOptions options) {
			Configure(options);
			Login();
			Uploadpackages(options.PackageName);
			return 0;
		}

		private static int Compression(CompressionOptions options) {
			CompressionProject(options.SourcePath, options.DestinationPath);
			return 0;
		}

		private static int Install(InstallOptions options) {
			Configure(options);
			Login();
			InstallPackage(options.FilePath);
			return 0;
		}

		private static int Main(string[] args) {
			return Parser.Default.ParseArguments<ExecuteOptions, RestartOptions, DownloadOptions, 
				UploadOptions, ConfigureOptions, RemoveOptions, CompressionOptions, InstallOptions>(args)
				.MapResult(
					(ExecuteOptions opts) => Execute(opts),
					(RestartOptions opts) => Restart(opts),
					(DownloadOptions opts) => Download(opts),
					(UploadOptions opts) => Upload(opts),
					(ConfigureOptions opts) => ConfigureEnvironment(opts),
					(RemoveOptions opts) => RemoveEnvironment(opts),
					(CompressionOptions opts) => Compression(opts),
					(InstallOptions opts) => Install(opts),
					errs => 1);
		}
	}
}