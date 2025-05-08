using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using CommandLine;
using Common;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Requests;

using ValidationException = FluentValidation.ValidationException;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Clio.Command;

[Verb("externalLink", Aliases = new[] { "link" }, HelpText = "Handle external deep-links")]
public class ExternalLinkOptions : EnvironmentOptions
{

    // Default to make sure we dont throw prematurely
    [Value(0, Default = "")]
    public string Content { get; set; }

    public override bool ShowDefaultEnvironment() => false;
}

public class ExternalLinkCommand(IMediator mediator, IValidator<ExternalLinkOptions> validator, ILogger logger): Command<ExternalLinkOptions>
{
    private readonly IMediator _mediator = mediator;
    private readonly IValidator<ExternalLinkOptions> _validator = validator;
    private readonly ILogger _logger = logger;




    /// <summary>
    /// Make sure to call clio register before testing, see reg/clio_context_menu_win.reg Lines 20-24 (protocol registration)
    /// To test execute the following the command line:
    /// clio-dev externalLink clio://commandName/?argName=argValue.
    /// </summary>
    /// <param name="options">Command areguments.</param>
    /// <returns>1 on fail, 0 on success.</returns>
    /// <remarks>
    /// See <see cref="Requests.Validators.ExternalLinkOptionsValidator"/> for validation details.
    /// </remarks>
    public override int Execute(ExternalLinkOptions options)
    {
        ValidationResult validationResult = _validator.Validate(options);
        if (!validationResult.IsValid)
        {
            PrintError(validationResult.Errors);
            return 1;
        }

        Uri _uri = new (options.Content, UriKind.Absolute);
        Type runtimeType = GetType().Assembly.GetTypes()
            .FirstOrDefault(t => t.FullName.ToLower(CultureInfo.InvariantCulture) == $"clio.requests.{_uri.Host}");

        IExternalLink xRequest = Activator.CreateInstance(runtimeType, true) as IExternalLink;
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
                _logger.WriteError(ex.Message);
            }
        }).Wait();
        return 0;
    }

    private void PrintError(IEnumerable<ValidationFailure> errors) =>
        errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
            .ToList().ForEach(e =>
            {
                _logger.WriteError($"{e.Severity.ToString().ToUpper()} ({e.ErrorCode}) - {e.ErrorMessage}");
            });
}
