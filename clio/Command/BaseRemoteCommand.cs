using Clio.Common;
using Creatio.Client;
using System;

namespace Clio.Command
{
	[Obsolete("Use RemoteCommand class instead.")]
	public class BaseRemoteCommand 
	{

		private IApplicationClient _applicationClient;

		protected IApplicationClient ApplicationClient {
			get => _applicationClient ?? (_applicationClient = new CreatioClientAdapter(_url, _userName, _userPassword, _isNetCore));
		}

		protected static CreatioClient CreatioClient {
			get => new CreatioClient(_url, _userName, _userPassword, _isDevMode, _isNetCore);
		}

		public BaseRemoteCommand() {
		}

		public BaseRemoteCommand(IApplicationClient applicationClient) {
			_applicationClient = applicationClient;
		}


		private static bool _isDevMode => Settings.IsDevMode;
		private static string _userName => Settings.Login;
		private static string _userPassword => Settings.Password;
		private static string _url => Settings.Uri;
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
		protected static bool _isNetCore => Settings.IsNetCore;
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
