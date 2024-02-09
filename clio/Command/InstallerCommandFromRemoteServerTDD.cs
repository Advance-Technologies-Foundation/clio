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

	public static class CreatioDBTypeExtensions {
		public static string ToDBTypeString(this CreatioDBType dbType) {
			return dbType switch {
				CreatioDBType.MSSQL => "MSSQL",
				CreatioDBType.PostgreSQL => "PostgreSQL",
				_ => throw new NotImplementedException()
			};
		}
	}

	public static class CreatioRuntimePlatformExtensions {
		public static string ToRuntimePlatformString(this CreatioRuntimePlatform runtimePlatform) {
			return runtimePlatform switch {
				CreatioRuntimePlatform.NETFramework => "",
				CreatioRuntimePlatform.NET6 => "Net6",
				_ => throw new NotImplementedException()
			};
		}
	}
}