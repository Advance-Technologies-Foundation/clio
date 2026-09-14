namespace Clio.Common
{
	/// <summary>Executes SQL through ClioGate and decodes its transport response without changing cell content.</summary>
	public interface ISqlScriptExecutor
	{
		/// <summary>Executes SQL once and unwraps a JSON string response when the transport wraps it.</summary>
		/// <param name="sql">SQL text to execute.</param>
		/// <param name="applicationClient">Authenticated client for the target environment.</param>
		/// <param name="settings">Target environment settings.</param>
		/// <returns>JSON rows, an affected-row count, or a server error, preserving embedded escapes.</returns>
		string Execute(string sql, IApplicationClient applicationClient, EnvironmentSettings settings);
	}
}
