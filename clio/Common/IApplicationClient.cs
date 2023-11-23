using Creatio.Client;
using System;
using System.Net.WebSockets;
using System.Threading;
using Creatio.Client.Dto;

namespace Clio.Common
{
	public interface IApplicationClient
	{
		
		public event EventHandler<WsMessage> MessageReceived;

		public event EventHandler<WebSocketState> ConnectionStateChanged;
		
		string CallConfigurationService(string serviceName, string serviceMethod, string requestData, int requestTimeout = 10000);
		void DownloadFile(string url, string filePath, string requestData);
		string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite);
		string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite);
		void Login();
		string UploadFile(string url, string filePath);
		string UploadAlmFile(string url, string filePath);
		
		void Listen(CancellationToken cancellationToken);
		
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

		public string UploadAlmFile(string url, string filePath) {
			return _creatioClient.UploadAlmFile(url, filePath);
		}

		internal T As<T>()
        {
            throw new NotImplementedException();
        }
		
		public void Listen(CancellationToken cancellationToken) {
			_creatioClient.ConnectionStateChanged += (sender, state) => {
				ConnectionStateChanged?.Invoke(sender, state);
			};
			
			_creatioClient.MessageReceived += (sender, message) => {
				MessageReceived?.Invoke(sender, message);
			};
			
			_creatioClient.StartListening(cancellationToken);
		} 
		
		public event EventHandler<WsMessage> MessageReceived;

		public event EventHandler<WebSocketState> ConnectionStateChanged;

    }
}
