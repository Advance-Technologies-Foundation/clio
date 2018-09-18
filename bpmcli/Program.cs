using System;
using System.IO;
using System.Net;
using CommandLine;

namespace bpmcli
{

	class Program
    {
        private static string Url; // Необходимо получить из конфига
        private static string LoginUrl => Url + @"/ServiceModel/AuthService.svc/Login";
        private static string ExecutorUrl => Url + @"/0/IDE/ExecuteScript";
        private static string UnloadAppDomainUrl => Url + @"/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
        private static string DownloadPackageUrl => Url + @"/0/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem";
        private static string UploadPackageUrl => Url + @"/0/ServiceModel/AppInstallerService.svc/LoadPackagesToDB";
        public static CookieContainer AuthCookie = new CookieContainer();

	    //   public class Options {
		//    [Option("Uri", Required = true)]
		//    public string Uri  { get; set; }
		//	[Option("Login", Required = true)]
		//	public string Login { get; set; }
		//    [Option("Password", Required = true)]
		//	public string Password { get; set; }
		//    [Option("Operation", Required = true)]
		//	public string Operation { get; set; }
		//    [Option("PackageName", Required = true)]
		//	public string PackageName { get; set; }
		//	[Option("FilePath", Required = true)]
		//	public string FilePath { get; set; }
		//	[Option("ExecutorType", Required = true)]
		//	public string ExecutorType { get; set; }
		//}

	    [Verb("Execute", HelpText = "Execute assembly.")]
	    class ExecuteOptions {
			[Option("FilePath", Required = true)]
			public string FilePath { get; set; }
			[Option("ExecutorType", Required = true)]
			public string ExecutorType { get; set; }
	    }
		[Verb("Restart", HelpText = "Restart application.")]
	    class RestartOptions {

	    }
	    [Verb("Download", HelpText = "Download assembly.")]
	    class DownloadOptions {
			[Option("PackageName", Required = true)]
			public string PackageName { get; set; }
	    }
		[Verb("Upload", HelpText = "Upload assembly.")]
	    class UploadOptions {
			[Option("PackageName", Required = true)]
			public string PackageName { get; set; }
	    }
	    

		public static void Login() {
			string userName = "Supervisor"; // Получить логин из конфига
			string userPassword = "Supervisor"; // Получить пароль из конфига
			var authRequest = HttpWebRequest.Create(LoginUrl) as HttpWebRequest;
			authRequest.Method = "POST";
			authRequest.ContentType = "application/json";
			authRequest.CookieContainer = AuthCookie;
			using (var requestStream = authRequest.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(@"{
						""UserName"":""" + userName + @""",
						""UserPassword"":""" + userPassword + @"""
					}");
				}
			}
			using (var response = (HttpWebResponse)authRequest.GetResponse()) {
				string authName = ".ASPXAUTH";
				string headerCookies = response.Headers["Set-Cookie"];
				string authCookeValue = GetCookieValueByName(headerCookies, authName);
				AuthCookie.Add(new Uri(Url), new Cookie(authName, authCookeValue));
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
			var bpmcsrf = request.CookieContainer.GetCookies(new Uri(Url))["BPMCSRF"];
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

		private static int Execute(ExecuteOptions options) {
			Login();
			ExecuteScript(options);
			return 0;
		}

		private static int Restart(RestartOptions options) {
			Login();
			UnloadAppDomain();
			return 0;
		}

		private static int Download(DownloadOptions options) {
			Login();
			DownloadPackages(options.PackageName);
			return 0;
		}

		private static int Upload(UploadOptions options) {
			Login();
			Uploadpackages(options.PackageName);
			return 0;
		}

		private static int Main(string[] args)
	    {
			// For Example
			//var options = new Options {
			//	Uri = "http://tscore-dev-01:88/StudioENU_2090688_0914",
			//	Login = "Supervisor",
			//	Password = "Supervisor",
			//	Operation = "UnloadAppDomain",
			//	PackageName = "BpmonlinePkg"
			//};
		    Url = "http://tscore-dev-01:88/StudioENU_2099879_0919";


			return Parser.Default.ParseArguments<ExecuteOptions, RestartOptions, DownloadOptions, UploadOptions>(args)
			    .MapResult(
				    (ExecuteOptions opts) => Execute(opts),
				    (RestartOptions opts) => Restart(opts),
				    (DownloadOptions opts) => Download(opts),
				    (UploadOptions opts) => Upload(opts),
					errs => 1);
	    }
    }
}