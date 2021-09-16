using Clio.Common;
using CommandLine;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Clio.Command
{
    [Verb("health-check", Aliases = new string[] { "health" }, HelpText = "Checks health of the selected environments")]
	public class HealthCheckOptions : EnvironmentNameOptions
	{
		[Option('x', "Endpoint", Required = false, HelpText = "Relative path for checked endpoint (by default use Health service)")]
		public string Endpoint { get; set; } = "/api/HealthCheck/Ping";
	}

	public class HealthCheckCommand : RemoteCommand<HealthCheckOptions>
	{
		private HttpClient _httpClient;
		private HttpClientHandler _handler;
		public HealthCheckCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {

			_handler = new HttpClientHandler();

			//Skip SSL cert validation
			_handler.ClientCertificateOptions = ClientCertificateOption.Manual;
			_handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, certChail, sslPolicyErrors)=>{
				return true;
            };

			_httpClient = new HttpClient(_handler);
			_httpClient.BaseAddress = new Uri(RootPath);
		}

		public override int Execute(HealthCheckOptions options) {

			HealthCheckResult r1 = default;
			HealthCheckResult r2 = default;
				
			Task T1 = Task.Run(async()=>
			{
				r1 = await Check(options, CheckType.WebApp);
			});

            Task T2 = Task.Run(async () =>
            {
                r2 = await Check(options, CheckType.WebAppLoader);
            });

            Task.WhenAll(T2, T1).Wait();
			PrintResult(r2);
			PrintResult(r1);
            
			if(r1.HttpStatusCode == HttpStatusCode.OK && r2.HttpStatusCode == HttpStatusCode.OK)
            {
				Console.WriteLine();
				Login();
            }

            Console.WriteLine();
			Console.WriteLine("Healthckeck completed");
			return 0;
		}
		
		/// <summary>
		/// Checks WebApp and WebAppLoader health check endpoints
		/// Only for Creatio version 7.18.1 and higher
		/// </summary>
		/// <param name="options">Options passed to health command from the console</param>
		/// <param name="checkType">Type of healthcheck to perform</param>
		/// <returns></returns>
		private async Task<HealthCheckResult> Check(HealthCheckOptions options, CheckType checkType)
        {
            // Had to use httpClient because ApplicationClient does not return content of an exception
            // https://github.com/Advance-Technologies-Foundation/creatioclient/blob/1eac828d5636f178ae38738296ebd58c88c1d0dc/creatioclient/ATFWebRequestExtensions.cs#L8
			                       
			string relativeUrl = (checkType == CheckType.WebApp) ? "/0" + options.Endpoint : options.Endpoint;

			HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync(relativeUrl);
			HealthCheckResult result = new HealthCheckResult
			{
				CheckType = checkType,
				HttpStatusCode = httpResponseMessage.StatusCode
			};

            Stream content = await httpResponseMessage.Content.ReadAsStreamAsync();

			if(result.HttpStatusCode == HttpStatusCode.InternalServerError && content.Length>0)
            {
				result.HealthCheckModel =  await JsonSerializer.DeserializeAsync<HealthCheckModel>(content);
            }
            else
            {
				//When everything is working response is 200 and no payload
				result.HealthCheckModel.IsConfAssemblyPingSuccessfull = true;
				result.HealthCheckModel.IsDbPingSuccessfull = true;
			}
			return result;
		}
		
		private void PrintResult(HealthCheckResult healthCheckResult)
        {

			var originalColor = Console.ForegroundColor;

			Console.Write($"{healthCheckResult.CheckType} - ");
			if(healthCheckResult.HttpStatusCode != HttpStatusCode.OK)
            {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write($"{healthCheckResult.HttpStatusCode}");
				Console.ForegroundColor = originalColor;
            }
            else
            {
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write($"{healthCheckResult.HttpStatusCode}");
				Console.ForegroundColor = originalColor;
			}

			if(healthCheckResult.CheckType == CheckType.WebApp)
            {
                Console.WriteLine();
				Console.Write($"  - ConfAssembly Ping Successfull: ");
				if (!healthCheckResult.HealthCheckModel.IsConfAssemblyPingSuccessfull)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.Write($"   {healthCheckResult.HealthCheckModel.IsConfAssemblyPingSuccessfull}");
					Console.ForegroundColor = originalColor;
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.Write($"{healthCheckResult.HealthCheckModel.IsConfAssemblyPingSuccessfull}");
					Console.ForegroundColor = originalColor;
				}

                Console.WriteLine();
				Console.Write("  - DB Ping Successfull:           ");
				if (!healthCheckResult.HealthCheckModel.IsDbPingSuccessfull)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.Write($"{healthCheckResult.HealthCheckModel.IsDbPingSuccessfull}");
					Console.ForegroundColor = originalColor;
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.Write($"{healthCheckResult.HealthCheckModel.IsDbPingSuccessfull}");
					Console.ForegroundColor = originalColor;
				}

			}

			Console.ForegroundColor = originalColor;
            Console.WriteLine();
            Console.WriteLine();
        }
	}
	enum CheckType {
		WebApp,
		WebAppLoader
	}
	internal class HealthCheckResult
    {
        public CheckType CheckType { get; set; }
        public HttpStatusCode HttpStatusCode { get; set; }
		public HealthCheckModel HealthCheckModel { get; set; } = new HealthCheckModel();

    }
	internal class HealthCheckModel
    {
		[JsonPropertyName("IsDbPingSuccessfull")]
        public bool IsDbPingSuccessfull { get; set; }
		
		[JsonPropertyName("IsConfAssemblyPingSuccessfull")]
		public bool IsConfAssemblyPingSuccessfull { get; set; }
    }

}
