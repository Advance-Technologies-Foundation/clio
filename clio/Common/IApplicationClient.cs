using Creatio.Client;
using System;
using System.Threading;

namespace Clio.Common
{
	public interface IApplicationClient
	{
		string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = 10000);
		void DownloadFile(string url, string filePath, string requestData);
		
		/// <summary>
		/// Executes GET Request without retry
		/// </summary>
		/// <param name="url">Request URL</param>
		/// <param name="requestTimeout">Request Timeout</param>
		/// <returns>Response</returns>
		string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite);

		/// <summary>
		/// Executes GET Request with retry
		/// </summary>
		/// <param name="url">Request URL</param>
		/// <param name="requestTimeout">Request Timeout</param>
		/// <param name="retryCount">retry count</param>
		/// <param name="delaySec">delay between retries in seconds</param>
		/// <returns>Response</returns>
		/// <exception cref="Exception">Throws when request fails after attempts exceed <paramref name="retryCount"/> count</exception>
		string ExecuteGetRequest(string url, int requestTimeout, int retryCount = 1, int delaySec = 1);
		
		/// <summary>
		/// Executes POST Request without retry
		/// </summary>
		/// <param name="url">Request URL</param>
		/// <param name="requestData">Request Data</param>
		/// <param name="requestTimeout">Request Timeout</param>
		/// <returns>Response</returns>
		string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite);

		/// <summary>
		/// Executes POST Request with retry
		/// </summary>
		/// <param name="url">Request URL</param>
		/// <param name="requestData">Request Data</param>
		/// <param name="requestTimeout">Request Timeout</param>
		/// <param name="retryCount">retry count</param>
		/// <param name="delaySec">delay between retries in seconds</param>
		/// <returns>Response</returns>
		/// <exception cref="Exception">Throws when request fails after attempts exceed <paramref name="retryCount"/> count</exception>
		string ExecutePostRequest(string url, string requestData, int requestTimeout , int retryCount = 1, int delaySec = 1);
		void Login();
		string UploadFile(string url, string filePath);
		string UploadAlmFile(string url, string filePath);
	}

	public class CreatioClientAdapter : IApplicationClient
	{
		private readonly CreatioClient _creatioClient;

		public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false) {
			_creatioClient = new CreatioClient(appUrl, userName, userPassword, true, isNetCore);
		}

		public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string AuthAppUrl, bool isNetCore = false) {
			_creatioClient = CreatioClient.CreateOAuth20Client(appUrl, AuthAppUrl, clientId, clientSecret, isNetCore);
		}

		public CreatioClientAdapter(CreatioClient creatioClient) {
			_creatioClient = creatioClient;
		}

		public string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = Timeout.Infinite) {
			return _creatioClient.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);
		}

		public void DownloadFile(string url, string filePath, string requestData) {
			_creatioClient.DownloadFile(url, filePath, requestData);
		}
		
		public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite) {
			return _creatioClient.ExecuteGetRequest(url, requestTimeout);
		}
		
		public string ExecuteGetRequest(string url, int requestTimeout, int retryCount = 1, int delaySec = 1 ) {
			return _creatioClient.ExecuteGetRequest(url, requestTimeout, retryCount, delaySec);
		}

		public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite) {
			return _creatioClient.ExecutePostRequest(url, requestData, requestTimeout);
		}
		
		public string ExecutePostRequest(string url, string requestData, int requestTimeout, int retryCount, int delaySec = 1) {
			return _creatioClient.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec);
		}
		
		public void Login() {
			_creatioClient.Login();
		}

		public string UploadFile(string url, string filePath) {
			return _creatioClient.UploadFile(url, filePath);
		}

		public string UploadAlmFile(string url, string filePath) {
			return _creatioClient.UploadAlmFile(url, filePath);
		}

		internal T As<T>()
        {
            throw new NotImplementedException();
        }
    }
}
