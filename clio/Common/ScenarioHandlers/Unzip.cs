using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using FluentValidation;
using MediatR;
using OneOf;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.ScenarioHandlers {


    public class UnzipRequest : BaseHandlerRequest {
    }



    public class UnzipResponse : BaseHandlerResponse {
    }


    public class UnzipRequestValidator : AbstractValidator<UnzipRequest> {
        public UnzipRequestValidator() {
            RuleFor(x => x.Arguments).NotNull().WithMessage("Unzip step requires options");
            RuleFor(x => x.Arguments).Cascade(CascadeMode.Stop)
                .Custom((options, context) => {
                    if (!options.ContainsKey("from")) {
                        context.AddFailure(new FluentValidation.Results.ValidationFailure("from", "Unzip step requires from option"));
                    }
                })
                .Custom((options, context) => {
                    
                    string fileName = options["from"];
                    if (!File.Exists(fileName)) {
                        context.AddFailure(new FluentValidation.Results.ValidationFailure("from", $"File does not exist: {fileName}"));
                    }
                });
            RuleFor(x => x.Arguments).Custom((options, context) => {
                if (!options.ContainsKey("to")) {
                    context.AddFailure(new FluentValidation.Results.ValidationFailure("to", "Unzip step requires to option"));
                }
            });
        }
    }


    internal class UnzipRequestHandler : IRequestHandler<UnzipRequest, OneOf<BaseHandlerResponse, HandlerError>> {
        
        private readonly char[] _sequence;
        private int _counter;
        public UnzipRequestHandler()
        {
            _sequence = new[] { '/', '-', '\\', '|' };
            _counter = 0;
        }
        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(UnzipRequest request, CancellationToken cancellationToken) {

            string zipFileName = request.Arguments["from"];
            string destinationDirectory = request.Arguments["to"];

            _ = !Directory.Exists(destinationDirectory) ? null : Directory.CreateDirectory(destinationDirectory);

            using var archive = ZipFile.OpenRead(zipFileName);
            await Console.Out.WriteAsync("Extracting files: ");
            foreach (var entry in archive.Entries) {
                // Skip directories (entries ending with '/')
                if (!entry.FullName.EndsWith("/")) {
                    entry.ExtractToFile(Path.Combine(destinationDirectory, entry.FullName), true);
                    Turn();
                }
                else {
                    var dir = Path.GetDirectoryName(Path.Combine(destinationDirectory, entry.FullName));
                    Directory.CreateDirectory(dir);
                }
            }
            return new UnzipResponse() {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = $"Finished extracting files to {destinationDirectory}"
            };

        }

        private void Turn() {
            _counter++;
            if (_counter >= _sequence.Length) {
                _counter = 0;
            }
            var position = Console.GetCursorPosition();
            Console.Write(_sequence[_counter]);
            Console.SetCursorPosition(position.Left, position.Top);
        }
    }
}
