using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using OneOf;

namespace Clio.Common.ScenarioHandlers;

public class UnzipRequest : BaseHandlerRequest
{ }

public class UnzipResponse : BaseHandlerResponse
{ }

public class UnzipRequestValidator : AbstractValidator<UnzipRequest>
{

    #region Constructors: Public

    public UnzipRequestValidator()
    {
        RuleFor(x => x.Arguments).NotNull().WithMessage("Unzip step requires options");
        RuleFor(x => x.Arguments).Cascade(CascadeMode.Stop)
                                 .Custom((options, context) =>
                                 {
                                     if (!options.ContainsKey("from"))
                                     {
                                         context.AddFailure(new ValidationFailure("from",
                                             "Unzip step requires from option"));
                                     }
                                 })
                                 .Custom((options, context) =>
                                 {
                                     string fileName = options["from"];
                                     if (!File.Exists(fileName))
                                     {
                                         context.AddFailure(new ValidationFailure("from",
                                             $"File does not exist: {fileName}"));
                                     }
                                 });
        RuleFor(x => x.Arguments).Custom((options, context) =>
        {
            if (!options.ContainsKey("to"))
            {
                context.AddFailure(new ValidationFailure("to", "Unzip step requires to option"));
            }
        });
    }

    #endregion

}

internal class UnzipRequestHandler : IRequestHandler<UnzipRequest, OneOf<BaseHandlerResponse, HandlerError>>
{

    #region Fields: Private

    private readonly char[] _sequence;
    private int _counter;

    #endregion

    #region Constructors: Public

    public UnzipRequestHandler()
    {
        _sequence = new[]
        {
            '/', '-', '\\', '|'
        };
        _counter = 0;
    }

    #endregion

    #region Methods: Private

    private void Turn()
    {
        _counter++;
        if (_counter >= _sequence.Length)
        {
            _counter = 0;
        }
        (int Left, int Top) position = Console.GetCursorPosition();
        Console.Write(_sequence[_counter]);
        Console.SetCursorPosition(position.Left, position.Top);
    }

    #endregion

    #region Methods: Public

    public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(UnzipRequest request,
        CancellationToken cancellationToken)
    {
        string zipFileName = request.Arguments["from"];
        string destinationDirectory = request.Arguments["to"];

        _ = !Directory.Exists(destinationDirectory) ? null : Directory.CreateDirectory(destinationDirectory);

        using ZipArchive archive = ZipFile.OpenRead(zipFileName);
        await Console.Out.WriteAsync("Extracting files: ");
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // Skip directories (entries ending with '/')
            if (!entry.FullName.EndsWith("/"))
            {
                entry.ExtractToFile(Path.Combine(destinationDirectory, entry.FullName), true);
                Turn();
            }
            else
            {
                string dir = Path.GetDirectoryName(Path.Combine(destinationDirectory, entry.FullName));
                Directory.CreateDirectory(dir);
            }
        }
        return new UnzipResponse
        {
            Status = BaseHandlerResponse.CompletionStatus.Success,
            Description = $"Finished extracting files to {destinationDirectory}"
        };
    }

    #endregion

}
