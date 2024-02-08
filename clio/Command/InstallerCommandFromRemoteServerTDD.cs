using Clio.Package;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command
{
	internal class InstallerCommandFromRemoteServerTDD
	{
		InstallerCommand _command;

		public InstallerCommandFromRemoteServerTDD(InstallerCommand command) {
			_command = command;
		}	

		public void Do() {
			_command.Execute(new PfInstallerOptions() {
				DBType = CreatioDBType.MSSQL,
				RuntimePlatform = CreatioRuntimePlatform.NETFramework,
				Product = "Studio"
			});
		}
	}
}

namespace Clio
{
	public enum CreatioDBType
	{
		MSSQL,
		PostgreSQL
	}

	public enum CreatioRuntimePlatform
	{
		NETFramework,
		NET6
	}
}