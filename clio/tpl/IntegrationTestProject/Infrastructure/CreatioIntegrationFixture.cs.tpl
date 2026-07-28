using ATF.Repository;
using ATF.Repository.Providers;
using NUnit.Framework;

namespace {{packageName}}.IntegrationTests.Infrastructure;

public abstract class CreatioIntegrationFixture {
	protected IDataProvider DataProvider { get; private set; }
	protected IAppDataContext DataContext { get; private set; }
	protected IAppProcessContext ProcessContext { get; private set; }

	[OneTimeSetUp]
	public void CreateContexts() {
		CreatioTestSettings settings = CreatioTestSettings.Load();
		DataProvider = settings.UsesAccessToken
			? new RemoteDataProvider(settings.Url.ToString(), settings.AccessToken, settings.IsNetCore)
			: new RemoteDataProvider(settings.Url.ToString(), settings.Username, settings.Password, settings.IsNetCore);
		DataContext = AppDataContextFactory.GetAppDataContext(DataProvider);
		ProcessContext = AppProcessContextFactory.GetAppProcessContext(DataProvider);
		TestContext.Progress.WriteLine($"Creatio URL: {settings.Url}; IsNetCore: {settings.IsNetCore}");
	}
}
