namespace Clio.Common
{
	public interface ISqlScriptExecutor
	{
		string Execute(string sql, IApplicationClient applicationClient, EnvironmentSettings settings);
	}
}
