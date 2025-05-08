using System;
using Clio.Command.CreatioInstallCommand;

namespace Clio.Command
{
    internal class InstallerCommandFromRemoteServerTDD(InstallerCommand command)
    {
        private readonly InstallerCommand _command = command;

        public void Do() =>
            _command.Execute(new PfInstallerOptions
            {
                DBType = CreatioDBType.MSSQL,
                RuntimePlatform = CreatioRuntimePlatform.NETFramework,
                Product = "Studio"
            });
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

    public static class CreatioDBTypeExtensions
    {
        public static string ToDBTypeString(this CreatioDBType dbType) =>
            dbType switch
            {
                CreatioDBType.MSSQL => "MSSQL",
                CreatioDBType.PostgreSQL => "PostgreSQL",
                _ => throw new NotImplementedException()
            };
    }

    public static class CreatioRuntimePlatformExtensions
    {
        public static string ToRuntimePlatformString(this CreatioRuntimePlatform runtimePlatform) =>
            runtimePlatform switch
            {
                CreatioRuntimePlatform.NETFramework => string.Empty,
                CreatioRuntimePlatform.NET6 => "Net6",
                _ => throw new NotImplementedException()
            };
    }
}
