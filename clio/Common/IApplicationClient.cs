using Creatio.Client;
using System;
using System.Threading;

namespace Clio.Common
{
	public interface IApplicationClient
	{
		string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = 10000);
		void DownloadFile(string url, string filePath, string requestData);
		string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite);
		string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite);
		void Login();
		string UploadFile(string url, string filePath);
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

		public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite) {
			return _creatioClient.ExecutePostRequest(url, requestData, requestTimeout);
		}

		public void Login() {
			_creatioClient.Login();
		}

		public string UploadFile(string url, string filePath) {
			return _creatioClient.UploadFile(url, filePath);
		}

        internal T As<T>()
        {
            throw new NotImplementedException();
        }
    }
}
