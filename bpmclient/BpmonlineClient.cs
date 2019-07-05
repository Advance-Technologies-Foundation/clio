using System;
using System.IO;
using System.Net;
using System.Text;

namespace Bpmonline.Client
{
	public class BpmonlineClient
	{

		#region Fields: private

		private string _appUrl;

		private string _userName;

		private string _userPassword;

		private string _worskpaceId;

		private bool _isNetCore;

		private string LoginUrl => _appUrl + @"/ServiceModel/AuthService.svc/Login";

		private string PingUrl => _appUrl + @"/0/ping";

		private CookieContainer AuthCookie;

		#endregion

		#region Methods: Public

		public BpmonlineClient(string appUrl, string userName, string userPassword, bool isNetCore = false, string workspaceId = "0") {
			_appUrl = appUrl;
			_userName = userName;
			_userPassword = userPassword;
			_worskpaceId = workspaceId;
			_isNetCore = isNetCore;
		}

		public void Login() {
			var authData = @"{
				""UserName"":""" + _userName + @""",
				""UserPassword"":""" + _userPassword + @"""
			}";
			var request = CreateRequest(LoginUrl, authData);
			AuthCookie = new CookieContainer();
			request.CookieContainer = AuthCookie;
			using (var response = (HttpWebResponse)request.GetResponse()) {
				if (response.StatusCode == HttpStatusCode.OK) {
					using (var reader = new StreamReader(response.GetResponseStream())) {
						var responseMessage = reader.ReadToEnd();
						if (responseMessage.Contains("\"Code\":1")) {
							throw new UnauthorizedAccessException($"Unauthorized {_userName} for {_appUrl}");
						}
					}
					string authName = ".ASPXAUTH";
					string authCookeValue = response.Cookies[authName].Value;
					AuthCookie.Add(new Uri(_appUrl), new Cookie(authName, authCookeValue));
				}
			}
		}

		public string ExecuteGetRequest(string url, int requestTimeout = 10000) {
			HttpWebRequest request = CreateBpmonlineRequest(url, null, requestTimeout);
			request.Method = "GET";
			return request.GetServiceResponse();
		}

		public string ExecutePostRequest(string url, string requestData, int requestTimeout = 10000) {
			HttpWebRequest request = CreateBpmonlineRequest(url, requestData, requestTimeout);
			return request.GetServiceResponse();
		}

		public string UploadFile(string url, string filePath) {
			FileInfo fileInfo = new FileInfo(filePath);
			string fileName = fileInfo.Name;
			string boundary = DateTime.Now.Ticks.ToString("x");
			HttpWebRequest request = CreateBpmonlineRequest(url);
			request.ContentType = "multipart/form-data; boundary=" + boundary;
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
			return request.GetServiceResponse();
		}

		public void DownloadFile(string url, string filePath, string requestData) {
			HttpWebRequest request = CreateBpmonlineRequest(url, requestData);
			request.SaveToFile(filePath);
		}

		public string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = 10000) {
			var executeUrl = CreateConfigurationServiceUrl(serviceName, serviceMethod);
			return ExecutePostRequest(executeUrl, requestData, requestTimeout);
		}

		#endregion

		#region Methods: private

		private string CreateConfigurationServiceUrl(string serviceName, string methodName) {
			return $"{_appUrl}/{_worskpaceId}/rest/{serviceName}/{methodName}";
		}

		private void AddCsrfToken(HttpWebRequest request) {
			var bpmcsrf = request.CookieContainer.GetCookies(new Uri(_appUrl))["BPMCSRF"];
			if (bpmcsrf != null) {
				request.Headers.Add("BPMCSRF", bpmcsrf.Value);
			}
		}

		private void PingApp() {
			if (_isNetCore) {
				return;
			}
			var pingRequest = CreateBpmonlineRequest(PingUrl);
			pingRequest.Timeout = 60000;
			_ = pingRequest.GetServiceResponse();
		}

		private HttpWebRequest CreateBpmonlineRequest(string url, string requestData = null, int requestTimeout = 100000) {
			if (AuthCookie == null) {
				Login();
				PingApp();
			}
			var request = CreateRequest(url, requestData);
			request.Timeout = requestTimeout;
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			return request;
		}

		private HttpWebRequest CreateRequest(string url, string requestData = null) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.ContentType = "application/json";
			request.Method = "POST";
			request.KeepAlive = true;
			if (!string.IsNullOrEmpty(requestData)) {
				using (var requestStream = request.GetRequestStream()) {
					using (var writer = new StreamWriter(requestStream)) {
						writer.Write(requestData);
					}
				}
			}
			return request;
		}

		#endregion

	}

}
