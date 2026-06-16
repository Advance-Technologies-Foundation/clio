using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
    /// <summary>
    /// Options for remote command execution.
    /// </summary>
    public class RemoteCommandOptions : EnvironmentOptions{

        private int? _timeOut;
        protected virtual int DefaultTimeout { get; set; } = 100_000;

        [Option("timeout", Required = false, HelpText = "Request timeout in milliseconds")]
        public int TimeOut
        {
            get => _timeOut ?? GetTimeOut();
            internal set => _timeOut = value;
        }

        public int RetryCount { get; internal set; } = 3;
        public int RetryDelay { get; internal set; } = 1;


        private int GetTimeOut() {
            string commandName = this.GetType().GetCustomAttribute<VerbAttribute>()?.Name ?? "undefined-command";
            int timeout =  commandName switch {
                               "generate-source-code" 
                                   or "compile-configuration" => 
                                   (int)TimeSpan.FromMinutes(60).TotalMilliseconds,
                               "compile-package" => (int)TimeSpan.FromMinutes(10).TotalMilliseconds,
                               "call-service" => (int)TimeSpan.FromMinutes(1).TotalMilliseconds,
                               var _ => DefaultTimeout
                           };
            this.TimeOut = timeout; // cache timeout value for future use
            return timeout;
        }
        
    }

    /// <summary>
    /// Base class for remote commands. Handles cliogate checks and remote execution logic.
    /// </summary>
    /// <typeparam name="TEnvironmentOptions">Type of environment options.</typeparam>
    public abstract class RemoteCommand<TEnvironmentOptions> : Command<TEnvironmentOptions>
        where TEnvironmentOptions : RemoteCommandOptions
    {
        #region Constructors: Protected

        protected virtual bool CommandSuccess { get; set; } = true;
        
        protected RemoteCommand(IApplicationClient applicationClient,
            EnvironmentSettings environmentSettings)
            : this(environmentSettings)
        {
            ApplicationClient = applicationClient;
        }

        protected RemoteCommand(EnvironmentSettings environmentSettings)
        {
            EnvironmentSettings = environmentSettings;
        }

        #endregion

        #region Constructors: Public

        public RemoteCommand() { } // for tests

        #endregion

        #region Properties: Protected

        internal IApplicationClient ApplicationClient { get; set; }
        internal EnvironmentSettings EnvironmentSettings { get; set; }

        protected IClioGateway ClioGateWay { get; set; }

        protected string RootPath =>
            EnvironmentSettings.IsNetCore
                ? EnvironmentSettings.Uri : EnvironmentSettings.Uri + @"/0";

        /// <summary>
        /// Service path for remote command.
        /// </summary>
        protected virtual string ServicePath { get; set; }
        protected string ServiceUri => Uri.TryCreate(ServicePath, UriKind.Absolute, out var maybeUri) && (maybeUri.Scheme == Uri.UriSchemeHttp || maybeUri.Scheme == Uri.UriSchemeHttps)
            ? ServicePath 
            : RootPath + ServicePath;
        
        #endregion

        #region Properties: Public

        public virtual HttpMethod HttpMethod => HttpMethod.Post;
        public int RequestTimeout { get; set; }
        public int RetryCount { get; set; }
        public int DelaySec { get; set; }
        public ILogger Logger { get; set; } = ConsoleLogger.Instance;

        #endregion

        #region Methods: Protected

        /// <summary>
        /// Executes the remote command.
        /// </summary>
        protected virtual void ExecuteRemoteCommand(TEnvironmentOptions options)
        {
            string response = HttpMethod == HttpMethod.Post
                ? ApplicationClient.ExecutePostRequest(ServiceUri, GetRequestData(options), RequestTimeout, RetryCount, DelaySec)
                : ApplicationClient.ExecuteGetRequest(ServiceUri, RequestTimeout, RetryCount, DelaySec);
            ProceedResponse(response, options);
        }

        /// <summary>
        /// Gets request data for remote command.
        /// </summary>
        protected virtual string GetRequestData(TEnvironmentOptions options)
        {
            return "{}";
        }

        /// <summary>
        /// Performs login to remote application.
        /// </summary>
        protected int Login()
        {
            try
            {
                Logger.WriteInfo($"Try login to {EnvironmentSettings.Uri} with {EnvironmentSettings.Login} credentials...");
                ApplicationClient.Login();
                Logger.WriteInfo("Login done");
                return 0;
            }
            catch (WebException we)
            {
                HttpWebResponse errorResponse = we.Response as HttpWebResponse;
                if (errorResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    Logger.WriteError($"Application {EnvironmentSettings.Uri} not found");
                }
                return 1;
            }
        }

        /// <summary>
        /// Handles response from remote command.
        /// </summary>
        protected virtual void ProceedResponse(string response, TEnvironmentOptions options) { }

        #endregion

        #region Methods: Public

        /// <summary>
        /// Executes the command and handles errors. Package requirements declared via
        /// <see cref="RequiresPackageAttribute"/> on the options type are enforced ahead of dispatch
        /// at the command chokepoints, so this method no longer performs an inline cliogate check.
        /// </summary>
        /// <param name="options">Command options.</param>
        /// <returns>0 if success, 1 if error.</returns>
        public override int Execute(TEnvironmentOptions options)
        {
            try
            {
                RequestTimeout = options.TimeOut;
                RetryCount = options.RetryCount;
                DelaySec = options.RetryDelay;
                ExecuteRemoteCommand(options);
                string commandName = typeof(TEnvironmentOptions).GetCustomAttribute<VerbAttribute>()?.Name;

                if (CommandSuccess) {
                    Logger.WriteInfo($"Done {commandName}");
                    return 0;
                }

                return 1;
            }
            catch (SilentException)
            {
                return 1;
            }
            catch (Exception e)
            {
                Logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
                return 1;
            }
        }

        #endregion
    }
}
