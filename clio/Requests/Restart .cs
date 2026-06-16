using Clio.Command;
using System;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class Restart : IExternalLink
	{
		public string Content {
			get; set;
		}
	}

	/// <summary>
	/// Restarts environment
	/// </summary>
	/// <remarks>
	/// Handles requests received via 
	/// clio://Restart/?environmentName=bundle8055
	/// </remarks>
	internal class RestartHandler : BaseExternalLinkHandler, IExternalLinkHandler
	{
		private readonly RestartCommand _restartCommand;

		public RestartHandler(RestartCommand restartCommand)
		{
			_restartCommand = restartCommand;
		}

		public Type RequestType => typeof(Restart);

		public Task Handle(IExternalLink request)
		{
			Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
			RestartOptions opt = new()
			{
				Environment = ClioParams["environmentName"]
			};
			_restartCommand.Execute(opt);
			return Task.CompletedTask;
		}
	}
}
