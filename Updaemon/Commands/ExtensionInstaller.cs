using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// The 'install' command for an extension: downloads its latest release, asks its host whether it would
    /// load it, links it into the host's folder of extensions and restarts the host.
    /// </summary>
    public class ExtensionInstaller
    {
        private readonly IConfigManager _configManager;
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppSwitcher _switcher;
        private readonly ExtensionCheck _check;
        private readonly ReleaseStager _stager;
        private readonly OtherScopeApps _otherScopeApps;
        private readonly UpdaemonScope _scope;
        private readonly IOutputWriter _outputWriter;

        public ExtensionInstaller(
            IConfigManager configManager,
            VersionStore versions,
            IServiceDeployer serviceDeployer,
            AppSwitcher switcher,
            ExtensionCheck check,
            ReleaseStager stager,
            OtherScopeApps otherScopeApps,
            UpdaemonScope scope,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _switcher = switcher;
            _check = check;
            _stager = stager;
            _otherScopeApps = otherScopeApps;
            _scope = scope;
            _outputWriter = outputWriter;
        }

        /// <param name="from">Where it comes from; null for where its host knows it to.</param>
        /// <returns>The command's exit code.</returns>
        public async Task<int> InstallAsync(ExtensionName name, PackageSource? from, CancellationToken cancellationToken = default)
        {
            InstalledApp? host = await _configManager.GetAppAsync(name.Host, cancellationToken);
            if (host == null)
            {
                return _outputWriter.Fail(await HostNotInstalledAsync(name, cancellationToken));
            }

            // First: a killed install of this very extension is taken out again by it.
            PreparedApp prepared = await _switcher.PrepareAsync(host, cancellationToken);
            if (host.FindExtension(name.Id) is InstalledExtension installed)
            {
                return _outputWriter.Fail($"'{host.NameOf(installed)}' is already installed.");
            }

            // Checked again before the switch; here, so nothing is downloaded for an install that cannot be.
            await _serviceDeployer.ReachServiceManagerAsync(host.Name, cancellationToken: cancellationToken);

            DeployableVersion hostVersion = await _serviceDeployer.OpenCurrentAsync(host.Name, cancellationToken);
            if (hostVersion.Manifest.Extensions is not ExtensionsSpec extensions)
            {
                return _outputWriter.Fail(
                    $"{host.Name} {hostVersion.Version} takes no extensions: its {AppManifest.FileName} has no 'extensions'. A newer release may; get it with 'updaemon update {host.Name}'.");
            }

            PackageSource? source = from ?? FindKnownSource(extensions, name.Id);
            if (source == null)
            {
                string knows = extensions.Known.Count == 0 ? "it knows none" : $"it knows {string.Join(", ", extensions.Known.Keys.Order(StringComparer.Ordinal))}";
                return _outputWriter.Fail($"{host.Name} {hostVersion.Version} does not know an extension '{name.Id}'; {knows}. For another, say where it comes from: updaemon install {name} --from <plugin>:<remote>");
            }

            // Versions of one that was installed before are not this one's to roll back to.
            _versions.DeleteAll(name);
            InstalledExtension extension = new InstalledExtension { Id = name.Id, Source = source };
            Version version;
            try
            {
                version = await _stager.StageLatestAsync(
                    source,
                    (latest, client) => _versions.DownloadAsync(name, extension, latest, client, _ => Task.FromResult(latest), cancellationToken),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is CredentialNeededException or PluginException or NoReleaseException)
            {
                return _outputWriter.Fail($"{name}: {ex.Message}");
            }

            try
            {
                return await CheckAndLinkAsync(prepared, hostVersion, extension, version, cancellationToken);
            }
            finally
            {
                // Unless it was installed, nothing of it stays.
                await _versions.DiscardAsync(name, version);
            }
        }

        private static PackageSource? FindKnownSource(ExtensionsSpec extensions, string id) =>
            extensions.Known.TryGetValue(id, out string? known) ? PackageSource.TryParse(known) : null;

        private async Task<int> CheckAndLinkAsync(
            PreparedApp prepared, DeployableVersion hostVersion, InstalledExtension extension, Version version, CancellationToken cancellationToken)
        {
            InstalledApp host = prepared.App;
            ExtensionName name = host.NameOf(extension);
            string directory = _versions.GetVersionDirectory(name, version);
            if (await _check.FindRefusalAsync(host, hostVersion, directory, cancellationToken) is string refusal)
            {
                return _outputWriter.Fail($"{name} was not installed: {host.Name} {hostVersion.Version} refused {version}: {refusal}.");
            }

            try
            {
                await _switcher.UpdateAsync(AppSwitch.OfExtension(prepared, extension, version), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return _outputWriter.Fail($"installing {name} failed: {ex.Message}");
            }

            _outputWriter.WriteLine($"Installed {name} {version} into {host.Name} {hostVersion.Version}.");
            return 0;
        }

        private async Task<string> HostNotInstalledAsync(ExtensionName name, CancellationToken cancellationToken)
        {
            string? otherOwner = await _otherScopeApps.FindOwnerOfAsync(name.Host, cancellationToken);
            return otherOwner == null
                ? $"'{name.Host}' is not installed, and {name} is an extension of it. Install it first: updaemon install {name.Host} --from <plugin>:<remote>"
                : $"'{name.Host}' is not installed for {_scope.Owner}, but for {otherOwner}. An extension goes where its host is: run this {(_scope.IsSystem ? "without" : "with")} sudo.";
        }
    }
}
