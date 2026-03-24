using System;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>build-docker-image</c> command.
/// </summary>
[Verb("build-docker-image", HelpText = "Build a Docker image for a Creatio .NET 8+ distribution")]
public class BuildDockerImageOptions {
	/// <summary>
	/// Gets or sets the path to the source ZIP archive or extracted application directory.
	/// </summary>
	[Option("from", Required = true, HelpText = "Path to a Creatio ZIP archive or extracted application directory")]
	public string SourcePath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the bundled template name or custom template directory path.
	/// </summary>
	[Option("template", Required = true, HelpText = "Bundled template name (dev, prod) or template directory path")]
	public string Template { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the optional output tar path used by <c>docker save</c>.
	/// </summary>
	[Option("output-path", Required = false, HelpText = "Optional tar file path where the built image should be saved")]
	public string OutputPath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the optional registry or repository prefix used by <c>docker push</c>.
	/// </summary>
	[Option("registry", Required = false, HelpText = "Optional registry or repository prefix used when pushing the image")]
	public string Registry { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether to force use of the Docker CLI for this invocation.
	/// </summary>
	[Option("use-docker", Required = false, HelpText = "Use the docker CLI for this invocation, overriding appsettings.json")]
	public bool UseDocker { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to force use of the nerdctl CLI for this invocation.
	/// </summary>
	[Option("use-nerdctl", Required = false, HelpText = "Use the nerdctl CLI for this invocation, overriding appsettings.json")]
	public bool UseNerdctl { get; set; }
}

/// <summary>
/// Executes the <c>build-docker-image</c> command.
/// </summary>
public sealed class BuildDockerImageCommand(IBuildDockerImageService buildDockerImageService)
	: Command<BuildDockerImageOptions> {
	private readonly IBuildDockerImageService _buildDockerImageService =
		buildDockerImageService ?? throw new ArgumentNullException(nameof(buildDockerImageService));

	/// <inheritdoc />
	public override int Execute(BuildDockerImageOptions options) {
		return _buildDockerImageService.Execute(options);
	}
}
