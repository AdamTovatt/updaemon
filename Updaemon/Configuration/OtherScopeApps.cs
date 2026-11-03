using Updaemon.Interfaces;

namespace Updaemon.Configuration
{
    /// <summary>
    /// The apps of the scope this run is not in, read only so <c>install</c> can warn about an app
    /// name both scopes would hold.
    /// </summary>
    public sealed class OtherScopeApps
    {
        private readonly IConfigManager? _config;
        private readonly string _owner;

        /// <param name="config">The other scope's config.</param>
        /// <param name="owner">Whose apps it holds, as <see cref="UpdaemonScope.Owner"/> names them.</param>
        public OtherScopeApps(IConfigManager config, string owner)
        {
            _config = config;
            _owner = owner;
        }

        private OtherScopeApps()
        {
            _owner = string.Empty;
        }

        /// <summary>No other scope to look in: root without sudo has none.</summary>
        public static OtherScopeApps None { get; } = new OtherScopeApps();

        /// <summary>The other scope's owner when it has an app named <paramref name="appName"/>; otherwise null.</summary>
        public async Task<string?> FindOwnerOfAsync(string appName, CancellationToken cancellationToken = default)
        {
            if (_config == null)
            {
                return null;
            }

            try
            {
                return await _config.GetAppAsync(appName, cancellationToken) == null ? null : _owner;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A config this user cannot read, or one this updaemon cannot read, holds nothing it can name.
                return null;
            }
        }
    }
}
