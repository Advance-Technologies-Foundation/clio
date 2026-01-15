using System.Net;
using ATF.Repository.Providers;
using Clio.Common;

namespace Clio.Command;

public abstract class BaseDataContextCommand<T> : Command<T>{
	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly EnvironmentSettings _environmentSettings;

	#endregion

	#region Constructors: Protected

	protected BaseDataContextCommand(IDataProvider provider, ILogger logger) {
		Provider = provider;
		Logger = logger;
	}

	protected BaseDataContextCommand(IDataProvider provider, ILogger logger,
		IApplicationClient applicationClient, EnvironmentSettings environmentSettings) {
		Provider = provider;
		Logger = logger;
		_applicationClient = applicationClient;
		_environmentSettings = environmentSettings;
	}

	#endregion

	#region Constructors: Internal

	internal BaseDataContextCommand() { }

	#endregion

	internal readonly ILogger Logger;

	internal readonly IDataProvider Provider;

	#region Methods: Private

	private void Login() {
		try {
			Logger.WriteInfo(
				$"Try login to {_environmentSettings.Uri} with {_environmentSettings.Login} credentials...");
			_applicationClient.Login();
			Logger.WriteInfo("Login done");
		}
		catch (WebException we) {
			HttpWebResponse errorResponse = we.Response as HttpWebResponse;
			if (errorResponse.StatusCode == HttpStatusCode.NotFound) {
				Logger.WriteError($"Application {_environmentSettings.Uri} not found");
			}
			throw we;
		}
	}

	#endregion

	#region Methods: Public

	public override int Execute(T options) {
		Login();
		return 0;
	}

	#endregion
}
