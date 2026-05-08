using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'init' command to perform first-time download and setup of a registered service or CLI tool.
    /// </summary>
    public class InitCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IServiceManager _serviceManager;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IUnitFileManager _unitFileManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly ISymlinkManager _symlinkManager;
        private readonly string _binDirectory;

        public InitCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            IServiceDeployer serviceDeployer,
            ISymlinkManager symlinkManager)
            : this(configManager, secretsManager, serviceManager, distributionClient, outputWriter,
                   unitFileManager, serviceDeployer, symlinkManager, PlatformPaths.BinDirectory)
        {
        }

        public InitCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            IServiceDeployer serviceDeployer,
            ISymlinkManager symlinkManager,
            string binDirectory)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _unitFileManager = unitFileManager;
            _serviceDeployer = serviceDeployer;
            _symlinkManager = symlinkManager;
            _binDirectory = binDirectory;
        }

        public string Name => "init";

        public string Description => "Download and set up a registered service or CLI tool for the first time";

        public string Usage => "updaemon init <app-name>";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            if (!parser.TryGetRequiredPositional(0, "app-name", out string appName, out int errorCode))
            {
                _outputWriter.WriteLine(Usage);
                return errorCode;
            }

            // Look up service in config
            RegisteredService? service = await _configManager.GetServiceAsync(appName, cancellationToken);
            if (service == null)
            {
                _outputWriter.WriteError($"Error: '{appName}' is not registered. Use 'updaemon new' to register it first.");
                return 1;
            }

            // Check if already initialized
            string? existingTarget = await _serviceDeployer.ReadCurrentTargetAsync(service.LocalName, cancellationToken);
            if (existingTarget != null)
            {
                _outputWriter.WriteLine($"{service.ServiceType.ToLabel()} '{appName}' is already initialized.");
                return 0;
            }

            // Verify plugin exists
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(service.DistributionPluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                _outputWriter.WriteError($"Error: Plugin '{service.DistributionPluginAlias}' not found.");
                return 1;
            }

            if (!File.Exists(pluginInfo.Path))
            {
                _outputWriter.WriteError($"Error: Plugin executable not found at '{pluginInfo.Path}'.");
                return 1;
            }

            // Pre-flight: check write access
            if (service.ServiceType == ServiceType.Cli)
            {
                int preflightResult = await CheckWriteAccessAsync(_binDirectory, cancellationToken);
                if (preflightResult != 0) return preflightResult;
            }
            else
            {
                try
                {
                    await _unitFileManager.EnsureWritableAsync(cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    _outputWriter.WriteError($"Error: No write access to the unit-file directory. Run with appropriate permissions.");
                    return 1;
                }
                catch (InvalidOperationException ex)
                {
                    _outputWriter.WriteError($"Error: {ex.Message}");
                    return 1;
                }
            }

            _outputWriter.WriteLine($"Initializing {service.ServiceType.ToLabel()}: {appName}");

            // Connect to plugin
            await _distributionClient.ConnectAsync(pluginInfo.Path, cancellationToken);
            string? secrets = await _secretsManager.GetPluginSecretsFormattedAsync(service.DistributionPluginAlias, cancellationToken);
            await _distributionClient.InitializeAsync(secrets, cancellationToken);

            DeployResult? deployResult = null;
            string? unitFilePath = null;
            string? binSymlinkPath = null;
            string? binAliasSymlinkPath = null;

            try
            {
                // Get latest version
                Version? latestVersion = await _distributionClient.GetLatestVersionAsync(service.RemoteName, cancellationToken);
                if (latestVersion == null)
                {
                    _outputWriter.WriteError($"Error: No version available for '{service.RemoteName}'.");
                    return 1;
                }

                _outputWriter.WriteLine($"Latest version: {latestVersion}");

                // Deploy (download, find executable, set permissions, create symlink)
                deployResult = await _serviceDeployer.DeployVersionAsync(service, latestVersion, _distributionClient, cancellationToken);
                if (deployResult == null)
                {
                    return 1;
                }

                string detectedExecutableName = Path.GetFileName(deployResult.ExecutablePath);

                if (service.ServiceType == ServiceType.Cli)
                {
                    // Create symlink in bin directory using the executable's real name
                    binSymlinkPath = Path.Combine(_binDirectory, detectedExecutableName);
                    string targetPath = Path.Combine(deployResult.SymlinkPath, detectedExecutableName);
                    await _symlinkManager.CreateOrUpdateSymlinkAsync(binSymlinkPath, targetPath, cancellationToken);
                    _outputWriter.WriteLine($"Created symlink: {binSymlinkPath}");

                    // Create an alias symlink using the user-chosen local name, if different
                    if (!string.Equals(service.LocalName, detectedExecutableName, StringComparison.Ordinal))
                    {
                        binAliasSymlinkPath = Path.Combine(_binDirectory, service.LocalName);
                        await _symlinkManager.CreateOrUpdateSymlinkAsync(binAliasSymlinkPath, targetPath, cancellationToken);
                        _outputWriter.WriteLine($"Created alias symlink: {binAliasSymlinkPath}");
                    }

                    _outputWriter.WriteLine($"CLI tool '{appName}' initialized successfully!");
                }
                else
                {
                    // Generate and write unit file (systemd .service on Linux, launchd .plist on macOS)
                    unitFilePath = _unitFileManager.GetUnitFilePath(service.LocalName);

                    await _unitFileManager.WriteUnitFileAsync(
                        unitFilePath, service.LocalName, deployResult.SymlinkPath, detectedExecutableName, cancellationToken);
                    _outputWriter.WriteLine($"Created service unit file: {unitFilePath}");

                    // Reload manager config (no-op on launchd) and enable/start service
                    await _serviceManager.DaemonReloadAsync(cancellationToken);
                    await _serviceManager.EnableServiceAsync(service.LocalName, cancellationToken);
                    await _serviceManager.StartServiceAsync(service.LocalName, cancellationToken);
                    _outputWriter.WriteLine($"Service '{appName}' initialized and started successfully!");
                }

                return 0;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error during initialization: {ex.Message}");

                // Clean up unit file
                if (unitFilePath != null)
                {
                    try { File.Delete(unitFilePath); } catch { }
                }

                // Clean up bin symlinks
                if (binSymlinkPath != null)
                {
                    try { File.Delete(binSymlinkPath); } catch { }
                }
                if (binAliasSymlinkPath != null)
                {
                    try { File.Delete(binAliasSymlinkPath); } catch { }
                }

                // Clean up deploy artifacts (version directory + symlink)
                if (deployResult != null)
                {
                    await _serviceDeployer.CleanupDeployAsync(deployResult, cancellationToken);
                    _outputWriter.WriteLine("Cleaned up deploy artifacts");
                }

                return 1;
            }
            finally
            {
                await _distributionClient.DisposeAsync();
            }
        }

        private async Task<int> CheckWriteAccessAsync(string directory, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directory))
            {
                _outputWriter.WriteError($"Error: Directory '{directory}' does not exist.");
                return 1;
            }

            string testFilePath = Path.Combine(directory, $".updaemon-init-check-{Guid.NewGuid()}");
            try
            {
                await File.WriteAllTextAsync(testFilePath, "", cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                _outputWriter.WriteError($"Error: No write access to '{directory}'. Run with appropriate permissions.");
                return 1;
            }
            finally
            {
                try { File.Delete(testFilePath); } catch { }
            }

            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Init Command

                Usage:
                  updaemon init <app-name>

                Description:
                  Downloads and sets up a registered service or CLI tool for the first time.

                  For services: downloads the latest version, detects the executable, creates
                  the service unit file (systemd on Linux, launchd on macOS), and starts the service.

                  For CLI tools: downloads the latest version, detects the executable, and
                  creates a symlink in /usr/local/bin so the tool is available on PATH.

                  The entry must first be registered with 'updaemon new'. If it is already
                  initialized, this command does nothing.

                Examples:
                  updaemon init my-api
                  updaemon init ripgrep
                """;
        }
    }
}
