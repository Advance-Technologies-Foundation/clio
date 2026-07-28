using System;
using System.Collections.ObjectModel;
using ClioRing.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using ClioRing.Services;

namespace ClioRing.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	private readonly WorkspaceService _workspaceService;

	[ObservableProperty]
	private ObservableCollection<WorkspaceViewModel> _workspaces = new();

	[ObservableProperty]
	private string? _errorMessage;

	[ObservableProperty]
	private bool _isLoading = true;

	public MainViewModel()
	{
		_workspaceService = new WorkspaceService();
		LoadWorkspaces();
	}

	private void LoadWorkspaces()
	{
		try
		{
			ErrorMessage = null;
			IsLoading = true;

			var settings = _workspaceService.LoadSettings();
			var workspaces = _workspaceService.DiscoverWorkspaces(settings.WorkspaceFolder);

			Workspaces.Clear();
			foreach (Workspace workspace in workspaces) {
				Workspaces.Add(new WorkspaceViewModel(workspace, ExecuteScript, ExecuteGitPull, ExecuteOpenInVsCode));
			}

			IsLoading = false;
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Error loading workspaces: {ex.Message}";
			IsLoading = false;
		}
	}

	private void ExecuteScript(string workspacePath, bool isNetCore)
	{
		try {
			_workspaceService.ExecuteScript(workspacePath, isNetCore);
		}
		catch (Exception ex) {
			ErrorMessage = $"Error executing script: {ex.Message}";
		}
	}

	private void ExecuteGitPull(string workspacePath)
	{
		try
		{
			_workspaceService.GitPull(workspacePath);
			ErrorMessage = null;
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Error pulling git changes: {ex.Message}";
		}
	}

	private void ExecuteOpenInVsCode(string workspacePath)
	{
		try
		{
			_workspaceService.OpenInVsCode(workspacePath);
			ErrorMessage = null;
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Error opening workspace in VS Code: {ex.Message}";
		}
	}
}
