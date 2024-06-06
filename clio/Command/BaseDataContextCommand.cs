using System;
using System.Net;
using ATF.Repository.Providers;
using Clio.Common;

namespace Clio.Command
{
	public abstract class BaseDataContextCommand<T>: Command<T>
	{
		#region Fields: Internal

		internal readonly IDataProvider _provider;
		internal readonly ILogger _logger;
        private readonly IApplicationClient _applicationClient;
        private readonly EnvironmentSettings _environmentSettings;

        #endregion

        internal BaseDataContextCommand()
        {
            
        }

        public BaseDataContextCommand(IDataProvider provider, ILogger logger) {
            _provider = provider;
            _logger = logger;
        }

        public BaseDataContextCommand(IDataProvider provider, ILogger logger,
                IApplicationClient applicationClient, EnvironmentSettings environmentSettings) {
			_provider = provider;
			_logger = logger;
            _applicationClient = applicationClient;
            _environmentSettings = environmentSettings;
        }

        protected void Login() {
            try {
                _logger.WriteInfo($"Try login to {_environmentSettings.Uri} with {_environmentSettings.Login} credentials...");
                _applicationClient.Login();
                _logger.WriteInfo("Login done");
            } catch (WebException we) {
                HttpWebResponse errorResponse = we.Response as HttpWebResponse;
                if (errorResponse.StatusCode == HttpStatusCode.NotFound) {
                    _logger.WriteError($"Application {_environmentSettings.Uri} not found");
                }
                throw we;
            }
        }

        public override int Execute(T options) {
            Login();
            return 0;
        }

    }
}