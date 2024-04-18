using Creatio.Client;
using System;
using System.Security.Policy;
using System.Text.Json;

namespace clio.ApiTest.Steps
{
	public class BaseServiceStepDefinition<T>
	{
		internal readonly ICreatioClient _creatioClient;
		internal readonly AppSettings _appSettings;

		internal virtual string Route { get; set; }

		internal string Url {
			get {
				return _appSettings.IS_NETCORE ? _appSettings.URL + Route : _appSettings.URL + "/0" + Route;
			}
		}

		internal virtual string GetPayload() {
			return string.Empty;
		}

		internal T GetServiceResopnse() {
			var response = _creatioClient.ExecutePostRequest(Url, GetPayload());
			return JsonSerializer.Deserialize<T>(response);
		}

		

		public BaseServiceStepDefinition(ICreatioClient creatioClient, AppSettings appSettings) {
			_creatioClient = creatioClient;
			_appSettings = appSettings;
		}

		internal string GeObjectPropertyValue(Packages? package, string propertyName) {
			return package.GetType().GetProperty(propertyName).GetValue(package).ToString();
		}
	
	}
}