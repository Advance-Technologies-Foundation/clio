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
            var handler = new ExecAsyncCallback((stdIn, stdOut, stdError) =>
                HandleExecStreamsAsync(stdIn, stdError, sourceFilePath, destinationFilePath, cancellationToken));

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

        /// <summary>
        /// Tars <paramref name="sourceFilePath"/> into <paramref name="stdIn"/> and surfaces any error text
        /// written to <paramref name="stdError"/>. Extracted from the <see cref="ExecAsyncCallback"/> passed to
        /// <see cref="IKubernetes.NamespacedPodExecAsync"/> so the stream-handling logic — including cancellation
        /// classification — is directly unit-testable without a real Kubernetes exec session.
        /// </summary>
        internal static async Task HandleExecStreamsAsync(Stream stdIn, Stream stdError, string sourceFilePath,
            string destinationFilePath, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(destinationFilePath);
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var inputFileStream = File.OpenRead(sourceFilePath))
                    {
                        // Not a `using` declaration: TarOutputStream.Dispose() validates that the declared
                        // entry size was fully written and throws its own exception when it wasn't (e.g. a
                        // write canceled mid-entry). Left to an implicit `using`, that secondary exception
                        // would replace the real one during stack unwind, masking a cancellation as a
                        // generic tar-write failure — see the catch below.
                        var tarOutputStream = new TarOutputStream(memoryStream, Encoding.Default) { IsStreamOwner = false };
                        try
                        {
                            var fileSize = inputFileStream.Length;
                            var entry = TarEntry.CreateTarEntry(fileInfo.Name);

                            entry.Size = fileSize;

                            tarOutputStream.PutNextEntry(entry);
                            await inputFileStream.CopyToAsync(tarOutputStream, cancellationToken);
                            tarOutputStream.CloseEntry();
                            tarOutputStream.Dispose();
                        }
                        catch
                        {
                            // Best-effort cleanup only: an entry left half-written after the exception above
                            // (most commonly cancellation) makes Dispose() throw its own "entry closed before
                            // N bytes written" exception. Swallow that secondary failure so the exception
                            // being unwound here — rethrown as-is — is what callers actually observe.
                            try { tarOutputStream.Dispose(); } catch { /* ignored: see comment above */ }
                            throw;
                        }
                    }

                    memoryStream.Position = 0;

                    await memoryStream.CopyToAsync(stdIn, cancellationToken);
                    await stdIn.FlushAsync(cancellationToken);
                }

            }
            catch (OperationCanceledException)
            {
                // Cancellation is a caller-initiated abort, not a copy failure — it must propagate
                // distinctly instead of being wrapped as IOException (review #1143).
                throw;
            }
            catch (Exception ex)
            {
                throw new IOException($"Copy command failed: {ex.Message}");
            }

            using StreamReader streamReader = new StreamReader(stdError);
            string error = await streamReader.ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrEmpty(error))
            {
                throw new IOException($"Copy command failed: {error}");
            }
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