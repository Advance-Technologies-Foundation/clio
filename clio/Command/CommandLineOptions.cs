using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;

namespace Clio
{
		public class EnvironmentOptions
		{
			[Option('u', "uri", Required = false, HelpText = "Application uri")]
			public string Uri { get; set; }

			[Option("url", Required = false, Hidden = true, HelpText = "Alias for --uri")]
			public string UrlAlias {
				get => Uri;
				set { if (!string.IsNullOrEmpty(value)) Uri = value; }
			}

			[Option('p', "password", Required = false, HelpText = "User password")]
			public string Password { get; set; }

			[Option("Password", Required = false, Hidden = true, HelpText = "Alias for --password")]
			public string PasswordAlias {
				get => Password;
				set { if (!string.IsNullOrEmpty(value)) Password = value; }
			}

			[Option('l', "login", Required = false, HelpText = "User login (administrator permission required)")]
			public string Login { get; set; }

			[Option("Login", Required = false, Hidden = true, HelpText = "Alias for --login")]
			public string LoginAlias {
				get => Login;
				set { if (!string.IsNullOrEmpty(value)) Login = value; }
			}

			[Option('i', "IsNetCore", Required = false, HelpText = "Use .NET Core application", Hidden = true)]
		public bool? IsNetCore { get; set; }

			[Option('e', "environment", Required = false, HelpText = "Environment name")]
			public string Environment { get; set; }

			[Option("Environment", Required = false, Hidden = true, HelpText = "Alias for --environment")]
			public string EnvironmentAlias {
				get => Environment;
				set { if (!string.IsNullOrEmpty(value)) Environment = value; }
			}

			[Option('m', "maintainer", Required = false, HelpText = "Maintainer name")]
			public string Maintainer { get; set; }

			[Option("Maintainer", Required = false, Hidden = true, HelpText = "Alias for --maintainer")]
			public string MaintainerAlias {
				get => Maintainer;
				set { if (!string.IsNullOrEmpty(value)) Maintainer = value; }
			}

			[Option('c', "dev", Required = false, HelpText = "Developer mode state for environment")]
			public string DevMode { get; set; }

			[Option("workspace-pathes", Required = false, HelpText = "Workspace path")]
			public string WorkspacePathes {
				get; set;
			}

			[Option("WorkspacePathes", Required = false, Hidden = true, HelpText = "Alias for --workspace-pathes")]
			public string WorkspacePathesAlias {
				get => WorkspacePathes;
				set { if (!string.IsNullOrEmpty(value)) WorkspacePathes = value; }
			}

			public bool? DeveloperModeEnabled {
				get {
					if (!string.IsNullOrEmpty(DevMode))
					{
						if (bool.TryParse(DevMode, out bool result))
						{
							return result;
						}
					}
					return null;
				}
				set {
					DevMode = value.ToString();
				}
			}

			[Option('s', "safe", Required = false, HelpText = "Safe action in this environment")]
			public string Safe { get; set; }

			[Option("Safe", Required = false, Hidden = true, HelpText = "Alias for --safe")]
			public string SafeAlias {
				get => Safe;
				set { if (!string.IsNullOrEmpty(value)) Safe = value; }
			}

			[Option("client-id", Required = false, HelpText = "OAuth client id")]
			public string ClientId { get; set; }

			[Option("clientId", Required = false, Hidden = true, HelpText = "Alias for --client-id")]
			public string ClientIdAlias {
				get => ClientId;
				set { if (!string.IsNullOrEmpty(value)) ClientId = value; }
			}

			[Option("client-secret", Required = false, HelpText = "OAuth client secret")]
			public string ClientSecret { get; set; }

			[Option("clientSecret", Required = false, Hidden = true, HelpText = "Alias for --client-secret")]
			public string ClientSecretAlias {
				get => ClientSecret;
				set { if (!string.IsNullOrEmpty(value)) ClientSecret = value; }
			}

			[Option("auth-app-uri", Required = false, HelpText = "OAuth app URI")]
			public string AuthAppUri { get; set; }

			[Option("authAppUri", Required = false, Hidden = true, HelpText = "Alias for --auth-app-uri")]
			public string AuthAppUriAlias {
				get => AuthAppUri;
				set { if (!string.IsNullOrEmpty(value)) AuthAppUri = value; }
			}


			[Option("silent", Required = false, HelpText = "Use default behavior without user interaction")]
			public bool IsSilent { get; set; }


			public bool? SafeValue {
				get {
					if (!string.IsNullOrEmpty(Safe))
					{
						if (bool.TryParse(Safe, out bool result))
						{
							return result;
						}
					}
					return null;
				}
			}

			[Option("restart-environment", Required = false, HelpText = "Restart environment after execute command")]
			public bool RestartEnvironment { get; set; }

			[Option("restartEnvironment", Required = false, Hidden = true, HelpText = "Alias for --restart-environment")]
			public bool RestartEnvironmentAlias {
				get => RestartEnvironment;
				set { if (value) RestartEnvironment = value; }
			}

			public static bool IsNullOrEmpty(EnvironmentOptions options) {
				if (options == null) {
					return true;
				}
				if (string.IsNullOrEmpty(options.Uri) &&
						string.IsNullOrEmpty(options.Login) &&
						string.IsNullOrEmpty(options.Password) &&
						string.IsNullOrEmpty(options.ClientId) &&
						string.IsNullOrEmpty(options.ClientSecret) &&
						string.IsNullOrEmpty(options.AuthAppUri) &&
						string.IsNullOrEmpty(options.Maintainer)) {
					return true;
				}
				return false;
			}

			public virtual bool ShowDefaultEnvironment() {
				return true;
			}
			
			[Option("db-server-uri", Required = false, HelpText = "Db server uri")]
			public string DbServerUri { get; set; }

			[Option("db-user", Required = false, HelpText = "Database user")]
			public string DbUser { get; set; }

			[Option("db-password", Required = false, HelpText = "Database password")]
			public string DbPassword { get; set; }
			
			[Option("backup-file", Required = false, HelpText = "Full path to backup file")]
			public string BackUpFilePath { get; set; }
		
			[Option("db-working-folder", Required = false, HelpText = "Folder visible to db server")]
			public string DbWorknigFolder { get; set; }
		
			[Option("db-name", Required = false, HelpText = "Desired database name")]
			public string DbName { get; set; }
			
			[Option("force", Required = false, HelpText = "Force restore")]
			public bool Force { get; set; }

			[Option("callback-process", Required = false, HelpText = "Callback process name")]
			public string CallbackProcess {
				get; set;
			}

			internal virtual bool RequiredEnvironment => true;
			
			[Option("ep", Required = false, HelpText = "Path to the application root folder")]
			public string EnvironmentPath { get; set; }

			public void CopyFromEnvironmentSettings(EnvironmentOptions source)
            {
                if (source == null) {
                    throw new ArgumentNullException(nameof(source), "Source environment options cannot be null.");
                }
            
                // Copy all the properties
                this.Uri = source.Uri;
                this.Password = source.Password;
                this.Login = source.Login;
                this.IsNetCore = source.IsNetCore;
                this.Environment = source.Environment;
                this.Maintainer = source.Maintainer;
                this.DevMode = source.DevMode;
                this.WorkspacePathes = source.WorkspacePathes;
                // Note: No need to copy DeveloperModeEnabled as it is derived from DevMode
                this.Safe = source.Safe;
                this.ClientId = source.ClientId;
                this.ClientSecret = source.ClientSecret;
                this.AuthAppUri = source.AuthAppUri;
                this.IsSilent = source.IsSilent;
                // Note: No need to copy SafeValue as it is derived from Safe
                this.RestartEnvironment = source.RestartEnvironment;
                this.DbServerUri = source.DbServerUri;
                this.DbUser = source.DbUser;
                this.DbPassword = source.DbPassword;
                this.BackUpFilePath = source.BackUpFilePath;
                this.DbWorknigFolder = source.DbWorknigFolder;
                this.DbName = source.DbName;
                this.Force = source.Force;
				this.CallbackProcess = source.CallbackProcess;
				this.EnvironmentPath = source.EnvironmentPath;
            }

        internal bool IsEmpty() {
			return string.IsNullOrEmpty(Uri);
        }
    }

	public class EnvironmentNameOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "EnvironmentName", Required = false, HelpText = "Application name")]
		public string EnvironmentName {
			get => Environment;
			set => Environment = value;
		}
	}


	[Verb("convert", HelpText = "Convert package to project", Hidden = true)]
	internal class ConvertOptions{
		[Option('p', "Path", Required = false,
			HelpText = "Path to package directory", Default = null)]
		public string Path { get; set; }

		[Value(0, MetaName = "<package names>", Required = false,
			HelpText = "Name of the convert instance (or comma separated names)")]
		public string Name { get; set; }

		[Option('c', "ConvertSourceCode", Required = false, HelpText = "Convert source code schema to files", Default = false)]
		public bool ConvertSourceCode { get; set; }


		[Usage(ApplicationAlias = "clio")]
		public static IEnumerable<Example> Examples =>
			new List<Example> {
				new Example("Convert existing packages",
					new ConvertOptions { Path = "C:\\Pkg\\" , Name = "MyApp,MyIntegration"}
				),
				new Example("Convert all packages in folder",
					new ConvertOptions { Path = "C:\\Pkg\\"}
				)
			};
	}

	[Verb("set-dev-mode", Aliases = ["dev", "unlock"], HelpText = "Activate developer mode for selected environment")]
	internal class DeveloperModeOptions : EnvironmentNameOptions
	{

	}



}
