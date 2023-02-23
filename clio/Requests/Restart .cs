using Clio.Command;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class Restart : IExtenalLink
	{
		public string Content
		{
			get; set;
		}
	}

	/// <summary>
	/// Restarts environment
	/// </summary>
	/// <remarks>
	/// Handles requests received via 
	/// clio://Restart?environmentName=bundle8055
	/// </remarks>
	public class RestartHandler : BaseExternalLinkHandler, IRequestHandler<Restart>
	{
		private readonly RestartCommand _restartCommand;

		public RestartHandler(RestartCommand restartCommand)
		{
			_restartCommand = restartCommand;
		}


		public Task<Unit> Handle(Restart request, CancellationToken cancellationToken)
		{

			if (!IsLinkValid(request.Content))
				return Unit.Task;

#if (DEBUG)
			PrintArguments();
#endif

			RestartOptions opt = new RestartOptions
			{
				Environment = clioParams["environmentName"]
			};

			_restartCommand.Execute(opt);
			return Unit.Task;
		}
	}
}
