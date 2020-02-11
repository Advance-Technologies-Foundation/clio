using Clio.Common;
using Creatio.Client;

namespace Clio.Command
{
	public class BaseRemoteCommand
	{

		private IApplicationClient _applicationClient;

		protected IApplicationClient ApplicationClient
		{
			get => _applicationClient ?? (_applicationClient = new CreatioClientAdapter(Url, UserName, UserPassword, IsNetCore));
		}

		protected static CreatioClient CreatioClient
		{
			get => new CreatioClient(Url, UserName, UserPassword, IsDevMode, IsNetCore);
		}

		public BaseRemoteCommand() {
		}

		public BaseRemoteCommand(IApplicationClient applicationClient) {
			_applicationClient = applicationClient;
		}


		private static bool IsDevMode => Settings.IsDevMode;
		private static string UserName => Settings.Login;
		private static string UserPassword => Settings.Password;
		private static string Url => Settings.Uri;
		protected static string AppUrl
		{
			get
			{
				if (IsNetCore) {
					return Url;
				} else {
					return Url + @"/0";
				}
			}
		}
		protected static bool IsNetCore => Settings.IsNetCore;
		protected static EnvironmentSettings Settings;

		protected static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			Settings = settingsRepository.GetEnvironment(options);
		}

		protected static void Configure(EnvironmentSettings settings) {
			Settings = settings;
		}
	}
}
