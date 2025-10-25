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

internal class Cp
    {

        private readonly IKubernetes _client;

        public Cp(IKubernetes client) {
            _client = client;
        }
        
        
        public async Task Copy(V1Pod destPod, string k8Namespace, string containerName, string sourceFilePath, string destinationFilePath ) {
            await CopyFileToPodAsync(destPod.Metadata.Name, k8Namespace, containerName, sourceFilePath, destinationFilePath);
        }


        private void ValidatePathParameters(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException($"{nameof(sourcePath)} cannot be null or whitespace");
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException($"{nameof(destinationPath)} cannot be null or whitespace");
            }

        }

        public async Task<int> CopyFileToPodAsync(string name, string @namespace, string container, string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            // All other parameters are being validated by MuxedStreamNamespacedPodExecAsync called by NamespacedPodExecAsync
            ValidatePathParameters(sourceFilePath, destinationFilePath);

            // The callback which processes the standard input, standard output and standard error of exec method
            var handler = new ExecAsyncCallback(async (stdIn, stdOut, stdError) =>
            {
                var fileInfo = new FileInfo(destinationFilePath);
                try
                {
                    using (var inputFileStream = File.OpenRead(sourceFilePath))
                    using (var tarOutputStream = new TarOutputStream(stdIn, Encoding.Default))
                    {
                        tarOutputStream.IsStreamOwner = false;

                        var fileSize = inputFileStream.Length;
                        var entry = TarEntry.CreateTarEntry(fileInfo.Name);

                        entry.Size = fileSize;

                        tarOutputStream.PutNextEntry(entry);
                        await inputFileStream.CopyToAsync(tarOutputStream, 81920, cancellationToken);
                        tarOutputStream.CloseEntry();
                        tarOutputStream.Flush();
                    }

                    await stdIn.FlushAsync();
                }
                catch (Exception ex)
                {
                    throw new IOException($"Copy command failed: {ex.Message}");
                }

                using StreamReader streamReader = new StreamReader(stdError);
                while (streamReader.EndOfStream == false)
                {
                    string error = await streamReader.ReadToEndAsync();
                    throw new IOException($"Copy command failed: {error}");
                }
            });

            string destinationFolder = GetFolderName(destinationFilePath);

            return await _client.NamespacedPodExecAsync(
                name,
                @namespace,
                container,
                new string[] { "sh", "-c", $"tar xmf - -C {destinationFolder}" },
                false,
                handler,
                cancellationToken);
        }


        private static string GetFolderName(string filePath)
        {
            string folderName = Path.GetDirectoryName(filePath)?.Replace('\\', '/') ?? ".";
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return folderName?.Replace('\\', '/');
            }
            return folderName;
        }
    }