using Clio.Command;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class Restart : IExtenalLink
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
	internal class RestartHandler : BaseExternalLinkHandler, IRequestHandler<Restart>
	{
		private readonly RestartCommand _restartCommand;

		public RestartHandler(RestartCommand restartCommand)
		{
			_restartCommand = restartCommand;
		}

		public Task Handle(Restart request, CancellationToken cancellationToken)
		{
			Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
			RestartOptions opt = new()
			{
				Environment = ClioParams["environmentName"]
			};
			_restartCommand.Execute(opt);
			return Unit.Task;
		}
	}
}
