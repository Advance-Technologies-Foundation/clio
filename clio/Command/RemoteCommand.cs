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
		protected IApplicationClient ApplicationClient => _applicationClient;

		protected RemoteCommand(IApplicationClient applicationClient) {
			_applicationClient = applicationClient;
		}
	}
}
