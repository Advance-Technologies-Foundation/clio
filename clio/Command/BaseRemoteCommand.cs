using Clio.Common;
using Creatio.Client;
using System;

namespace Clio.Command
{
	public class BaseRemoteCommand 
	{

		private IApplicationClient _applicationClient;

		protected IApplicationClient ApplicationClient {
			get => _applicationClient ?? (_applicationClient = new CreatioClientAdapter(_url, _userName, _userPassword, _isNetCore));
		}

		[Obsolete("Use ApplicationClient property instead")]
		protected static CreatioClient CreatioClient {
			get => new CreatioClient(_url, _userName, _userPassword, _isNetCore);
		}

		public BaseRemoteCommand() {
		}

		public BaseRemoteCommand(IApplicationClient applicationClient) {
			_applicationClient = applicationClient;
		}

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
	}
}
