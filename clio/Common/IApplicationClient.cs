using Creatio.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace Clio.Common
{
	public interface IApplicationClient
	{
		string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = 10000);
		void DownloadFile(string url, string filePath, string requestData);
		string ExecuteGetRequest(string url, int requestTimeout = 10000);
		string ExecutePostRequest(string url, string requestData, int requestTimeout = 10000);
		void Login();
		string UploadFile(string url, string filePath);
	}

	public class CreatioClientAdapter : IApplicationClient
	{
		private CreatioClient _creatioClient;

		public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false, string workspaceId = "0") {
			_creatioClient = new CreatioClient(appUrl, userName, userPassword, isNetCore, workspaceId);
		}

		public CreatioClientAdapter(CreatioClient creatioClient) {
			_creatioClient = creatioClient;
		}

		public string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = 10000) {
			return _creatioClient.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);
		}

		public void DownloadFile(string url, string filePath, string requestData) {
			_creatioClient.DownloadFile(url, filePath, requestData);
		}

		public string ExecuteGetRequest(string url, int requestTimeout = 10000) {
			return _creatioClient.ExecuteGetRequest(url, requestTimeout);
		}

		public string ExecutePostRequest(string url, string requestData, int requestTimeout = 10000) {
			return _creatioClient.ExecutePostRequest(url, requestData, requestTimeout);
		}

		public void Login() {
			_creatioClient.Login();
		}

		public string UploadFile(string url, string filePath) {
			return _creatioClient.UploadFile(url, filePath);
		}
	}
}
