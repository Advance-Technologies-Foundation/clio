using FluentValidation;
using MediatR;
using OneOf;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.ScenarioHandlers {

    public class CreateIISSiteRequest : BaseHandlerRequest {
    }

    public class CreateIISSiteResponse : BaseHandlerResponse {
    }

    public class CreateIISSiteRequestValidator: AbstractValidator<CreateIISSiteRequest> {
        public CreateIISSiteRequestValidator() {
            RuleFor(x => x.Arguments).NotEmpty();

            RuleFor(x=> x.Arguments).Cascade(CascadeMode.Stop)
                .Custom((options, context) => {
                    if (!options.ContainsKey("sourceDirectory")) {
                        context.AddFailure("createIISSite step requires sourceDirectory options");
                    }
                })
                .Custom((options, context) => {
                    var sourceDir = options["sourceDirectory"];
                    if (!Directory.Exists(sourceDir)) {
                        context.AddFailure($"sourceDirectory does not exist: '{sourceDir}'");
                    }
                })
                .Custom((options, context) => {
                    if (!options.ContainsKey("destinationDirectory")) {
                        context.AddFailure("createIISSite step requires destinationDirectory options");
                    }
                })
                .Custom((options, context) => {
                    var destDir = options["destinationDirectory"];
                    if (!Directory.Exists(destDir)) {
                        context.AddFailure($"destinationDirectory does not exist: '{destDir}' ");
                    }
                })
                .Custom((options, context) => {
                    if (!options.ContainsKey("siteName")) {
                        context.AddFailure("createIISSite step requires siteName options");
                    }
                })
                .Custom((options, context) => {
                    var siteName = options["siteName"];
                    if (string.IsNullOrWhiteSpace(siteName)) {
                        context.AddFailure($"siteName cannot be empty");
                    }
                })
                .Custom((options, context) => {
                    if (!options.ContainsKey("port")) {
                        context.AddFailure("createIISSite step requires port options");
                    }
                })
                .Custom((options, context) => {
                    var port = options["port"];
                    if (string.IsNullOrWhiteSpace(port)) {
                        context.AddFailure($"port cannot be empty");
                    }
                    if(!int.TryParse(port, out int portNumber)) {
                        context.AddFailure($"port: '{port}' is not a valid port number");
                    }
                })
                .Custom((options, context) => {
                    var isNetFramework = options["isNetFramework"];
                    if (string.IsNullOrWhiteSpace(isNetFramework)) {
                        context.AddFailure($"isNetFramework cannot be empty");
                    }
                    if(!bool.TryParse(isNetFramework, out bool _isNetFramework)) {
                        context.AddFailure($"isNetFramework: '{isNetFramework}' is not a valid boolean value");
                    }
                });
        }
    }



    internal class CreateIISSiteRequestHandler : IRequestHandler<CreateIISSiteRequest, OneOf<BaseHandlerResponse, HandlerError>> {
        private readonly IProcessExecutor _processExecutor;
        
        public CreateIISSiteRequestHandler(IProcessExecutor processExecutor)
        {
            _processExecutor = processExecutor;
        }


        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(CreateIISSiteRequest request, CancellationToken cancellationToken) {
            
            string siteName = request.Arguments["siteName"].Trim();
            int sitePort = int.Parse(request.Arguments["port"].Trim());
            string sourceDirectory = request.Arguments["sourceDirectory"];
            string destinationFolder = Path.Join(request.Arguments["destinationDirectory"].Trim(), siteName);
            bool isNetFramework = bool.Parse(request.Arguments["isNetFramework"]);
            
            StringBuilder sb = new();
            
            CopyFiles(sourceDirectory,destinationFolder);
            sb.AppendLine($"Copied directory");
            sb.Append("\tfrom: ").AppendLine(sourceDirectory)
                .Append("\tto: ").AppendLine(destinationFolder);

            sb.Append(CreateAppPool(siteName, isNetFramework));
            sb.Append(CreateWebSite(siteName, sitePort, destinationFolder));
            if(isNetFramework) {
                sb.Append(CreateWebApplication(siteName, Path.Join(destinationFolder, "Terrasoft.WebApp")));
            }

            return new CreateIISSiteResponse() {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = sb.ToString()
            };
        }


        private string CreateAppPool(string poolName, bool isNetFramework = true) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            string command;
            if (isNetFramework) {
                command = $"add apppool /name:{poolName}";
            }
            else {
                command = $"add apppool /name:{poolName} /managedRuntimeVersion:\"\" /managedPipelineMode:\"Integrated\"";
            }
            return _processExecutor.Execute(appcmdPath, command, true);
        }

        private string CreateWebSite(string siteName, int port, string destinationFolder) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            string command = $"add site /name:\"{siteName}\" /bindings:\"http/*:{port}:\" /physicalPath:\"{destinationFolder}\" /applicationDefaults.applicationPool:\"{siteName}\"";
            return _processExecutor.Execute(appcmdPath, command, true);
        }

        private string CreateWebApplication(string siteName, string physicalPath) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            string command = $"add app /site.name:\"{siteName}\" /path:\"/0\" /physicalPath:\"{physicalPath}\" /applicationPool:\"{siteName}\"";
            return _processExecutor.Execute(appcmdPath, command, true);
        }

        private void CopyFiles(string sourceDirectory, string destinationDirectory) {
            
            DirectoryInfo diSource = new(sourceDirectory);
            DirectoryInfo diTarget = new(destinationDirectory);
            CopyAll(diSource, diTarget);

        }
        
        public void CopyAll(DirectoryInfo source, DirectoryInfo target) {
            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles()) {
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }

            // Copy each subdirectory using recursion.
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories()) {
                
                if(diSourceSubDir.Name != "db") {
                    DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
                    CopyAll(diSourceSubDir, nextTargetSubDir);
                }
                
            }
        }
    }
}
