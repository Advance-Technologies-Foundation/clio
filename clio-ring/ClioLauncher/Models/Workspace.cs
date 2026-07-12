namespace ClioLauncher.Models;

public class Workspace
{
	public required string Name { get; init; }
	public required string Path { get; init; }
	public string? IconPath { get; init; }
	public bool HasNetFrameworkScript { get; init; }
	public bool HasNetCoreScript { get; init; }
	public bool IsGitRepository { get; init; }
	public string? CurrentBranch { get; init; }
}
