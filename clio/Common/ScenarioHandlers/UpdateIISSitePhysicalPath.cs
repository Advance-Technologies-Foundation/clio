using System;
using FluentValidation;
using OneOf;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Clio.Command;

namespace Clio.Common.ScenarioHandlers {

    public class UpdateIISSitePhysicalPathRequest : BaseHandlerRequest {
    }

    public class UpdateIISSitePhysicalPathResponse : BaseHandlerResponse {
    }

    public class UpdateIISSitePhysicalPathRequestValidator : AbstractValidator<UpdateIISSitePhysicalPathRequest> {
        public UpdateIISSitePhysicalPathRequestValidator() {
            RuleFor(x => x.Arguments).NotEmpty();

            RuleFor(x => x.Arguments).Cascade(CascadeMode.Stop)
                .Custom((options, context) => {
                    if (!options.ContainsKey("siteName")) {
                        context.AddFailure("updateIISSitePhysicalPath step requires siteName option");
                    }
                })
                .Custom((options, context) => {
                    var siteName = options.ContainsKey("siteName") ? options["siteName"] : string.Empty;
                    if (string.IsNullOrWhiteSpace(siteName)) {
                        context.AddFailure("siteName cannot be empty");
                    }
                })
                .Custom((options, context) => {
                    if (!options.ContainsKey("physicalPath")) {
                        context.AddFailure("updateIISSitePhysicalPath step requires physicalPath option");
                    }
                })
                .Custom((options, context) => {
                    var physicalPath = options.ContainsKey("physicalPath") ? options["physicalPath"] : string.Empty;
                    if (string.IsNullOrWhiteSpace(physicalPath)) {
                        context.AddFailure("physicalPath cannot be empty");
                    }
                    if (!Directory.Exists(physicalPath)) {
                        context.AddFailure($"physicalPath does not exist: '{physicalPath}'");
                    }
                });
        }
    }

    /// <summary>
    /// Handles <see cref="UpdateIISSitePhysicalPathRequest"/> scenario steps by updating the
    /// physical path of an existing IIS site (and its <c>0</c> web application) for a Creatio deployment.
    /// </summary>
    public interface IUpdateIISSitePhysicalPathHandler {

        /// <summary>
        /// Validates the request and, when valid, updates the physical path of the IIS site's
        /// root virtual directory (and the <c>0</c> web application virtual directory when present)
        /// described by the request <c>Arguments</c> (site name and physical path).
        /// </summary>
        /// <param name="request">The request carrying the IIS site physical path arguments.</param>
        /// <returns>
        /// A <see cref="OneOf{T0, T1}"/> containing a <see cref="BaseHandlerResponse"/>
        /// (a <see cref="UpdateIISSitePhysicalPathResponse"/>) on success or a <see cref="HandlerError"/> on failure.
        /// </returns>
        /// <exception cref="FluentValidation.ValidationException">
        /// Thrown when the request fails validation (for example, missing required arguments
        /// or a non-existent physical path).
        /// </exception>
        Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(UpdateIISSitePhysicalPathRequest request);
    }

    internal class UpdateIISSitePhysicalPathRequestHandler : IUpdateIISSitePhysicalPathHandler {
        private readonly IProcessExecutor _processExecutor;
        private readonly ILogger _logger;
        private readonly IValidator<UpdateIISSitePhysicalPathRequest> _validator;

        public UpdateIISSitePhysicalPathRequestHandler(IProcessExecutor processExecutor, ILogger logger, IValidator<UpdateIISSitePhysicalPathRequest> validator) {
            _processExecutor = processExecutor;
            _logger = logger;
            _validator = validator;
        }

        /// <inheritdoc />
        public Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(UpdateIISSitePhysicalPathRequest request) {
            _validator.ValidateAndThrow(request);

            string siteName = request.Arguments["siteName"].Trim();
            string physicalPath = request.Arguments["physicalPath"].Trim();

            StringBuilder sb = new();

            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");

            // Update root virtual directory physical path for the site
            _logger?.WriteInfo($"Setting physical path for root virtual directory '{siteName}/' to: {physicalPath}");
            string command = $"set vdir /vdir.name:\"{siteName}/\" /physicalPath:\"{physicalPath}\"";
            try {
                string result = _processExecutor.Execute(appcmdPath, command, true);
                sb.AppendLine(result);
            }
            catch (Exception ex)
            {
                var msg = $"Failed to update physical path for site '{siteName}', {ex.Message}";
                _logger?.WriteError(msg);
                return Task.FromResult<OneOf<BaseHandlerResponse, HandlerError>>(new UpdateIISSitePhysicalPathResponse {
                    Status = BaseHandlerResponse.CompletionStatus.Failure,
                    Description = msg
                });
            }

            // Update Web application '0' virtual directory to point to physicalPath\Terrasoft.WebApp
            string webAppPath = Path.Combine(physicalPath, "Terrasoft.WebApp");
            if (Directory.Exists(webAppPath)) {
                _logger?.WriteInfo($"Setting physical path for virtual directory '{siteName}/0/' to: {webAppPath}");
                command = $"set vdir /vdir.name:\"{siteName}/0/\" /physicalPath:\"{webAppPath}\"";
                try {
                    string resultApp = _processExecutor.Execute(appcmdPath, command, true);
                    sb.AppendLine(resultApp);
                }
                catch (Exception ex) {
                    var msg = $"Failed to update physical path for application '0' under site '{siteName}', {ex.Message}";
                    _logger?.WriteError(msg);
                    return Task.FromResult<OneOf<BaseHandlerResponse, HandlerError>>(new UpdateIISSitePhysicalPathResponse {
                        Status = BaseHandlerResponse.CompletionStatus.Failure,
                        Description = msg
                    });
                }
            }

            return Task.FromResult<OneOf<BaseHandlerResponse, HandlerError>>(new UpdateIISSitePhysicalPathResponse {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = sb.ToString()
            });
        }
    }
}
