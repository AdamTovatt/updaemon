using System.Text.Json;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Configuration
{
    /// <summary>The last <c>update</c> run, kept in the scope root for <c>status</c>.</summary>
    public class UpdateRunLog
    {
        private readonly UpdaemonScope _scope;

        public UpdateRunLog(UpdaemonScope scope)
        {
            _scope = scope;
        }

        public Task SaveAsync(UpdateRun run, CancellationToken cancellationToken = default)
        {
            string json = JsonSerializer.Serialize(run, UpdaemonJsonContext.Default.UpdateRun);
            return AtomicFile.WriteAsync(_scope.LastUpdateFilePath, json, AtomicFile.Readable, cancellationToken);
        }

        /// <summary>The last run; null when none was recorded.</summary>
        /// <exception cref="InvalidOperationException">The record exists but cannot be read.</exception>
        public async Task<UpdateRun?> LoadAsync(CancellationToken cancellationToken = default)
        {
            string path = _scope.LastUpdateFilePath;
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, cancellationToken), UpdaemonJsonContext.Default.UpdateRun)
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new InvalidOperationException($"{path} cannot be read: {ex.Message}", ex);
            }
        }
    }
}
