using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'self-update' command: turns updaemon's own updates on or off for this scope.
    /// </summary>
    public class SelfUpdateCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;

        public SelfUpdateCommand(IConfigManager configManager, IOutputWriter outputWriter, UpdaemonScope scope)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _scope = scope;
        }

        public string Name => "self-update";

        public string Description => "Turn updaemon's own updates on or off";

        public string Usage => "updaemon self-update [on|off]";

        /// <summary>Plain 'self-update' only shows whether it is on.</summary>
        public bool ChangesState(string[] args) => args.Length > 0;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            switch (parser.Positionals)
            {
                case []:
                    _outputWriter.WriteLine($"Self-update is {(config.SelfUpdate ? "on" : "off")}.");
                    return 0;
                case [string state] when state is "on" or "off":
                    config.SelfUpdate = state == "on";
                    await _configManager.SaveConfigAsync(config, cancellationToken);
                    _outputWriter.WriteLine($"Self-update is {state}: 'updaemon update' {(config.SelfUpdate ? "also updates" : "no longer updates")} {_scope.UpdaemonCommandPath} and its GitHub plugins.");
                    return 0;
                default:
                    return _outputWriter.FailUsage(this);
            }
        }

        public string GetDetailedHelp()
        {
            return """
                Self-Update Command

                Usage:
                  updaemon self-update [on|off]

                Description:
                  When on (the default), 'updaemon update' ends by updating updaemon itself
                  to its latest release, for this scope: system-wide as root, or your own.
                  The new version is installed beside the old one and must run before the
                  'updaemon' command is switched to it. Registry plugins follow the next
                  time they are used. Plugins installed with 'dist-install github:<owner>/<repo>'
                  are updated to their repository's latest release in the same step.
                  Without an argument, shows whether it is on.

                Examples:
                  updaemon self-update
                  updaemon self-update off
                """;
        }
    }
}
