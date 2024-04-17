using ATF.Repository.Providers;
using Creatio.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SolidToken.SpecFlow.DependencyInjection;

namespace clio.ApiTest;

/// <summary>
/// Creates DI container that will be available in tests
/// </summary>
/// <remarks>
/// See usage exmaples from <a href="https://github.com/solidtoken/SpecFlow.DependencyInjection">SpecFlow.DependencyInjection</a>
/// </remarks>
public static class DiBuilder
{

	#region Methods: Public

	[ScenarioDependencies]
	public static IServiceCollection CreateServices(){
		ServiceCollection services = [];
		IConfiguration configuration = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", false, true)
			.AddEnvironmentVariables()
			.Build();

		AppSettings? appSettings = configuration.Get<AppSettings>();
		if (appSettings is null) {
			throw new MissingFieldException("Could not find appsetings in file appsettings.json file or env variable, can not continue");
		}
		services.AddSingleton(appSettings);
		services.AddSingleton<IDataProvider>(sp => {
			return string.IsNullOrEmpty(appSettings.LOGIN) switch {
				true => new RemoteDataProvider(appSettings.URL, appSettings.AuthAppUri, appSettings.ClientId,
					appSettings.ClientSecret, appSettings.IS_NETCORE),
				false => new RemoteDataProvider(appSettings.URL, appSettings.LOGIN, appSettings.PASSWORD,
					appSettings.IS_NETCORE)
			};
		});
		services.AddTransient<ICreatioClient>(sp => {
			return string.IsNullOrEmpty(appSettings.LOGIN) switch {
				true => CreatioClient.CreateOAuth20Client(appSettings.URL, appSettings.AuthAppUri, appSettings.ClientId,
					appSettings.ClientSecret, appSettings.IS_NETCORE),
				false => new CreatioClient(appSettings.URL, appSettings.LOGIN, appSettings.PASSWORD,
					appSettings.IS_NETCORE)
			};
		});
		services.AddTransient<ICLioRunner, ClioRunner>();
		services.AddScoped<TestContext>();
		return services;
	}

	#endregion

}