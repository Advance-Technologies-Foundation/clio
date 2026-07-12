using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClioLauncher.Models;

namespace ClioLauncher.ViewModels;

public partial class WorkspaceViewModel : ViewModelBase
{
	private readonly Workspace _workspace;
	private readonly Action<string, bool> _executeScript;
	private readonly Action<string> _gitPull;
	private readonly Action<string> _openInVsCode;

	public WorkspaceViewModel(
		Workspace workspace,
		Action<string, bool> executeScript,
		Action<string> gitPull,
		Action<string> openInVsCode)
	{
		_workspace = workspace;
		_executeScript = executeScript;
		_gitPull = gitPull;
		_openInVsCode = openInVsCode;
	}

	public string Name => _workspace.Name;
	public string? IconPath => _workspace.IconPath;
	public bool HasNetFrameworkScript => _workspace.HasNetFrameworkScript;
	public bool HasNetCoreScript => _workspace.HasNetCoreScript;
	public bool IsGitRepository => _workspace.IsGitRepository;
	public string BranchDisplay => string.IsNullOrWhiteSpace(_workspace.CurrentBranch)
		? "Branch: (unknown)"
		: $"Branch: {_workspace.CurrentBranch}";

	[RelayCommand]
	private void OpenNetFramework()
	{
		_executeScript(_workspace.Path, false);
	}

	[RelayCommand]
	private void OpenNetCore()
	{
		_executeScript(_workspace.Path, true);
	}

	[RelayCommand]
	private void GitPull()
	{
		_gitPull(_workspace.Path);
	}

	[RelayCommand]
	private void OpenInVsCode()
	{
		_openInVsCode(_workspace.Path);
	}
}
