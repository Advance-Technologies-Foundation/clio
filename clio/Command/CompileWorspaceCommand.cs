using System;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("build-workspace", Aliases = new[] { "build", "compile", "compile-all", "rebuild" }, HelpText = "Build/Rebuild worksapce for selected environment")]
	public class CompileOptions : RemoteCommandOptions
	{
		[Option('o', "ModifiedItems", HelpText = "Build modified items")]
		public bool ModifiedItems { get; set; }
	}


	public class CompileWorkspaceCommand : RemoteCommand<CompileOptions>
	{
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private bool _isSuccess;

		public CompileWorkspaceCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
			IServiceUrlBuilder serviceUrlBuilder)
			: base(applicationClient, settings) {
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		private bool _modifiedItems;

		protected override string ServicePath => _modifiedItems
			? _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Compile)
			: _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileAll);

		protected override string GetRequestData(CompileOptions options) => string.Empty;

		public override int Execute(CompileOptions options) {
			_modifiedItems = options.ModifiedItems;
			return base.Execute(options);
		}

		protected override void ProceedResponse(string response, CompileOptions options) {
			base.ProceedResponse(response, options);
			try {
				if (string.IsNullOrWhiteSpace(response)) {
					CommandSuccess = _isSuccess = false;
					Logger.WriteError("Empty response received from server during compilation.");
					Logger.WriteError($"Endpoint: {ServiceUri}");
					return;
				}
				string trimmed = response.TrimStart();
				if (trimmed.StartsWith("<", StringComparison.Ordinal)) {
					CommandSuccess = _isSuccess = false;
					Logger.WriteError("Server returned non-JSON response during compilation (looks like HTML).");
					Logger.WriteError($"Endpoint: {ServiceUri}");
					return;
				}
				CreatioResponse model = JsonSerializer.Deserialize<CreatioResponse>(response);
				CommandSuccess = _isSuccess = model.Success;
				if (!model.Success) {
					Logger.WriteError($"{model.ErrorInfo.ErrorCode}: {model.ErrorInfo.Message}");
				}
			}
			catch (Exception e) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError(e.Message);
				Logger.WriteError($"Endpoint: {ServiceUri}");
				if (!string.IsNullOrWhiteSpace(response)) {
					Logger.WriteError("Full response:");
					Logger.WriteLine(response);
				}
			}
		}
	}

}
