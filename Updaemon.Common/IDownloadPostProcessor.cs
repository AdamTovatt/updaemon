namespace Updaemon.Common
{
    /// <summary>
    /// Post-processes downloaded files, such as extracting archives and unwrapping directory structures.
    /// </summary>
    public interface IDownloadPostProcessor
    {
        /// <summary>
        /// Processes the target directory after a download completes.
        /// Automatically extracts archives and unwraps single-directory structures.
        /// </summary>
        /// <param name="targetDirectory">The directory where files were downloaded.</param>
        Task ProcessAsync(string targetDirectory);
    }
}

