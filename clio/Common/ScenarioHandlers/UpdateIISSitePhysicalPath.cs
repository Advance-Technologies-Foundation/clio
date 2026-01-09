using System;
using FluentValidation;
using MediatR;
using OneOf;
using System.IO;
using System.Text;
using System.Threading;
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

    internal class UpdateIISSitePhysicalPathRequestHandler : IRequestHandler<UpdateIISSitePhysicalPathRequest, OneOf<BaseHandlerResponse, HandlerError>> {
        private readonly IProcessExecutor _processExecutor;
        private readonly ILogger _logger;

        public UpdateIISSitePhysicalPathRequestHandler(IProcessExecutor processExecutor, ILogger logger) {
            _processExecutor = processExecutor;
            _logger = logger;
        }

        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(UpdateIISSitePhysicalPathRequest request, CancellationToken cancellationToken) {
            string siteName = request.Arguments["siteName"].Trim();
            string physicalPath = request.Arguments["physicalPath"].Trim();

            StringBuilder sb = new();

            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");

            // Update root virtual directory physical path for the site
            string command = $"set vdir /vdir.name:\"{siteName}/\" /physicalPath:\"{physicalPath}\"";
            try {
                string result = _processExecutor.Execute(appcmdPath, command, true);
                sb.AppendLine(result);
            }
            catch (Exception ex)
            {
                var msg = $"Failed to update physical path for site '{siteName}', {ex.Message}";
                _logger?.WriteError(msg);
                return new UpdateIISSitePhysicalPathResponse {
                    Status = BaseHandlerResponse.CompletionStatus.Failure,
                    Description = msg
                };
            }

            return new UpdateIISSitePhysicalPathResponse {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = sb.ToString()
            };
        }
    }
}

