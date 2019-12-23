using Clio.UserEnvironment;

namespace Clio.Command
{
	public abstract class Command<TEnvironmentOptions>
	{
		public abstract int Execute(TEnvironmentOptions options);
	}
}
