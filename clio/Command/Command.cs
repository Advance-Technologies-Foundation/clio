namespace Clio.Command;

public abstract class Command<TEnvironmentOptions>
{

    #region Methods: Public

    public abstract int Execute(TEnvironmentOptions options);

    #endregion

}
