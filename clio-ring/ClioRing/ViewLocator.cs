using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ClioRing.ViewModels;
using ClioRing.Views;

namespace ClioRing;

/// <summary>
/// Resolves a view for a view-model using an explicit, compiled view↔view-model map.
/// This avoids <see cref="System.Type.GetType(string)"/> reflection (IL2057) so the
/// application is fully NativeAOT/trim safe.
/// </summary>
public class ViewLocator : IDataTemplate {
	/// <inheritdoc />
	public Control? Build(object? param) {
		return param switch {
			null => null,
			MainViewModel => new MainView(),
			RingViewModel => new RingView(),
			InstallFormViewModel => new InstallView(),
			UninstallFormViewModel => new UninstallView(),
			DeployPipelineViewModel => new DeployPipelineView(),
			_ => new TextBlock { Text = "Not Found: " + param.GetType().FullName }
		};
	}

	/// <inheritdoc />
	public bool Match(object? data) {
		return data is ViewModelBase;
	}
}
