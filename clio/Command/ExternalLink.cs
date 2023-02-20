namespace Clio.Command
{
	using Clio.Requests;
	using CommandLine;
	using MediatR;
	using System;
	using System.Linq;
	using System.Threading.Tasks;


	#region Class: ExternalLinkOptions

	[Verb("externalLink", Aliases = new string[] { "link" }, HelpText = "Handle external deep-links")]
	public class ExternalLinkOptions : EnvironmentOptions
	{
		#region Properties: Public

		// Default to make sure we dont throw prematurely
		[Value(0, Default = "")]
		public string Content
		{
			get; set;
		}

		#endregion
	}

	#endregion

	#region Class: ExternalLinkCommand

	public class ExternalLinkCommand : Command<ExternalLinkOptions>
	{

		#region Fields: Private
		private readonly IMediator _mediator;
		#endregion

		#region Constructors: Public

		public ExternalLinkCommand(IMediator mediator)
		{
			_mediator = mediator;
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Make sure to call clio register before testing, see reg/clio_context_menu_win.reg Lines 20-24 (protocol registration)
		/// To test execute the following the command line:
		/// clio-dev externalLink clio://commandName/?protocol=https:&host=129117-crm-bundle.creatio.com&name=vscode&clientId=83B03D807E3DEEAEF6A55D8CB587E191&clientSecret=C6EA75A49446A63F239BEB4C89892A610E638063AC298EEAF6786E309E06970C
		/// </summary>
		/// <param name="options"></param>
		/// <returns></returns>
		public override int Execute(ExternalLinkOptions options)
		{
			if (!ValidateInput(options, out string commandName))
			{
				return 1;
			}

			Type runtimeType = GetType().Assembly.GetTypes()
				.Where(t => t.FullName.ToLower() == $"clio.requests.{commandName}")
				.FirstOrDefault();


			if (runtimeType == null)
			{
				Console.WriteLine($"Could not match {commandName} command with implemented type - Command {commandName} Not implemented.");
				return 1;
			}


			IExtenalLink xRequest = (IExtenalLink)Activator.CreateInstance(runtimeType, true);
			xRequest.Content = options.Content;

			Task.Run(async () =>
			{
				await _mediator.Send(xRequest);
			}).Wait();
			return 0;
		}
		#endregion


		private bool ValidateInput(in ExternalLinkOptions options, out string commandName)
		{

			commandName = string.Empty;
			if (string.IsNullOrEmpty(options.Content))
			{
				Console.WriteLine("Clio url cannot be emty");
				return false;
			}

			if (!Uri.TryCreate(options.Content, UriKind.Absolute, out Uri _uri))
			{
				Console.WriteLine("Could not parse clio link");
				return false;
			}

			commandName = _uri.Host;
			if (string.IsNullOrEmpty(commandName))
			{
				Console.WriteLine(
				"Action Name missing:"
				+ Environment.NewLine +
				"clio url has to follow the formar clio://actionName/?param1=val1&param2=val2");
			}


			return true;
		}

	}
	#endregion
}
