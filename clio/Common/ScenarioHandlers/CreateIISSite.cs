using System;
using FluentValidation;
using OneOf;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common.IIS;

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
                })
                .Custom((options, context) => {
                    options.TryGetValue("protocol", out string protocol);
                    if (protocol is not ("http" or "https")) {
                        context.AddFailure("createIISSite step requires protocol http or https");
                    }
                    if (!options.TryGetValue("hostName", out string hostName) || string.IsNullOrWhiteSpace(hostName)) {
                        context.AddFailure("createIISSite step requires hostName options");
                    }
                    if (protocol == "https" && (!options.TryGetValue("certificateThumbprint", out string thumbprint)
                        || string.IsNullOrWhiteSpace(thumbprint))) {
                        context.AddFailure("createIISSite HTTPS step requires certificateThumbprint options");
                    }
                });
        }
    }
    
    /// <summary>
    /// Handles <see cref="CreateIISSiteRequest"/> scenario steps by creating an IIS
    /// application pool, website and bindings for a Creatio deployment.
    /// </summary>
    public interface ICreateIISSiteHandler {

        /// <summary>
        /// Validates the request and, when valid, creates the IIS application pool and
        /// website described by the request <c>Arguments</c> (site name, port, source and
        /// destination directories, and the .NET Framework flag).
        /// </summary>
        /// <param name="request">The request carrying the IIS site creation arguments.</param>
        /// <returns>
        /// A <see cref="OneOf{T0, T1}"/> containing a <see cref="BaseHandlerResponse"/>
        /// (a <see cref="CreateIISSiteResponse"/>) on success or a <see cref="HandlerError"/> on failure.
        /// </returns>
        /// <exception cref="FluentValidation.ValidationException">
        /// Thrown when the request fails validation (for example, missing required arguments
        /// or non-existent source/destination directories).
        /// </exception>
        Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(CreateIISSiteRequest request);
    }

    internal class CreateIISSiteRequestHandler : ICreateIISSiteHandler {
        private readonly IProcessExecutor _processExecutor;
        private readonly ILogger _logger;
        private readonly IValidator<CreateIISSiteRequest> _validator;
        private readonly INetFrameworkHttpsConfigurator _netFrameworkHttpsConfigurator;
        private readonly IIisCertificateBindingService _certificateBindingService;

        /// <summary>Initializes the IIS site scenario handler.</summary>
        /// <param name="processExecutor">Executes AppCmd operations.</param>
        /// <param name="logger">Writes command diagnostics.</param>
        /// <param name="validator">Validates scenario arguments before mutation.</param>
        /// <param name="netFrameworkHttpsConfigurator">Applies .NET Framework HTTPS configuration.</param>
        /// <param name="certificateBindingService">Attaches a machine certificate to an HTTPS binding.</param>
        public CreateIISSiteRequestHandler(IProcessExecutor processExecutor, ILogger logger, IValidator<CreateIISSiteRequest> validator,
            INetFrameworkHttpsConfigurator netFrameworkHttpsConfigurator,
            IIisCertificateBindingService certificateBindingService) {
            _processExecutor = processExecutor;
            _logger = logger;
            _validator = validator;
            _netFrameworkHttpsConfigurator = netFrameworkHttpsConfigurator;
            _certificateBindingService = certificateBindingService;
        }


        /// <inheritdoc />
        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(CreateIISSiteRequest request) {

            _validator.ValidateAndThrow(request);

            string siteName = request.GetRequired("siteName").Trim();
            int sitePort = request.GetRequired<int>("port");
            string sourceDirectory = request.GetRequired("sourceDirectory");
            string destinationFolder = Path.Combine(request.GetRequired("destinationDirectory").Trim(), siteName);
            bool isNetFramework = request.GetRequired<bool>("isNetFramework");
            string protocol = request.GetRequired("protocol");
            string hostName = request.GetRequired("hostName");
            request.Arguments.TryGetValue("certificateThumbprint", out string certificateThumbprint);
            certificateThumbprint ??= string.Empty;
            
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

            if (isNetFramework && protocol == "https") {
                _netFrameworkHttpsConfigurator.Configure(destinationFolder);
                sb.AppendLine("Configured .NET Framework HTTPS settings.");
            }

            sb.Append(CreateAppPool(siteName, isNetFramework));
            sb.Append(CreateWebSite(siteName, sitePort, destinationFolder, protocol, hostName));
            if (protocol == "https") {
                _certificateBindingService.Attach(siteName, certificateThumbprint);
                sb.AppendLine($"Attached HTTPS certificate {certificateThumbprint} from LocalMachine/My.");
            }
            if(isNetFramework) {
                sb.Append(CreateWebApplication(siteName, Path.Combine(destinationFolder, "Terrasoft.WebApp")));
            }
            sb.Append("EnableWindowsAuthentication: ").AppendLine(EnableWindowsAuthentication(siteName));

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

        private string CreateWebSite(string siteName, int port, string destinationFolder, string protocol, string hostName) {
            string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
            string command = $"add site /name:\"{siteName}\" /bindings:\"{protocol}/*:{port}:{hostName}\" /physicalPath:\"{destinationFolder}\" /applicationDefaults.applicationPool:\"{siteName}\"";
            
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

        private string EnableWindowsAuthentication(string siteName) {
            // Best-effort: a failure here (missing appcmd, permission denied) must not fail
            // deploy-creatio. Required IIS modules are ensured upfront by IISDeploymentStrategy.
            try {
                string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
                string section = "system.webServer/security/authentication/windowsAuthentication";

                string unlockCmd = $"unlock config -section:{section}";
                var unlockResult = _processExecutor.Execute(appcmdPath, unlockCmd, true);

                string enableCmd = $"set config \"{siteName}\" -section:{section} /enabled:true /commit:apphost";
                var enableResult = _processExecutor.Execute(appcmdPath, enableCmd, true);

                return "UnlockResult: " + unlockResult + Environment.NewLine + "EnableResult: " + enableResult;
            }
            catch (Exception ex) {
                _logger.WriteWarning(
                    $"Could not enable Windows Authentication for site '{siteName}': {ex.Message}. "
                    + "Deployment will continue; enable it manually if required.");
                return "Skipped: Windows Authentication step failed (" + ex.Message + "). Deployment continues.";
            }
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
