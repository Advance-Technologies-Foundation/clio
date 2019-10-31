using Bpmonline.Client;

namespace clio.Command
{
	class BaseRemoteCommand
	{
		private static string _userName => _settings.Login;
		private static string _userPassword => _settings.Password;
		private static string _url => _settings.Uri;
		protected static string _appUrl
		{
			get
			{
				if (_isNetCore) {
					return _url;
				} else {
					return _url + @"/0";
				}
			}
		}
		protected static bool _isNetCore => _settings.IsNetCore;
		private static EnvironmentSettings _settings;


		protected static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			_settings = settingsRepository.GetEnvironment(options);
		}

		protected static void Configure(EnvironmentSettings settings) {
			_settings = settings;
		}

		protected static BpmonlineClient CreatioClient
		{
			get => new BpmonlineClient(_url, _userName, _userPassword, _isNetCore);
		}
	}
}
