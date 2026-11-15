using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'migrate' command: converts what is left of an updaemon 0.x setup, or previews it.
    /// </summary>
    public class MigrateCommand : ICommand
    {
        private readonly IZeroXConverter _converter;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;

        public MigrateCommand(IZeroXConverter converter, IOutputWriter outputWriter, UpdaemonScope scope)
        {
            _converter = converter;
            _outputWriter = outputWriter;
            _scope = scope;
        }

        public string Name => "migrate";

        public string Description => "Convert an updaemon 0.x setup";

        public string Usage => "updaemon migrate [--dry-run]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, "--dry-run");
            if (parser.GetUnknownFlags("--dry-run") is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [])
            {
                return _outputWriter.FailUsage(this);
            }

            if (!_scope.IsSystem)
            {
                return _outputWriter.Fail("an updaemon 0.x setup is system-wide; run 'sudo updaemon migrate'.");
            }

            return await _converter.RunAsync(parser.HasFlag("--dry-run"), cancellationToken) ? 0 : 1;
        }

        public string GetDetailedHelp()
        {
            return """
                Migrate Command

                Usage:
                  updaemon migrate [--dry-run]

                Description:
                  Converts an updaemon 0.x setup. You rarely need to: the first command run
                  with sudo converts it by itself. This command previews the conversion, and
                  converts what an earlier one left out.

                  Each 0.x service or CLI tool becomes an app that keeps its versions, its
                  restart setting, as settings its unit's Environment= lines (on macOS, its
                  EnvironmentVariables), and as the user it runs as its unit's User= (on
                  macOS, UserName). A unit edited by hand in any other way is left alone and
                  reported: make it what updaemon 0.x wrote, then run this again.

                  A unit's drop-ins (<unit>.service.d/*.conf) are left in place and still
                  apply to the new unit, which has the same name; the report lists what each
                  still sets. As systemd reads them, the last User= wins, a drop-in's over
                  the unit's.
                  What the conversion replaces is kept in the 0.x-backup directory beside
                  config.json.

                Options:
                  --dry-run   Show what would be converted, and change nothing.

                Examples:
                  sudo updaemon migrate --dry-run
                  sudo updaemon migrate
                """;
        }
    }
}
