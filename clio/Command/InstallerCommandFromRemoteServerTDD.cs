using System;
using Clio.Command.CreatioInstallCommand;

namespace Clio.Command
{
    internal class InstallerCommandFromRemoteServerTDD
    {

        #region Fields: Private

        private readonly InstallerCommand _command;

        #endregion

        #region Constructors: Public

        public InstallerCommandFromRemoteServerTDD(InstallerCommand command)
        {
            _command = command;
        }

        #endregion

        #region Methods: Public

        public void Do()
        {
            _command.Execute(new PfInstallerOptions
            {
                DBType = CreatioDBType.MSSQL, RuntimePlatform = CreatioRuntimePlatform.NETFramework, Product = "Studio"
            });
        }

        #endregion

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

        #region Methods: Public

        public static string ToDBTypeString(this CreatioDBType dbType)
        {
            return dbType switch
                   {
                       CreatioDBType.MSSQL => "MSSQL",
                       CreatioDBType.PostgreSQL => "PostgreSQL",
                       var _ => throw new NotImplementedException()
                   };
        }

        #endregion

    }

    public static class CreatioRuntimePlatformExtensions
    {

        #region Methods: Public

        public static string ToRuntimePlatformString(this CreatioRuntimePlatform runtimePlatform)
        {
            return runtimePlatform switch
                   {
                       CreatioRuntimePlatform.NETFramework => "",
                       CreatioRuntimePlatform.NET6 => "Net6",
                       var _ => throw new NotImplementedException()
                   };
        }

        #endregion

    }
}
