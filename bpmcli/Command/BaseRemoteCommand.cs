using Bpmonline.Client;

namespace clio.Command
{
	class BaseRemoteCommand
	{
		private static string _userName => _settings.Login;
		private static string _userPassword => _settings.Password;
		protected static string _url => _settings.Uri;
		private static bool _isNetCore => _settings.IsNetCore;
		private static EnvironmentSettings _settings;


		protected static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			_settings = settingsRepository.GetEnvironment(options);
		}

		protected static BpmonlineClient BpmonlineClient
		{
			get => new BpmonlineClient(_url, _userName, _userPassword, _isNetCore);
		}
	}
}
