using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Tar;
using k8s;
using k8s.Models;

namespace Clio.Common.K8;

internal class Cp {

	#region Fields: Private

	private readonly IKubernetes _client;

	#endregion

	#region Constructors: Public

	public Cp(IKubernetes client) {
		_client = client;
	}

	#endregion

	#region Methods: Private

	private static string GetFolderName(string filePath) {
		string folderName = Path.GetDirectoryName(filePath)?.Replace('\\', '/') ?? ".";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return folderName?.Replace('\\', '/');
		}
		return folderName;
	}

	private void ValidatePathParameters(string sourcePath, string destinationPath) {
		if (string.IsNullOrWhiteSpace(sourcePath)) {
			throw new ArgumentException($"{nameof(sourcePath)} cannot be null or whitespace");
		}

		if (string.IsNullOrWhiteSpace(destinationPath)) {
			throw new ArgumentException($"{nameof(destinationPath)} cannot be null or whitespace");
		}
	}

	#endregion

	#region Methods: Public

	public async Task CopyAsync(V1Pod destPod, string k8Namespace, string containerName, string sourceFilePath,
		string destinationFilePath) {
		await CopyFileToPodAsync(destPod.Metadata.Name, k8Namespace, containerName, sourceFilePath, destinationFilePath);
	}

	public async Task<int> CopyFileToPodAsync(string name, string @namespace, string container, string sourceFilePath,
		string destinationFilePath, CancellationToken cancellationToken = default) {
		// All other parameters are being validated by MuxedStreamNamespacedPodExecAsync called by NamespacedPodExecAsync
		ValidatePathParameters(sourceFilePath, destinationFilePath);

		// The callback which processes the standard input, standard output and standard error of exec method
		ExecAsyncCallback handler = async (stdIn, _, stdError) => {
			FileInfo fileInfo = new(destinationFilePath);
			try {
				await using (FileStream inputFileStream = File.OpenRead(sourceFilePath)) {
					await using (TarOutputStream tarOutputStream = new(stdIn, Encoding.Default)) {
						tarOutputStream.IsStreamOwner = false;

						long fileSize = inputFileStream.Length;
						TarEntry entry = TarEntry.CreateTarEntry(fileInfo.Name);

						entry.Size = fileSize;

						await tarOutputStream.PutNextEntryAsync(entry,CancellationToken.None);
						await inputFileStream.CopyToAsync(tarOutputStream, 81920, cancellationToken);
						await tarOutputStream.CloseEntryAsync(CancellationToken.None);
						await tarOutputStream.FlushAsync(CancellationToken.None);
					}
				}

				await stdIn.FlushAsync(CancellationToken.None);
			}
			catch (Exception ex) {
				throw new IOException($"Copy command failed: {ex.Message}");
			}

			using StreamReader streamReader = new(stdError);
			while (streamReader.EndOfStream == false) {
				string error = await streamReader.ReadToEndAsync(CancellationToken.None);
				throw new IOException($"Copy command failed: {error}");
			}
		};

		string destinationFolder = GetFolderName(destinationFilePath);

		return await _client.NamespacedPodExecAsync(name,
			@namespace,
			container,
			["sh", "-c", $"tar xmf - -C {destinationFolder}"],
			false,
			handler,
			cancellationToken);
	}

	#endregion

}
