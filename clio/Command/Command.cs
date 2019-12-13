using Clio.UserEnvironment;

namespace Clio.Command
{
	public abstract class Command<TEnvironmentOptions> 
		where TEnvironmentOptions: EnvironmentOptions
	{
		public abstract int Execute(TEnvironmentOptions options);
	}
}
