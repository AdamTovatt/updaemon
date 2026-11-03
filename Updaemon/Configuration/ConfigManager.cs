using System.Text.Json;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Manages the updaemon configuration stored in the scope root's config.json.
    /// </summary>
    public class ConfigManager : IConfigManager
    {
        private readonly string _configFilePath;

        public ConfigManager(UpdaemonScope scope)
        {
            _configFilePath = scope.ConfigFilePath;
        }

        public async Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_configFilePath))
            {
                return new UpdaemonConfig();
            }

            string json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            CheckFormat(json);
            UpdaemonConfig? config = JsonSerializer.Deserialize(json, UpdaemonJsonContext.Default.UpdaemonConfig);
            return config ?? new UpdaemonConfig();
        }

        /// <summary>
        /// Refuses a config of another format with one clear line, before the serializer can fail on
        /// fields that changed shape (0.x 'services', plugins without a source).
        /// </summary>
        private void CheckFormat(string json)
        {
            int? format = null;
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("format", out JsonElement element)
                    && element.TryGetInt32(out int value))
                {
                    format = value;
                }
            }

            if (format == null)
            {
                throw new InvalidOperationException($"{_configFilePath} is from updaemon 0.x. Move it aside, then reinstall your apps with 'updaemon install'.");
            }

            if (format != UpdaemonConfig.CurrentFormat)
            {
                throw new InvalidOperationException($"{_configFilePath} has format {format}, which this updaemon cannot read; update updaemon.");
            }
        }

        public async Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default)
        {
            string json = JsonSerializer.Serialize(config, UpdaemonJsonContext.Default.UpdaemonConfig);
            await AtomicFile.WriteAsync(_configFilePath, json, AtomicFile.Readable, cancellationToken);
        }

        public async Task<InstalledApp?> GetAppAsync(string name, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.Apps.FirstOrDefault(app => app.Name == name);
        }

        public async Task<IReadOnlyList<InstalledApp>> GetAllAppsAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.Apps.AsReadOnly();
        }

        public async Task SaveAppAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            int index = config.Apps.FindIndex(existing => existing.Name == app.Name);
            if (index >= 0)
            {
                config.Apps[index] = app;
            }
            else
            {
                config.Apps.Add(app);
            }

            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task RemoveAppAsync(string name, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.Apps.RemoveAll(app => app.Name == name);
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.InstalledPlugins[pluginInfo.Alias] = pluginInfo;
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.InstalledPlugins.TryGetValue(alias, out InstalledPluginInfo? plugin) ? plugin : null;
        }

        public async Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.InstalledPlugins;
        }

        public async Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.InstalledPlugins.Remove(alias);
            await SaveConfigAsync(config, cancellationToken);
        }
    }
}
