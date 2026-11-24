using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Conversion
{
    /// <summary>
    /// Converts an updaemon 0.x setup, which is always system-wide, into this updaemon's. Each 0.x
    /// service or CLI tool becomes an app without a manifest, keeping its versions where they are, so
    /// nothing is downloaded. A unit with a hand edit is left alone and reported. What it replaces
    /// goes into <see cref="UpdaemonScope.ZeroXBackupDirectory"/>, from which a later run converts
    /// what is left.
    /// </summary>
    public class ZeroXConverter : IZeroXConverter
    {
        /// <summary>0.x copied its unit template here, under the scope root.</summary>
        private static readonly string[] TemplateFileNames = { "default-unit.template", "default-plist.template" };

        private readonly UpdaemonScope _scope;
        private readonly IConfigManager _configManager;
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly ITimerManager _timerManager;
        private readonly IExecutableDetector _executableDetector;
        private readonly ISymlinkManager _symlinkManager;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly AppFileWriter _appFileWriter;
        private readonly PluginProvider _pluginProvider;
        private readonly IPluginUrlResolver _pluginUrlResolver;
        private readonly IOutputWriter _outputWriter;

        public ZeroXConverter(
            UpdaemonScope scope,
            IConfigManager configManager,
            VersionStore versions,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            ITimerManager timerManager,
            IExecutableDetector executableDetector,
            ISymlinkManager symlinkManager,
            IFilePermissionManager filePermissionManager,
            AppFileWriter appFileWriter,
            PluginProvider pluginProvider,
            IPluginUrlResolver pluginUrlResolver,
            IOutputWriter outputWriter)
        {
            _scope = scope;
            _configManager = configManager;
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _timerManager = timerManager;
            _executableDetector = executableDetector;
            _symlinkManager = symlinkManager;
            _filePermissionManager = filePermissionManager;
            _appFileWriter = appFileWriter;
            _pluginProvider = pluginProvider;
            _pluginUrlResolver = pluginUrlResolver;
            _outputWriter = outputWriter;
        }

        private string BackupConfigPath => Path.Combine(_scope.ZeroXBackupDirectory, "config.json");

        public bool IsPending()
        {
            return _scope.IsSystem && File.Exists(_scope.ConfigFilePath) && ZeroXConfig.IsZeroX(File.ReadAllText(_scope.ConfigFilePath));
        }

        public async Task<bool> RunAsync(bool dryRun, CancellationToken cancellationToken = default)
        {
            bool firstRun = IsPending();
            string? configPath = firstRun ? _scope.ConfigFilePath : File.Exists(BackupConfigPath) ? BackupConfigPath : null;
            if (configPath == null)
            {
                _outputWriter.WriteLine("There is no updaemon 0.x setup to convert.");
                return true;
            }

            string oldJson = await File.ReadAllTextAsync(configPath, cancellationToken);
            if (firstRun && File.Exists(BackupConfigPath) && await File.ReadAllTextAsync(BackupConfigPath, cancellationToken) != oldJson)
            {
                _outputWriter.WriteError(
                    $"Error: {BackupConfigPath} holds the 0.x config of an earlier conversion, which 'updaemon migrate' still converts from. " +
                    $"Move {_scope.ZeroXBackupDirectory} aside to convert this 0.x config instead.");
                return false;
            }

            ConversionPlan plan = await PlanAsync(ZeroXConfig.Parse(oldJson), firstRun, cancellationToken);
            if (!plan.FirstRun && plan.IsEmpty)
            {
                _outputWriter.WriteLine("Everything from updaemon 0.x is converted; there is nothing left to do.");
                return true;
            }

            if (dryRun)
            {
                Preview(plan);
                return true;
            }

            _outputWriter.WriteLine($"Converting the updaemon 0.x setup; what it replaces is kept in {_scope.ZeroXBackupDirectory}.");
            bool allConverted = true;
            if (plan.FirstRun)
            {
                await BackUpAndStartConfigAsync(plan.Old, cancellationToken);
            }

            foreach (PluginPlan plugin in plan.Plugins)
            {
                allConverted &= await ConvertPluginAsync(plugin, cancellationToken);
            }

            foreach (AppPlan app in plan.Apps)
            {
                allConverted &= app switch
                {
                    ReadyApp ready => await ConvertAppAsync(ready, cancellationToken),
                    BlockedApp blocked => ReportNotConverted(blocked),
                    _ => throw new InvalidOperationException($"Unknown plan {app}."),
                };
            }

            if (plan.Timer != null)
            {
                allConverted &= await ConvertTimerAsync(plan.Timer, cancellationToken);
            }

            _outputWriter.WriteLine(allConverted
                ? "The updaemon 0.x setup is converted."
                : "Not everything was converted; the lines above say why. Fix what they name, then run 'updaemon migrate' again.");
            return allConverted;
        }

        /// <summary>
        /// What is left to convert. <paramref name="FirstRun"/>: the config is still 0.x's, so it is
        /// backed up and replaced first.
        /// </summary>
        private sealed record ConversionPlan(ZeroXConfig Old, bool FirstRun, IReadOnlyList<PluginPlan> Plugins, IReadOnlyList<AppPlan> Apps, ZeroXTimer? Timer)
        {
            public bool IsEmpty => Plugins.Count == 0 && Apps.Count == 0 && Timer == null;
        }

        /// <summary>A 0.x plugin, and the registry plugin it is a build of; null when it is none.</summary>
        private sealed record PluginPlan(ZeroXPlugin Old, string? RegistryName);

        private abstract record AppPlan(ZeroXApp Old);

        private sealed record BlockedApp(ZeroXApp Old, string Problem) : AppPlan(Old);

        /// <param name="ZeroXUnitText">The 0.x unit, put back if converting fails; null for a command.</param>
        /// <param name="ZeroXLinks">Where 0.x linked the command: emptied for the wrapper, linked again if converting fails.</param>
        /// <param name="UnitRunAs">The user the 0.x unit itself names, which a drop-in can override.</param>
        /// <param name="DropIns">The 0.x unit's drop-ins, in the order systemd applies them; left in place.</param>
        private sealed record ReadyApp(
            ZeroXApp Old,
            InstalledApp App,
            DeployableVersion Current,
            string? ZeroXUnitText,
            IReadOnlyList<string> ZeroXLinks,
            string? UnitRunAs,
            IReadOnlyList<ZeroXDropIn> DropIns) : AppPlan(Old)
        {
            public string Kind => Current.Manifest.IsService ? "service" : "command";

            public string RunningAs => App.RunAs == null ? string.Empty : $", running as {App.RunAs}";
        }

        private async Task<ConversionPlan> PlanAsync(ZeroXConfig old, bool firstRun, CancellationToken cancellationToken)
        {
            // On the first run the config is still 0.x's and nothing is converted yet.
            UpdaemonConfig converted = firstRun ? new UpdaemonConfig() : await _configManager.LoadConfigAsync(cancellationToken);

            List<PluginPlan> plugins = old.InstalledPlugins.Values
                .Where(plugin => !converted.InstalledPlugins.ContainsKey(plugin.Alias))
                .Select(plugin => new PluginPlan(plugin, _pluginUrlResolver.FindPluginName(Path.GetFileName(plugin.Path))))
                .ToList();

            List<AppPlan> apps = new List<AppPlan>();
            foreach (ZeroXApp app in old.Services.Where(app => !converted.Apps.Any(convertedApp => convertedApp.Name == app.LocalName)))
            {
                apps.Add(await PlanAppAsync(app, cancellationToken));
            }

            return new ConversionPlan(old, firstRun, plugins, apps, await _timerManager.FindZeroXTimerAsync(cancellationToken));
        }

        private async Task<AppPlan> PlanAppAsync(ZeroXApp old, CancellationToken cancellationToken)
        {
            string name = old.LocalName;
            if (!AppManifest.IsValidAppName(name))
            {
                return new BlockedApp(old, $"'{name}' is not a valid app name; reinstall it under another name with 'updaemon install'");
            }

            string unitPath = _scope.GetUnitFilePath(name);
            Version current;
            try
            {
                current = await _versions.GetCurrentAsync(new AppName(name), cancellationToken);
            }
            catch (InvalidOperationException)
            {
                string install = old.ServiceType == ZeroXAppType.Service && File.Exists(unitPath)
                    ? $"remove {unitPath}, then install it with 'updaemon install'"
                    : "install it with 'updaemon install'";
                return new BlockedApp(old, $"it has no current version (0.x 'init' never ran); {install}");
            }

            string versionDirectory = Path.Combine(_scope.GetAppDirectory(name), current.ToString());
            string currentLink = _scope.GetCurrentLinkPath(name);
            InstalledApp app = new InstalledApp
            {
                Name = name,
                Plugin = old.DistributionPluginAlias,
                Remote = old.RemoteName,
                AutoRestart = old.AutoRestart,
            };

            string executable;
            string? unitText = null;
            string? unitRunAs = null;
            List<ZeroXDropIn> dropIns = new List<ZeroXDropIn>();
            List<string> zeroXLinks = new List<string>();
            if (old.ServiceType == ZeroXAppType.Service)
            {
                string? unitSource = FindZeroXUnit(name, unitPath);
                if (unitSource == null)
                {
                    return new BlockedApp(old, $"its unit {unitPath} is missing");
                }

                unitText = await File.ReadAllTextAsync(unitSource, cancellationToken);
                ZeroXUnitReading result = ZeroXUnit.Read(unitText, name, currentLink, _scope.Platform.IsMacOS);
                if (result is ZeroXUnitReading.HandEdited edited)
                {
                    string allowed = _scope.Platform.IsMacOS ? "its EnvironmentVariables and UserName" : "Environment= and User= lines";
                    return new BlockedApp(old, $"{unitPath} was edited by hand: {edited.Problem}. Make it what updaemon 0.x wrote, apart from {allowed}");
                }

                ZeroXUnitReading.AsWritten reading = (ZeroXUnitReading.AsWritten)result;
                executable = reading.ExecutableName;
                if (!File.Exists(Path.Combine(versionDirectory, executable)))
                {
                    return new BlockedApp(old, $"its executable {Path.Combine(versionDirectory, executable)} is missing");
                }

                foreach (KeyValuePair<string, string> setting in reading.Settings)
                {
                    app.Settings[setting.Key] = setting.Value;
                }

                // launchd has no drop-ins.
                IReadOnlyList<string> dropInPaths = _scope.Platform.IsMacOS ? Array.Empty<string>() : ZeroXDropIn.FindPaths(unitPath);
                foreach (string dropInPath in dropInPaths)
                {
                    ZeroXDropInReading dropInReading = ZeroXDropIn.Read(dropInPath, await File.ReadAllTextAsync(dropInPath, cancellationToken));
                    if (dropInReading is ZeroXDropInReading.Unreadable unreadable)
                    {
                        return new BlockedApp(old, $"its drop-in {dropInPath} {unreadable.Problem}");
                    }

                    dropIns.Add(((ZeroXDropInReading.Read)dropInReading).DropIn);
                }

                // As systemd applies them: the unit, then each drop-in, the last User= winning. A variable a
                // drop-in sets stays the drop-in's: as a setting too, the unit's value would win, since the
                // wrapper loads settings after systemd has set the drop-in's.
                unitRunAs = reading.RunAs;
                ZeroXDropIn? userDropIn = dropIns.LastOrDefault(dropIn => dropIn.User != null);
                app.RunAs = userDropIn != null ? userDropIn.User!.Name : unitRunAs;
                foreach (string variable in dropIns.SelectMany(dropIn => dropIn.Variables))
                {
                    app.Settings.Remove(variable);
                }

                if (app.RunAs != null && !await _filePermissionManager.UserExistsAsync(app.RunAs, cancellationToken))
                {
                    return new BlockedApp(old, $"it runs as '{app.RunAs}', and there is no user by that name");
                }
            }
            else
            {
                string? found = await _executableDetector.FindExecutableAsync(versionDirectory, old.ExecutableName ?? name, cancellationToken);
                if (found == null)
                {
                    return new BlockedApp(old, $"no executable named '{old.ExecutableName ?? name}' is in {versionDirectory}");
                }

                executable = Path.GetRelativePath(versionDirectory, found);
                string command = Path.GetFileName(found);
                foreach (string linkName in new[] { command, name }.Distinct())
                {
                    string linkPath = _scope.GetCommandPath(linkName);
                    if (await IsZeroXLinkPlaceAsync(linkPath, Path.Combine(currentLink, command), name, cancellationToken))
                    {
                        zeroXLinks.Add(linkPath);
                    }
                    else if (linkName == command)
                    {
                        return new BlockedApp(old, $"{linkPath} is not the link updaemon 0.x made");
                    }
                }
            }

            app.Manifest = new AppManifest
            {
                Executable = old.ExecutableName ?? name,
                Command = old.ServiceType == ZeroXAppType.Cli ? Path.GetFileName(executable) : null,
                Service = old.ServiceType == ZeroXAppType.Service ? new ServiceSpec() : null,
            };

            DeployableVersion deployable = new DeployableVersion
            {
                Version = current,
                Directory = versionDirectory,
                Manifest = app.Manifest with { Executable = executable },
                ManifestInDirectory = false,
            };
            return new ReadyApp(old, app, deployable, unitText, zeroXLinks, unitRunAs, dropIns);
        }

        /// <summary>
        /// The 0.x unit to convert: the one in place, unless a conversion cut off before it finished
        /// already wrote the app's settings file (0.x had none) and maybe its new unit. Then it is the
        /// copy that conversion kept. Null when there is none.
        /// </summary>
        private string? FindZeroXUnit(string appName, string unitPath)
        {
            string kept = Path.Combine(_scope.ZeroXBackupDirectory, Path.GetFileName(unitPath));
            if (File.Exists(_scope.GetSettingsFilePath(appName)) && File.Exists(kept))
            {
                return kept;
            }

            return File.Exists(unitPath) ? unitPath : null;
        }

        /// <summary>
        /// Whether <paramref name="path"/> holds 0.x's link to <paramref name="target"/>, or what a cut-off
        /// conversion left in its place: nothing, or the app's own wrapper.
        /// </summary>
        private async Task<bool> IsZeroXLinkPlaceAsync(string path, string target, string appName, CancellationToken cancellationToken)
        {
            if (await _symlinkManager.ReadSymlinkAsync(path, cancellationToken) is string linkTarget)
            {
                return linkTarget == target;
            }

            bool empty = !File.Exists(path) && !Directory.Exists(path);
            return empty || AppWrapper.ReadOwner(path) == appName;
        }

        private void Preview(ConversionPlan plan)
        {
            _outputWriter.WriteLine("Converting the updaemon 0.x setup would:");
            foreach (PluginPlan plugin in plan.Plugins)
            {
                _outputWriter.WriteLine(plugin.RegistryName != null
                    ? $"  reinstall the '{plugin.Old.Alias}' plugin from this updaemon's release, keeping its secrets"
                    : $"  leave out the '{plugin.Old.Alias}' plugin, which is not in the plugin registry: {DistInstallHint(plugin.Old.Alias)}");
            }

            foreach (AppPlan app in plan.Apps)
            {
                if (app is ReadyApp ready)
                {
                    string settings = ready.App.Settings.Count == 0 ? string.Empty : $", with the settings {string.Join(", ", ready.App.Settings.Keys.Order(StringComparer.Ordinal))}";
                    _outputWriter.WriteLine($"  convert {app.Old.LocalName} ({ready.Kind}) at {ready.Current.Version}{ready.RunningAs}{settings}");
                    foreach (string line in DescribeDropIns(ready))
                    {
                        _outputWriter.WriteLine($"    {line}");
                    }
                }
                else if (app is BlockedApp blocked)
                {
                    _outputWriter.WriteLine($"  leave out {app.Old.LocalName}: {blocked.Problem}");
                }
            }

            if (plan.Timer != null)
            {
                _outputWriter.WriteLine($"  {DescribeTimer(plan.Timer, done: false)}");
            }

            _outputWriter.WriteLine("Nothing was changed.");
        }

        /// <summary>The drop-ins converting leaves in place, and which user wins when one overrides the unit's.</summary>
        private static IEnumerable<string> DescribeDropIns(ReadyApp app)
        {
            foreach (ZeroXDropIn dropIn in app.DropIns)
            {
                string sets = dropIn.Lines.Count == 0 ? "sets nothing" : $"still sets {string.Join("; ", dropIn.Lines)}";
                yield return $"its drop-in {dropIn.Path} stays in place and {sets}";
            }

            ZeroXDropIn? overriding = app.DropIns.LastOrDefault(dropIn => dropIn.User != null);
            if (app.UnitRunAs != null && overriding != null && overriding.User!.Name != app.UnitRunAs)
            {
                yield return $"its drop-in {overriding.Path} overrides its unit's User={app.UnitRunAs}, so it runs as {app.App.RunAs ?? "root"}, as before";
            }
        }

        private static string DescribeTimer(ZeroXTimer timer, bool done)
        {
            if (!timer.IsOn)
            {
                return done ? "The update timer was off and stays off." : "leave the update timer off, as it is";
            }

            return timer.Interval is TimeSpan interval
                ? done ? $"The update timer runs every {interval} as before." : $"rewrite the update timer, keeping its interval of {interval}"
                : done ? "The update timer is off, as its interval could not be read; set it with 'updaemon timer <interval>'." : "turn the update timer off, as its interval cannot be read";
        }

        private static string DistInstallHint(string alias) => $"install it again with 'updaemon dist-install --as {alias} <url>'";

        /// <summary>Backs up the 0.x config and template, then replaces the config with one in this updaemon's format.</summary>
        private async Task BackUpAndStartConfigAsync(ZeroXConfig old, CancellationToken cancellationToken)
        {
            ZeroXBackup.Keep(_scope.ConfigFilePath, _scope.ZeroXBackupDirectory);
            foreach (string template in TemplateFileNames.Select(name => Path.Combine(_scope.RootDirectory, name)).Where(File.Exists))
            {
                ZeroXBackup.Keep(template, _scope.ZeroXBackupDirectory);
                File.Delete(template);
            }

            await _configManager.SaveConfigAsync(new UpdaemonConfig { ReleaseRetentionCount = old.ReleaseRetentionCount }, cancellationToken);
        }

        private async Task<bool> ConvertPluginAsync(PluginPlan plugin, CancellationToken cancellationToken)
        {
            if (plugin.RegistryName == null)
            {
                _outputWriter.WriteError($"Not converted: the '{plugin.Old.Alias}' plugin is not in the plugin registry; {DistInstallHint(plugin.Old.Alias)}.");
                return false;
            }

            try
            {
                InstalledPluginInfo installed = await _pluginProvider.ReinstallAsync(plugin.Old.Alias, plugin.RegistryName, cancellationToken);
                if (installed.Path != plugin.Old.Path && File.Exists(plugin.Old.Path))
                {
                    File.Delete(plugin.Old.Path);
                }

                return true;
            }
            catch (PluginException ex)
            {
                // The apps using it are still converted: they reinstall it when next used.
                _outputWriter.WriteError($"Not converted: {ex.Message}");
                return false;
            }
        }

        private bool ReportNotConverted(BlockedApp app)
        {
            _outputWriter.WriteError($"Not converted: {app.Old.LocalName}: {app.Problem}.");
            return false;
        }

        private async Task<bool> ConvertAppAsync(ReadyApp plan, CancellationToken cancellationToken)
        {
            InstalledApp app = plan.App;
            DeployableVersion current = plan.Current;
            string unitPath = _scope.GetUnitFilePath(app.Name);
            if (plan.ZeroXUnitText != null)
            {
                ZeroXBackup.Keep(unitPath, _scope.ZeroXBackupDirectory);
            }

            try
            {
                // The wrapper takes the 0.x links' place on PATH.
                foreach (string link in plan.ZeroXLinks)
                {
                    File.Delete(link);
                }

                await WriteManifestsIntoOlderVersionsAsync(app, current.Version, cancellationToken);
                ReachableServiceManager reachable = await _serviceManager.EnsureReachableAsync(cancellationToken);
                await _serviceDeployer.MakeExecutableAsync(app.Name, current, cancellationToken);
                await _versions.MakeReadableAsync(app.PackageName, cancellationToken);
                await _serviceDeployer.ActivateAsync(app, current, reachable, cancellationToken);
                if (current.Manifest.IsService)
                {
                    await _serviceManager.EnableServiceAsync(app.Name, cancellationToken);
                    if (app.AutoRestart && !await _serviceDeployer.RestartAsync(app.Name, reachable, cancellationToken))
                    {
                        throw new InvalidOperationException(ServiceDeployer.NotRunning(app.Name, current.Version, " with its new unit, so its 0.x unit is back"));
                    }
                }

                await _configManager.SaveAppAsync(app, cancellationToken);
            }
            catch (Exception ex)
            {
                // Also when cancelled: half converted, the app would be neither 0.x's nor this updaemon's.
                await RestoreAsync(plan, unitPath);
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                _outputWriter.WriteError($"Not converted: {app.Name}: {ex.Message.TrimEnd('.')}.");
                return false;
            }

            string restart = current.Manifest.IsService && !app.AutoRestart ? "; it runs as before until it restarts (restart=manual)" : string.Empty;
            string command = current.Manifest.Command != null && current.Manifest.Command != app.Name ? $"; its command is now only '{current.Manifest.Command}'" : string.Empty;
            _outputWriter.WriteLine($"Converted {app.Name} ({plan.Kind}) at {current.Version}{plan.RunningAs}{restart}{command}.");
            foreach (string line in DescribeDropIns(plan))
            {
                _outputWriter.WriteLine($"  {char.ToUpperInvariant(line[0])}{line.Substring(1)}.");
            }

            return true;
        }

        /// <summary>Kept versions get the manifest the current one runs with, so they can be rolled back to.</summary>
        private async Task WriteManifestsIntoOlderVersionsAsync(InstalledApp app, Version current, CancellationToken cancellationToken)
        {
            foreach (Version version in _versions.GetRetained(app.PackageName).Where(version => version != current))
            {
                string directory = Path.Combine(_scope.GetAppDirectory(app.Name), version.ToString());
                string? found = await _executableDetector.FindExecutableAsync(directory, app.Manifest!.Executable, cancellationToken);
                if (found != null)
                {
                    await (app.Manifest with { Executable = Path.GetRelativePath(directory, found) }).WriteAsync(directory, cancellationToken);
                }
            }
        }

        /// <summary>Puts back what a failed conversion replaced, so the app runs as under 0.x. Best effort, and not cancelled.</summary>
        private async Task RestoreAsync(ReadyApp plan, string unitPath)
        {
            string name = plan.Old.LocalName;
            try
            {
                _appFileWriter.RemoveCommandAndSettings(name, plan.Current.Manifest);
                string target = Path.Combine(_scope.GetCurrentLinkPath(name), Path.GetFileName(plan.Current.Manifest.Executable));
                foreach (string link in plan.ZeroXLinks.Where(link => !File.Exists(link)))
                {
                    File.CreateSymbolicLink(link, target);
                }

                if (plan.ZeroXUnitText != null)
                {
                    await AtomicFile.WriteAsync(unitPath, plan.ZeroXUnitText, AtomicFile.Readable, CancellationToken.None);
                    await _serviceManager.DaemonReloadAsync(CancellationToken.None);
                    if (plan.Old.AutoRestart)
                    {
                        await _serviceDeployer.RestartAsync(name, await _serviceManager.EnsureReachableAsync(CancellationToken.None), CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Warning: putting {name}'s 0.x files back failed: {ex.Message}");
            }
        }

        private async Task<bool> ConvertTimerAsync(ZeroXTimer timer, CancellationToken cancellationToken)
        {
            try
            {
                // The new timer first: if it cannot be set, the 0.x one keeps running and a later run tries again.
                foreach (string file in timer.Files)
                {
                    ZeroXBackup.Keep(file, _scope.ZeroXBackupDirectory);
                }

                ReachableServiceManager reachable = await _serviceManager.EnsureReachableAsync(cancellationToken);
                if (timer.IsOn && timer.Interval is TimeSpan interval)
                {
                    await _timerManager.SetTimerAsync(interval, reachable, cancellationToken);
                }
                else
                {
                    // Recorded as 'timer -' does, so install.sh's 'timer 1h --keep' leaves it off.
                    UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
                    config.TimerTurnedOff = true;
                    await _configManager.SaveConfigAsync(config, cancellationToken);
                }

                await _timerManager.RemoveZeroXTimerAsync(timer, reachable, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Not converted: the update timer: {ex.Message.TrimEnd('.')}.");
                return false;
            }

            _outputWriter.WriteLine(DescribeTimer(timer, done: true));
            return true;
        }
    }
}
