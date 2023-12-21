using Clio.Tests.Command;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Tests;

public static class ClioTestsSetup
{
	
	private static readonly ServiceCollection Services = new ServiceCollection();
	private static IServiceScope _scope;
	
	private static IServiceScope Init(){
		Services.AddSingleton<ReadmeChecker>();
		return Services.BuildServiceProvider().CreateScope();
	}

	public static T GetService<T>(){
		_scope ??= Init();
		return _scope.ServiceProvider.GetService<T>();
	}
	
}