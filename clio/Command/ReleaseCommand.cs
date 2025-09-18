using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Clio.Common;
using CommandLine;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Command
{
	[Verb("release", HelpText = "Create a new release by incrementing the version from the latest tag")]
	public class ReleaseCommandOptions : BaseCommandOptions
	{
		[Option('f', "force", Required = false, HelpText = "Skip confirmation prompt and create release immediately")]
		public bool Force { get; set; }
	}

	public class ReleaseCommand : Command<ReleaseCommandOptions>
	{
		private readonly ILogger _logger;
		private readonly IFileSystem fileSystem;

		public ReleaseCommand(ILogger logger, IFileSystem fileSystem)
		{
			_logger = logger;
			this.fileSystem = fileSystem;
		}

		public override int Execute(ReleaseCommandOptions options)
		{
			try
			{
				_logger.WriteInfo("🚀 Starting release process...");

				// Step 1: Check and setup GitHub CLI
				if (!CheckGitHubCLI())
				{
					_logger.WriteWarning("⚠️  GitHub CLI not found. Installing...");
					if (!InstallGitHubCLI())
					{
						_logger.WriteWarning("⚠️  Could not install GitHub CLI. Will continue with tag creation only.");
					}
				}

				// Verify authentication if CLI is available
				if (CheckGitHubCLI() && !CheckGitHubAuth())
				{
					_logger.WriteError("❌ GitHub CLI is not authenticated. Please run 'gh auth login' first.");
					return 1;
				}

				// Step 2: Get the latest tag
				string currentVersion = GetLatestTag();
				if (string.IsNullOrEmpty(currentVersion))
				{
					_logger.WriteInfo("No existing tags found. Starting with version 8.0.1.1");
					currentVersion = "8.0.1.0"; // Will be incremented to 8.0.1.1
				}

				// Step 3: Parse and validate version format
				if (!TryParseVersion(currentVersion, out var versionParts))
				{
					_logger.WriteError($"❌ Invalid version format: {currentVersion}. Expected format: X.Y.Z.W");
					return 1;
				}

				// Step 4: Increment build number
				versionParts[3]++;
				string newVersion = string.Join(".", versionParts);
				
				_logger.WriteInfo($"📊 Current version: {currentVersion}");
				_logger.WriteInfo($"📊 New version: {newVersion}");

				// Step 5: Confirm with user unless --force is used
				if (!options.Force)
				{
					_logger.WriteInfo($"Do you want to create release {newVersion}? (y/n)");
					var response = Console.ReadLine()?.Trim().ToLower();
					if (response != "y" && response != "yes")
					{
						_logger.WriteInfo("Release cancelled by user.");
						return 0;
					}
				}

				// Step 6: Update project version
				if (!UpdateProjectVersion(newVersion))
				{
					_logger.WriteError("❌ Failed to update project version.");
					return 1;
				}

				// Step 7: Create and push tag
				if (!CreateAndPushTag(newVersion))
				{
					_logger.WriteError("❌ Failed to create and push tag.");
					return 1;
				}

				// Step 8: Create GitHub release
				if (CheckGitHubCLI() && CheckGitHubAuth())
				{
					if (!CreateGitHubRelease(newVersion))
					{
						_logger.WriteWarning("⚠️  Failed to create GitHub release, but tag was created successfully.");
					}
				}
				else
				{
					_logger.WriteInfo($"🔗 Create release manually: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag={newVersion}");
				}

				_logger.WriteInfo("✅ Release process completed successfully!");
				_logger.WriteInfo("📦 CI/CD will now build, test, and publish the NuGet package automatically.");

				return 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"❌ Release process failed: {ex.Message}");
				return 1;
			}
		}

		private bool CheckGitHubCLI()
		{
			try
			{
				var processInfo = new ProcessStartInfo
				{
					FileName = "gh",
					Arguments = "--version",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};

				using var process = Process.Start(processInfo);
				process?.WaitForExit();
				return process?.ExitCode == 0;
			}
			catch
			{
				return false;
			}
		}

		private bool InstallGitHubCLI()
		{
			try
			{
				string installCommand;
				string arguments;

				if (Environment.OSVersion.Platform == PlatformID.Win32NT)
				{
					// Try winget first, then choco
					installCommand = "winget";
					arguments = "install --id GitHub.cli";
				}
				else if (File.Exists("/usr/bin/brew") || File.Exists("/opt/homebrew/bin/brew"))
				{
					// macOS with Homebrew
					installCommand = "brew";
					arguments = "install gh";
				}
				else
				{
					// Linux - provide instructions
					_logger.WriteInfo("Please install GitHub CLI manually:");
					_logger.WriteInfo("Ubuntu/Debian: https://github.com/cli/cli/blob/trunk/docs/install_linux.md");
					_logger.WriteInfo("Other Linux: https://cli.github.com/");
					return false;
				}

				var processInfo = new ProcessStartInfo
				{
					FileName = installCommand,
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var process = Process.Start(processInfo);
				process?.WaitForExit();
				return process?.ExitCode == 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to install GitHub CLI: {ex.Message}");
				return false;
			}
		}

		private bool CheckGitHubAuth()
		{
			try
			{
				var processInfo = new ProcessStartInfo
				{
					FileName = "gh",
					Arguments = "auth status",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};

				using var process = Process.Start(processInfo);
				process?.WaitForExit();
				return process?.ExitCode == 0;
			}
			catch
			{
				return false;
			}
		}

		private string GetLatestTag()
		{
			try
			{
				var processInfo = new ProcessStartInfo
				{
					FileName = "git",
					Arguments = "describe --tags --abbrev=0",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};

				using var process = Process.Start(processInfo);
				process?.WaitForExit();
				
				if (process?.ExitCode == 0)
				{
					return process.StandardOutput.ReadToEnd().Trim();
				}
				
				return string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}

		private bool TryParseVersion(string version, out int[] versionParts)
		{
			versionParts = null;

			// Remove 'v' prefix if present
			version = version.TrimStart('v');

			// Validate format X.Y.Z.W
			var regex = new Regex(@"^\d+\.\d+\.\d+\.\d+$");
			if (!regex.IsMatch(version))
			{
				return false;
			}

			var parts = version.Split('.');
			versionParts = new int[4];
			
			for (int i = 0; i < 4; i++)
			{
				if (!int.TryParse(parts[i], out versionParts[i]))
				{
					return false;
				}
			}

			return true;
		}

		private bool UpdateProjectVersion(string newVersion)
		{
			try
			{
				string projectPath = Path.Combine("clio", "clio.csproj");
				if (!fileSystem.ExistsFile(projectPath))
				{
					_logger.WriteError($"Project file not found: {projectPath}");
					return false;
				}

				string content = fileSystem.ReadAllText(projectPath);
				
				// Update AssemblyVersion
				var versionRegex = new Regex(@"<AssemblyVersion[^>]*>[^<]*</AssemblyVersion>");
				string newAssemblyVersionTag = $"<AssemblyVersion Condition=\"'$(AssemblyVersion)' == ''\">{newVersion}</AssemblyVersion>";
				
				if (versionRegex.IsMatch(content))
				{
					content = versionRegex.Replace(content, newAssemblyVersionTag);
				}
				else
				{
					_logger.WriteError("Could not find AssemblyVersion tag in project file.");
					return false;
				}

				fileSystem.WriteAllTextToFile(projectPath, content);
				_logger.WriteInfo($"✅ Updated {projectPath} to version {newVersion}");

				// Commit the change
				return CommitVersionChange(newVersion);
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to update project version: {ex.Message}");
				return false;
			}
		}

		private bool CommitVersionChange(string version)
		{
			try
			{
				// Add the file
				var addProcessInfo = new ProcessStartInfo
				{
					FileName = "git",
					Arguments = "add clio/clio.csproj",
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var addProcess = Process.Start(addProcessInfo);
				addProcess?.WaitForExit();

				if (addProcess?.ExitCode != 0)
				{
					_logger.WriteError("Failed to stage project file changes.");
					return false;
				}

				// Commit the change
				var commitProcessInfo = new ProcessStartInfo
				{
					FileName = "git",
					Arguments = $"commit -m \"Bump version to {version} for release\"",
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var commitProcess = Process.Start(commitProcessInfo);
				commitProcess?.WaitForExit();

				return commitProcess?.ExitCode == 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to commit version change: {ex.Message}");
				return false;
			}
		}

		private bool CreateAndPushTag(string version)
		{
			try
			{
				// Create tag
				var tagProcessInfo = new ProcessStartInfo
				{
					FileName = "git",
					Arguments = $"tag {version}",
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var tagProcess = Process.Start(tagProcessInfo);
				tagProcess?.WaitForExit();

				if (tagProcess?.ExitCode != 0)
				{
					_logger.WriteError("Failed to create tag.");
					return false;
				}

				_logger.WriteInfo($"✅ Created tag {version}");

				// Push tag
				var pushProcessInfo = new ProcessStartInfo
				{
					FileName = "git",
					Arguments = $"push origin {version}",
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var pushProcess = Process.Start(pushProcessInfo);
				pushProcess?.WaitForExit();

				if (pushProcess?.ExitCode != 0)
				{
					_logger.WriteError("Failed to push tag.");
					return false;
				}

				_logger.WriteInfo($"✅ Pushed tag {version} to origin");
				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to create and push tag: {ex.Message}");
				return false;
			}
		}

		private bool CreateGitHubRelease(string version)
		{
			try
			{
				var processInfo = new ProcessStartInfo
				{
					FileName = "gh",
					Arguments = $"release create {version} --title \"Release {version}\" --notes \"Automated release {version}\"",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};

				using var process = Process.Start(processInfo);
				process?.WaitForExit();

				if (process?.ExitCode == 0)
				{
					_logger.WriteInfo($"✅ Created GitHub release: https://github.com/Advance-Technologies-Foundation/clio/releases/tag/{version}");
					return true;
				}
				else
				{
					string error = process?.StandardError.ReadToEnd() ?? "Unknown error";
					_logger.WriteError($"Failed to create GitHub release: {error}");
					return false;
				}
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to create GitHub release: {ex.Message}");
				return false;
			}
		}
	}
}