using System;
using FluentValidation;
using MediatR;
using OneOf;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;

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
        private readonly ILogger _logger;

        public CreateIISSiteRequestHandler(IProcessExecutor processExecutor, ILogger logger) {
            _processExecutor = processExecutor;
            _logger = logger;
        }


        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(CreateIISSiteRequest request, CancellationToken cancellationToken) {
            
            string siteName = request.Arguments["siteName"].Trim();
            int sitePort = int.Parse(request.Arguments["port"].Trim());
            string sourceDirectory = request.Arguments["sourceDirectory"];
            string destinationFolder = Path.Combine(request.Arguments["destinationDirectory"].Trim(), siteName);
            bool isNetFramework = bool.Parse(request.Arguments["isNetFramework"]);
            
            StringBuilder sb = new();

            if (sourceDirectory != destinationFolder) {
                CopyFiles(sourceDirectory,destinationFolder);
                sb.AppendLine($"Copied directory");
                sb.Append("\tfrom: ").AppendLine(sourceDirectory)
                    .Append("\tto: ").AppendLine(destinationFolder);
            }
            else {
                sb.AppendLine($"Directory already exists: {destinationFolder} skipped copying");
            }

            sb.Append(CreateAppPool(siteName, isNetFramework));
            sb.Append(CreateWebSite(siteName, sitePort, destinationFolder));
            if(isNetFramework) {
                sb.Append(CreateWebApplication(siteName, Path.Combine(destinationFolder, "Terrasoft.WebApp")));
            }

            return new CreateIISSiteResponse {
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
            string result = _processExecutor.Execute(appcmdPath, command, true);
            return result;
        }

        private string CreateWebSite(string siteName, int port, string destinationFolder) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            //string command = $"add site /name:\"{siteName}\" /bindings:\"http/*:{port}:\" /physicalPath:\"{destinationFolder}\" /applicationDefaults.applicationPool:\"{siteName}\"";
            string command = $"add site /name:\"{siteName}\" /bindings:\"http/*:{port}:{InstallerHelper.FetFQDN()}\" /physicalPath:\"{destinationFolder}\" /applicationDefaults.applicationPool:\"{siteName}\"";
            
            var result =  _processExecutor.Execute(appcmdPath, command, true);

            // Enable Basic Authentication for the created site (moved to helper)
            var basicResult = EnableBasicAuthentication(siteName);

            // Return combined output so caller sees both results
            return result + Environment.NewLine + "EnableBasicAuthentication: " + basicResult;
        }

        private string EnableBasicAuthentication(string siteName) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            // appcmd syntax: set config "<siteName>" -section:system.webServer/security/authentication/basicAuthentication /enabled:true
            string section = "system.webServer/security/authentication/basicAuthentication";

            // Always try to unlock the section first (idempotent)
            string unlockCmd = $"unlock config -section:{section}";
            var unlockResult = _processExecutor.Execute(appcmdPath, unlockCmd, true);

            // Now try to enable basic auth for the site
            string enableBasicCmd = $"set config \"{siteName}\" -section:{section} /enabled:true";
            var basicResult = _processExecutor.Execute(appcmdPath, enableBasicCmd, true);

            // Return both outputs so caller can see the unlock and enable results
            return "UnlockResult: " + unlockResult + Environment.NewLine + "EnableResult: " + basicResult;
        }

        private string CreateWebApplication(string siteName, string physicalPath) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            string command = $"add app /site.name:\"{siteName}\" /path:\"/0\" /physicalPath:\"{physicalPath}\" /applicationPool:\"{siteName}\"";
            string result =  _processExecutor.Execute(appcmdPath, command, true);
            return result;
        }
        
        private static void CopyFiles(string sourceDirectory, string destinationDirectory) {
            DirectoryInfo diSource = new(sourceDirectory);
            DirectoryInfo diTarget = new(destinationDirectory);
            CopyAll(diSource, diTarget);
        }

        private static void CopyAll(DirectoryInfo source, DirectoryInfo target) {
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
