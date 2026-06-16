using FluentValidation;
using OneOf;
using System;
using System.IO;
using System.IO.Compression;
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


    /// <summary>
    /// Handles <see cref="UnzipRequest"/> scenario steps by extracting a zip archive
    /// to a target directory.
    /// </summary>
    public interface IUnzipHandler {

        /// <summary>
        /// Validates the request and, when valid, extracts the archive referenced by the
        /// <c>from</c> argument into the directory referenced by the <c>to</c> argument.
        /// </summary>
        /// <param name="request">The unzip request carrying the <c>from</c> and <c>to</c> arguments.</param>
        /// <returns>
        /// A <see cref="OneOf{T0, T1}"/> containing an <see cref="UnzipResponse"/> on success
        /// or a <see cref="HandlerError"/> on failure.
        /// </returns>
        /// <exception cref="FluentValidation.ValidationException">
        /// Thrown when the request fails validation (for example, missing <c>from</c>/<c>to</c>
        /// arguments or a non-existent source file).
        /// </exception>
        Task<OneOf<UnzipResponse, HandlerError>> Handle(UnzipRequest request);
    }


    internal class UnzipRequestHandler : IUnzipHandler {

        private readonly IValidator<UnzipRequest> _validator;
        private readonly char[] _sequence;
        private int _counter;
        public UnzipRequestHandler(IValidator<UnzipRequest> validator)
        {
            _validator = validator;
            _sequence = new[] { '/', '-', '\\', '|' };
            _counter = 0;
        }

        /// <inheritdoc />
        public async Task<OneOf<UnzipResponse, HandlerError>> Handle(UnzipRequest request) {

            _validator.ValidateAndThrow(request);

            string zipFileName = request.Arguments["from"];
            string destinationDirectory = request.Arguments["to"];

            _ = !Directory.Exists(destinationDirectory) ? null : Directory.CreateDirectory(destinationDirectory);

            using var archive = ZipFile.OpenRead(zipFileName);
#pragma warning disable CLIO002
            await Console.Out.WriteAsync("Extracting files: ");
#pragma warning restore CLIO002
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
#pragma warning disable CLIO002
            Console.Write(_sequence[_counter]);
#pragma warning restore CLIO002
            Console.SetCursorPosition(position.Left, position.Top);
        }
    }
}
