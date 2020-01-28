using System;
using System.Collections.Generic;
using System.Text;

namespace Clio.Common
{
	public interface IApplicationClientFactory
	{
		IApplicationClient CreateClient(EnvironmentSettings environment);
	}
}
