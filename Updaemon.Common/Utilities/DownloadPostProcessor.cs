using System.IO.Compression;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// Post-processes downloaded files by extracting archives and unwrapping directory structures.
    /// </summary>
    public class DownloadPostProcessor : IDownloadPostProcessor
    {
        public async Task ProcessAsync(string targetDirectory, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(targetDirectory))
            {
                return;
            }

            // Check if there's only a single file in the directory
            string[] files = Directory.GetFiles(targetDirectory);
            string[] directories = Directory.GetDirectories(targetDirectory);

            if (files.Length == 1 && directories.Length == 0)
            {
                string filePath = files[0];
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                // Check if it's a zip file
                if (extension == ".zip")
                {
                    await ExtractAndDeleteZipAsync(filePath, targetDirectory, cancellationToken);
                }
            }
        }

        private async Task ExtractAndDeleteZipAsync(string zipFilePath, string targetDirectory, CancellationToken cancellationToken)
        {
            try
            {
                // Extract the zip file
                await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, targetDirectory), cancellationToken);

                // Delete the zip file
                File.Delete(zipFilePath);

                // After extraction, check if we should unwrap a single directory
                await UnwrapSingleDirectoryAsync(targetDirectory, cancellationToken);
            }
            catch
            {
                // Gracefully handle any errors (corrupted zip, permission issues, etc.)
                // Don't fail the update process
            }
        }

        private async Task UnwrapSingleDirectoryAsync(string targetDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string[] files = Directory.GetFiles(targetDirectory);
                string[] directories = Directory.GetDirectories(targetDirectory);

                // Only unwrap if there's a single directory and no files at the top level
                if (files.Length == 0 && directories.Length == 1)
                {
                    string singleDirectory = directories[0];
                    string[] nestedFiles = Directory.GetFiles(singleDirectory, "*", SearchOption.AllDirectories);

                    // Only unwrap if the directory actually contains files
                    if (nestedFiles.Length > 0)
                    {
                        await Task.Run(() => UnwrapDirectory(singleDirectory, targetDirectory), cancellationToken);
                    }
                }
            }
            catch
            {
                // Gracefully handle any errors
            }
        }

        private void UnwrapDirectory(string sourceDirectory, string targetDirectory)
        {
            // Move all contents from the nested directory up one level
            foreach (string filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                string destinationPath = Path.Combine(targetDirectory, relativePath);

                string? destinationDirectoryPath = Path.GetDirectoryName(destinationPath);
                if (destinationDirectoryPath != null)
                {
                    Directory.CreateDirectory(destinationDirectoryPath);
                }

                File.Move(filePath, destinationPath);
            }

            // Delete the now-empty nested directory
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }
}

