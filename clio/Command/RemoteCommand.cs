using Clio.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Clio.Command
{
	public abstract class RemoteCommand<TEnvironmentOptions>: Command<TEnvironmentOptions>
		where TEnvironmentOptions: EnvironmentOptions
	{
		private readonly IApplicationClient _applicationClient;
		private readonly EnvironmentSettings _environmentSettings;

		protected IApplicationClient ApplicationClient => _applicationClient;
		protected EnvironmentSettings EnvironmentSettings => _environmentSettings;

		protected RemoteCommand(IApplicationClient applicationClient, 
				EnvironmentSettings environmentSettings) {
			_applicationClient = applicationClient;
			_environmentSettings = environmentSettings;
		}
	}
}
