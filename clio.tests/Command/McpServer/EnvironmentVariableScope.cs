using System;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Scopes a process-wide environment variable to one test and restores the previous value on
/// dispose, so an override cannot leak into sibling tests. Shared by every fixture that has to
/// drive clio through an environment variable; a fixture using it must also be
/// <c>[NonParallelizable]</c>, because the variable is process-wide and the assembly runs
/// fixtures in parallel (<c>TestAssemblySetup</c>).
/// </summary>
internal sealed class EnvironmentVariableScope : IDisposable {
	private readonly string _name;
	private readonly string? _previous;

	public EnvironmentVariableScope(string name, string? value) {
		_name = name;
		_previous = Environment.GetEnvironmentVariable(name);
		Environment.SetEnvironmentVariable(name, value);
	}

	public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}
