namespace Clio.Common;

public interface ISqlScriptExecutor
{

    #region Methods: Public

    string Execute(string sql, IApplicationClient applicationClient, EnvironmentSettings settings);

    #endregion

}
