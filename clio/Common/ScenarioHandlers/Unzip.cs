using FluentValidation;
using OneOf;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

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
        private readonly IAbstractionsFileSystem _fileSystem;
        private readonly ILogger _logger;
        public UnzipRequestHandler(IValidator<UnzipRequest> validator, IAbstractionsFileSystem fileSystem, ILogger logger)
        {
            _validator = validator;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<OneOf<UnzipResponse, HandlerError>> Handle(UnzipRequest request) {

            _validator.ValidateAndThrow(request);

            string zipFileName = request.Arguments["from"];
            string destinationDirectory = request.Arguments["to"];

            // Ensure the destination directory exists (create it when it does NOT exist).
            if (!_fileSystem.Directory.Exists(destinationDirectory)) {
                _fileSystem.Directory.CreateDirectory(destinationDirectory);
            }

            _logger.BeginSpinner("Extracting files");
            bool success = false;
            try {
                // Open the archive through IFileSystem so the file-open is mockable; the per-entry
                // ExtractToFile below writes bytes via concrete System.IO and is integration-tested.
                using var stream = _fileSystem.File.OpenRead(zipFileName);
                using var archive = new ZipArchive(stream);
                foreach (var entry in archive.Entries) {
                    // Skip directories (entries ending with '/')
                    if (!entry.FullName.EndsWith("/")) {
                        entry.ExtractToFile(_fileSystem.Path.Combine(destinationDirectory, entry.FullName), true);
                    }
                    else {
                        var dir = _fileSystem.Path.GetDirectoryName(_fileSystem.Path.Combine(destinationDirectory, entry.FullName));
                        _fileSystem.Directory.CreateDirectory(dir);
                    }
                }
                success = true;
            }
            finally {
                _logger.EndSpinner(success);
            }

            return Task.FromResult<OneOf<UnzipResponse, HandlerError>>(new UnzipResponse() {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = $"Finished extracting files to {destinationDirectory}"
            });

        }
    }
}
