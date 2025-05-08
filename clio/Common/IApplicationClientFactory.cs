namespace Clio.Common;

public interface IApplicationClientFactory
{

    #region Methods: Public

    IApplicationClient CreateClient(EnvironmentSettings environment);

    IApplicationClient CreateEnvironmentClient(EnvironmentSettings environment);

    #endregion

}
