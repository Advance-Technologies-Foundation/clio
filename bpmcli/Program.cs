using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using bpmcli.environment;
using CommandLine;

namespace bpmcli
{

	public class StringParser {
		public static IEnumerable<string> ParseArray(string input) {
			return input.Split(',').Select(p => p.Trim()).ToList();
		}
	}

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
		private static string LogUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/GetLogFile";
		private static string SelectQueryUrl => _url + @"/0/DataService/json/SyncReply/SelectQuery";
		private static string UninstallAppUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/DeletePackage";
		private static string ClearRedisDbUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/ClearRedisDb";
		private static string GetZipPackageUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/GetZipPackages";
		public static CookieContainer AuthCookie = new CookieContainer();

		private static string CurrentProj => 
			new DirectoryInfo(Environment.CurrentDirectory).GetFiles("*.csproj").FirstOrDefault()?.FullName;

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

		private static void ClearRedisDbInternal() {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ClearRedisDbUrl);
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
			Console.WriteLine("Done");
		}

		private static int ConfigureEnvironment(ConfigureOptions options) {
			var repository = new SettingsRepository();
			var environment = new EnvironmentSettings() {
				Login = options.Login,
				Password = options.Password,
				Uri = options.Uri,
				Maintainer = options.Maintainer
			};
			if (!String.IsNullOrEmpty(options.ActiveEnvironment)) {
				repository.SetActiveEnvironment(options.ActiveEnvironment);
			}
			repository.ConfigureEnvironment(options.Environment, environment);
			return 0;
		}

		private static int ViewEnvironments() {
			var repository = new SettingsRepository();
			repository.ShowSettingsTo(Console.Out);
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
			//Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(packageName + " - " + responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static void UploadPackages(string packageName) {
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
			//Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Console.WriteLine(packageName + " - " + responseFromServer);
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static void CompressionProjects(string sourcePath, string destinationPath, IEnumerable<string> names) {
			string tempPath = Path.Combine(Path.GetTempPath(), "Application_");// + DateTime.Now.ToShortDateString());
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
			Directory.CreateDirectory(tempPath);
			foreach (var name in names) {
				var currentSourcePath = Path.Combine(sourcePath, name);
				var currentDestinationPath = Path.Combine(tempPath, name + ".gz");
				CompressionProject(currentSourcePath, currentDestinationPath);
			}
			ZipFile.CreateFromDirectory(tempPath, destinationPath);
		}

		internal static IEnumerable<string> GetPackages(string inputline) {
			return StringParser.ParseArray(inputline);
		}		
		
		private static void CompressionProject(string sourcePath, string destinationPath) {
			if (File.Exists(destinationPath)) {
				File.Delete(destinationPath);
			}
			string tempPath = CreateTempPath(sourcePath);
			CopyProjectFiles(sourcePath, tempPath);

			string[] files = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories);
			int directoryPathLength = tempPath.Length;
			using (Stream fileStream =
				File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Compress)) {
					foreach (string filePath in files) {
						CompressionUtilities.ZipFile(filePath, directoryPathLength, zipStream);
					}
				}
			}
			Directory.Delete(tempPath, true);
		}

		private static string CreateTempPath(string sourcePath) {
			var directoryInfo = new DirectoryInfo(sourcePath);
			string tempPath = Path.Combine(Path.GetTempPath(), directoryInfo.Name);
			return tempPath;
		}

		private static void CopyProjectFiles(string sourcePath, string destinationPath) {
			if (Directory.Exists(destinationPath)) {
				Directory.Delete(destinationPath, true);
			}
			Directory.CreateDirectory(destinationPath);
			CopyProjectElement(sourcePath, destinationPath, "Assemblies");
			CopyProjectElement(sourcePath, destinationPath, "Bin");
			CopyProjectElement(sourcePath, destinationPath, "Data");
			CopyProjectElement(sourcePath, destinationPath, "Files");
			CopyProjectElement(sourcePath, destinationPath, "Resources");
			CopyProjectElement(sourcePath, destinationPath, "Schemas");
			CopyProjectElement(sourcePath, destinationPath, "SqlScripts");
			File.Copy(Path.Combine(sourcePath, "descriptor.json"), Path.Combine(destinationPath, "descriptor.json"));
		}

		private static void DownloadZipPackagesInternal(string packageName, string destinationPath) {
			Console.WriteLine("Start download packages ({0}).", packageName);
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GetZipPackageUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			var packageNames = string.Format("\"{0}\"", packageName.Replace(" ", string.Empty).Replace(",", "\",\""));
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write("[" + packageNames + "]");
				}
			}
			request.ContentType = "application/json";
			Stream dataStream;
			WebResponse response = request.GetResponse();
			dataStream = response.GetResponseStream();
			if (dataStream != null) {
				var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
				dataStream.CopyTo(fileStream);
				fileStream.Dispose();
				dataStream.Close();
				Console.WriteLine("Download packages ({0}) completed.", packageName);
			} else {
				Console.WriteLine("Download packages ({0}) not completed.", packageName);
			}
			response.Close();
		}

		private static void UnZipPackages(string zipFilePath) {
			var fileInfo = new FileInfo(zipFilePath);
			if (fileInfo.Length == 0) {
				throw new Exception("CompressionUtilities.Exception.FileIsEmpty");
			}
			string targetDirectoryPath = GetPackagePathFromZipFile(zipFilePath, ".zip");
			if (Directory.Exists(targetDirectoryPath)) {
				Directory.Delete(targetDirectoryPath, true);
			}
			ZipFile.ExtractToDirectory(zipFilePath, targetDirectoryPath);
			foreach (var filePath in Directory.GetFiles(targetDirectoryPath)){
				string packageName = GetPackagePathFromZipFile(new FileInfo(filePath).Name, ".gz");
				string currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, packageName);
				Console.WriteLine("Start unzip package ({0}).", packageName);
				using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
					using (var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true)) {
						while (CompressionUtilities.UnzipFile(currentDirectoryPath, zipStream)) {
						}
					}
				}
				Console.WriteLine("Unzip package ({0}) completed.", packageName);
			}
		}

		private static string GetPackagePathFromZipFile(string filePath, string zipFileExtention) {
			string targetDirectoryPath = filePath.Remove(filePath.IndexOf(zipFileExtention,
				StringComparison.Ordinal), zipFileExtention.Length);
			return targetDirectoryPath;
		}

		internal static void CopyProjectElement(string sourcePath, string destinationPath, string name) {
			string fromAssembliesPath = Path.Combine(sourcePath, name);
			if (Directory.Exists(fromAssembliesPath)) {
				string toAssembliesPath = Path.Combine(destinationPath, name);
				CopyDir(fromAssembliesPath, toAssembliesPath);
			}
		}

		internal static void CopyDir(string source, string dest) {
			if (String.IsNullOrEmpty(source) || String.IsNullOrEmpty(dest))
				return;
			Directory.CreateDirectory(dest);
			foreach (string fn in Directory.GetFiles(source)) {
				File.Copy(fn, Path.Combine(dest, Path.GetFileName(fn)), true);
			}
			foreach (string dirFn in Directory.GetDirectories(source)) {
				CopyDir(dirFn, Path.Combine(dest, Path.GetFileName(dirFn)));
			}
		}

		private static void InstallPackage(string filePath) {
			string fileName = string.Empty;
			try {				
				fileName = UploadPackage(filePath);
			}
			catch (Exception e) {
				Console.WriteLine(e.Message);
				return;
			}
			Console.WriteLine("Installing...");
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(InstallUrl);
			request.Timeout = 1800000;
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
			Console.WriteLine("Installed");

		}
		
		private static void DeletePackage(string code) {
			DeleteAppById(code);
		}

		private static string GetAppId(string code) {
			string result;
			string query = "{\"rootSchemaName\":\"SysInstalledApp\",\"operationType\":0,\"filters\":{\"items\":{\"4eacef8f-252a-4054-9711-ded84c911dc7\":{\"items\":{\"CustomFilters\":{\"items\":{\"customFilterCode_SysInstalledApp\":{\"filterType\":1,\"comparisonType\":3,\"isEnabled\":true,\"trimDateTimeParameterToDate\":false,\"leftExpression\":{\"expressionType\":0,\"columnPath\":\"Code\"},\"rightExpression\":{\"expressionType\":2,\"parameter\":{\"dataValueType\":1,\"value\":\""
			               + code + "\"}}}},\"logicalOperation\":0,\"isEnabled\":true,\"filterType\":6}},\"logicalOperation\":0,\"isEnabled\":true,\"filterType\":6}},\"logicalOperation\":0,\"isEnabled\":true,\"filterType\":6},\"columns\":{\"items\":{\"Id\":{\"caption\":\"\",\"orderDirection\":0,\"orderPosition\":-1,\"isVisible\":true,\"expression\":{\"expressionType\":0,\"columnPath\":\"Id\"}}}},\"isDistinct\":false,\"rowCount\":30,\"rowsOffset\":0,\"isPageable\":true,\"allColumns\":false,\"useLocalization\":true,\"useRecordDeactivation\":false,\"serverESQCacheParameters\":{\"cacheLevel\":0,\"cacheGroup\":\"\",\"cacheItemName\":\"\"},\"queryOptimize\":false,\"useMetrics\":false,\"querySource\":0,\"ignoreDisplayValues\":false,\"conditionalValues\":null,\"isHierarchical\":false}";
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(SelectQueryUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(query);
				}
			}
			request.ContentType = "application/json";
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			Regex regex = new Regex("\"Id\":\"(.+?)\"");
			Match match = regex.Match(responseFromServer);
			if (match.Success) {
				result = match.Groups[1].Value;
			} else {
				const string message = "This code not exists.";
				Console.WriteLine(message);
				throw new Exception(message);
			}
			reader.Close();
			dataStream.Close();
			response.Close();
			return result;
		}

		private static void DeleteAppById(string id) {
			Console.WriteLine("Deleting...");
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UninstallAppUrl);
			request.Method = "POST";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write("\"" + id + "\"");
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
			Console.WriteLine("Deleted.");
		}

		private static string UploadPackage(string filePath) {
			Console.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string fileName = fileInfo.Name;
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
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read)) {
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
			Console.WriteLine("Uploaded");
			return fileName;
		}

		private static int Execute(ExecuteOptions options) {
			Configure(options);
			Login();
			ExecuteScript(options);
			return 0;
		}

		private static int Register(RegisterOptions options) {
			var bpmcliEnv = new BpmcliEnvironment();
			string path = string.IsNullOrEmpty(options.Path) ? Environment.CurrentDirectory : options.Path;
			IResult result = options.Target == "m" 
				? bpmcliEnv.MachineRegisterPath(path) 
				: bpmcliEnv.UserRegisterPath(path);
			result.ShowMessagesTo(Console.Out);
			return 1;
		}

		private static int Restart(RestartOptions options) {
			try {
				Configure(options);
				Login();
				UnloadAppDomain();
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int ClearRedisDb(RedisOptions options) {
			try {
				Configure(options);
				Login();
				ClearRedisDbInternal();
				return 0;
			}
			catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int DownloadZipPackages(DownloadZipPackagesOptions options) {
			try {
				Configure(options);
				Login();
				string destPath = options.DestPath != null
					? options.DestPath
					: Path.Combine(Path.GetTempPath(), "packages.zip");
				DownloadZipPackagesInternal(options.Packages, destPath);
					UnZipPackages(destPath);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int Compression(CompressionOptions options) {
			if (options.Packages == null) {
				CompressionProject(options.SourcePath, options.DestinationPath);
			} else {
				var packages = GetPackages(options.Packages);
				CompressionProjects(options.SourcePath, options.DestinationPath, packages);
			}
			return 0;
		}

		private static int Install(InstallOptions options) {
			Configure(options);
			Login();
			if (File.Exists(options.FilePath)) {
				InstallPackage(options.FilePath);
			} else {
				if (Directory.Exists(options.FilePath)) {
					var folderPath = options.FilePath;
					var filePath = options.FilePath + ".gz";
					CompressionProject(folderPath, filePath);
					InstallPackage(filePath);
					File.Delete(filePath);
				}
			}

			if (options.ReportPath != null) {
				SaveLogFile(options.ReportPath);
			}
			Console.WriteLine("Done");
			return 0;
		}

		private static int Delete(DeleteOptions options) {
			Configure(options);
			Login();
			DeletePackage(options.Code);
			return 0;
		}

		private static void SaveLogFile(string reportPath) {
			if (File.Exists(reportPath)) {
				File.Delete(reportPath);
			}
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LogUrl);
			request.Timeout = 100000;
			request.Method = "GET";
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			request.ContentType = "application/json";
			Stream dataStream;
			WebResponse response = request.GetResponse();
			Console.WriteLine(((HttpWebResponse)response).StatusDescription);
			dataStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(dataStream);
			string responseFromServer = reader.ReadToEnd();
			File.WriteAllText(reportPath, responseFromServer, Encoding.UTF8);
			Console.WriteLine("Logs downloaded");
			reader.Close();
			dataStream.Close();
			response.Close();
		}

		private static int Main(string[] args) {
			return Parser.Default.ParseArguments<ExecuteOptions, RestartOptions, RedisOptions, FetchOptions,
					ConfigureOptions, ViewOptions, RemoveOptions, CompressionOptions, InstallOptions,
					DeleteOptions, RebaseOptions, NewOptions, ConvertOptions, RegisterOptions,
					DownloadZipPackagesOptions>(args)
				.MapResult(
					(ExecuteOptions opts) => Execute(opts),
					(RestartOptions opts) => Restart(opts),
					(RedisOptions opts) => ClearRedisDb(opts),
					(FetchOptions opts) => Fetch(opts),
					(ConfigureOptions opts) => ConfigureEnvironment(opts),
					(ViewOptions opts) => ViewEnvironments(),
					(RemoveOptions opts) => RemoveEnvironment(opts),
					(CompressionOptions opts) => Compression(opts),
					(InstallOptions opts) => Install(opts),
					(DeleteOptions opts) => Delete(opts),
					(RebaseOptions opts) => Rebase(opts),
					(NewOptions opts) => New(opts),
					(ConvertOptions opts) => ConvertPackage(opts),
					(RegisterOptions opts) => Register(opts),
					(DownloadZipPackagesOptions opts) => DownloadZipPackages(opts),
					errs => 1);
		}

		private static int ConvertPackage(ConvertOptions opts) {
			return PackageConverter.Convert(opts);
		}

		private static int Fetch(FetchOptions opts) {
			Configure(opts);
			Login();
			var packages = GetPackages(opts.PackageNames);
			foreach (var package in packages) {
				if (opts.Operation == "load") {
					DownloadPackages(package);
				} else {
					UploadPackages(package);
				}
			}
			return 0;
		}

		private static int New(NewOptions options) {
			var settings = new SettingsRepository().GetEnvironment();
			try {
				switch (options.Template) {
					case "pkg": {
						BpmPkg.CreatePackage(options.Name, settings.Maintainer)
							.Create();
						if (bool.Parse(options.Rebase)) {
							Rebase(new RebaseOptions {ProjectType = options.Template});
						}
					}
						break;
					default: {
						throw new NotSupportedException($"You use not supported option type {options.Template}");
					}
				}
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int Rebase(RebaseOptions options) {
			options.FilePath = options.FilePath ?? CurrentProj;
			if (string.IsNullOrEmpty(options.FilePath)) {
				throw new ArgumentNullException(nameof(options.FilePath));
			}
			try {
				switch (options.ProjectType) {
					case "sln": {
						throw new NotSupportedException("option sln temporaly not supported");
					}
					case "pkg": {
						BpmPkgProject.LoadFromFile(options.FilePath)
							.RebaseToCoreDebug()
							.SaveChanges();
					}
						break;
					default: {
						throw new NotSupportedException($"You use not supported option type {options.ProjectType}");
					}
				}
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
