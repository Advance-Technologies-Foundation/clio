namespace Clio.Command {
	using Clio.Requests;
	using CommandLine;
	using FluentValidation;
	using FluentValidation.Results;
	using MediatR;
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Threading.Tasks;
	using ValidationException = FluentValidation.ValidationException;
	using ValidationResult = FluentValidation.Results.ValidationResult;


	#region Class: ExternalLinkOptions

	[Verb("externalLink", Aliases = new[] { "link" }, HelpText = "Handle external deep-links")]
	public class ExternalLinkOptions : EnvironmentOptions {
		#region Properties: Public

		// Default to make sure we dont throw prematurely
		[Value(0, Default = "")]
		public string Content {
			get; set;
		}

		#endregion
	}

	#endregion

	#region Class: ExternalLinkCommand

	public class ExternalLinkCommand : Command<ExternalLinkOptions> {

		#region Fields: Private
		private readonly IMediator _mediator;
		private readonly IValidator<ExternalLinkOptions> _validator;
		#endregion

		#region Constructors: Public

		public ExternalLinkCommand(IMediator mediator, IValidator<ExternalLinkOptions> validator) {
			_mediator = mediator;
			_validator = validator;
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Make sure to call clio register before testing, see reg/clio_context_menu_win.reg Lines 20-24 (protocol registration)
		/// To test execute the following the command line:
		/// clio-dev externalLink clio://commandName/?argName=argValue
		/// </summary>
		/// <param name="options">Command areguments</param>
		/// <returns>1 on fail, 0 on success</returns>
		/// <remarks>
		/// See <see cref="Requests.Validators.ExternalLinkOptionsValidator"/> for validation details
		/// </remarks>
		public override int Execute(ExternalLinkOptions options) {
			ValidationResult validationResult = _validator.Validate(options);
			if (!validationResult.IsValid)
			{
				PrintError(validationResult.Errors);
				return 1;
			}

			Uri _uri = new(options.Content, UriKind.Absolute);
			Type runtimeType = GetType().Assembly.GetTypes()
				.FirstOrDefault(t => t.FullName.ToLower(CultureInfo.InvariantCulture) == $"clio.requests.{_uri.Host}");

			IExtenalLink xRequest = Activator.CreateInstance(runtimeType, true) as IExtenalLink;
			xRequest.Content = options.Content;

			Task.Run(async () =>
			{
				try
				{
					await _mediator.Send(xRequest);
				}
				catch (ValidationException vex)
				{
					PrintError(vex.Errors);
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.Message);
				}

			}).Wait();
			return 0;
		}
		#endregion

		private void PrintError(IEnumerable<ValidationFailure> errors) {
			errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
			.ToList().ForEach(e =>
			{
				Console.WriteLine($"{e.Severity.ToString().ToUpper()} ({e.ErrorCode}) - {e.ErrorMessage}");
			});

		}
	}
	#endregion
}
