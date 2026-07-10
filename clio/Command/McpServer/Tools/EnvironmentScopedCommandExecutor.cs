namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Holds the shared env-less resolution decision used by <c>BaseTool&lt;T&gt;</c>: a handful of options
/// types resolve against a default (environment-less) container when no environment/uri is supplied.
/// Keeping this decision in one place guarantees every flat tool that resolves a command makes the
/// identical environment-vs-environmentless choice.
/// </summary>
public static class EnvironmentScopedCommandExecutor {

	/// <summary>
	/// Returns <c>true</c> when the given options instance should be resolved against the default
	/// (environment-less) container rather than an environment-scoped one.
	/// </summary>
	/// <param name="options">An <see cref="EnvironmentOptions"/>-derived options instance.</param>
	/// <returns><c>true</c> for the env-less special-case option types; otherwise <c>false</c>.</returns>
	internal static bool UsesEnvironmentlessResolution(EnvironmentOptions options) =>
		options switch {
			CreateTestProjectOptions o when string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			AddPackageOptions o when string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			CreateWorkspaceCommandOptions o when o.Empty && string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			CreateUiProjectOptions o when string.IsNullOrWhiteSpace(o.Environment) && string.IsNullOrWhiteSpace(o.Uri) => true,
			_ => false
		};
}
