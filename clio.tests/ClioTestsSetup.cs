using Clio.Tests.Command;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Tests;

public static class ClioTestsSetup
{

    #region Fields: Private

    private static readonly ServiceCollection Services = new();
    private static IServiceScope _scope;

    #endregion

    #region Methods: Private

    private static IServiceScope Init()
    {
        Services.AddSingleton<ReadmeChecker>();
        return Services.BuildServiceProvider().CreateScope();
    }

    #endregion

    #region Methods: Public

    public static T GetService<T>()
    {
        _scope ??= Init();
        return _scope.ServiceProvider.GetService<T>();
    }

    #endregion

}
