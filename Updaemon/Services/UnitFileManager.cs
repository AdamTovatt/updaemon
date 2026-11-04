using System.Text;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Generates an app's service definition in code: a systemd unit on Linux, a launchd plist on
    /// macOS, for the system or the user scope.
    /// </summary>
    public class UnitFileManager : IUnitFileManager
    {
        private readonly UpdaemonScope _scope;

        public UnitFileManager(UpdaemonScope scope)
        {
            _scope = scope;
        }

        public async Task WriteUnitFileAsync(ServiceUnit unit, CancellationToken cancellationToken = default)
        {
            // Neither holds a secret, and 0644 is what systemd and launchd expect.
            await AtomicFile.WriteAsync(_scope.GetUnitFilePath(unit.AppName), Render(unit), AtomicFile.Readable, cancellationToken);
        }

        public string Render(ServiceUnit unit) =>
            _scope.Platform.IsMacOS ? RenderPlist(unit) : RenderSystemdUnit(unit);

        private string RenderSystemdUnit(ServiceUnit unit)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[Unit]");
            builder.AppendLine($"Description={SystemdSyntax.EscapeSpecifiers(unit.AppName)} (managed by updaemon)");
            if (_scope.IsSystem)
            {
                builder.AppendLine("After=network.target");
            }

            builder.AppendLine();
            builder.AppendLine("[Service]");
            builder.AppendLine("Type=simple");
            if (unit.RunAs != null)
            {
                builder.AppendLine($"User={SystemdSyntax.EscapeSpecifiers(unit.RunAs)}");
            }

            builder.AppendLine($"WorkingDirectory={SystemdSyntax.EscapeSpecifiers(unit.WorkingDirectory)}");
            IEnumerable<string> command = new[] { unit.WrapperPath }.Concat(unit.Arguments);
            builder.AppendLine($"ExecStart={string.Join(' ', command.Select(SystemdSyntax.QuoteArgument))}");
            builder.AppendLine("Restart=always");
            builder.AppendLine("RestartSec=10");
            builder.AppendLine($"SyslogIdentifier={SystemdSyntax.EscapeSpecifiers(unit.AppName)}");
            builder.AppendLine();
            builder.AppendLine("[Install]");
            builder.AppendLine(_scope.IsSystem ? "WantedBy=multi-user.target" : "WantedBy=default.target");
            return builder.ToString();
        }

        private string RenderPlist(ServiceUnit unit)
        {
            Launchd.Plist plist = new Launchd.Plist().String("Label", _scope.GetServiceLabel(unit.AppName));
            if (unit.RunAs != null)
            {
                plist.String("UserName", unit.RunAs);
            }

            return plist
                .String("WorkingDirectory", unit.WorkingDirectory)
                .Strings("ProgramArguments", new[] { unit.WrapperPath }.Concat(unit.Arguments))
                .Boolean("RunAtLoad", true)
                .Boolean("KeepAlive", true)
                .Integer("ThrottleInterval", 10)
                .String("StandardOutPath", _scope.GetOutputLogPath(unit.AppName))
                .String("StandardErrorPath", _scope.GetErrorLogPath(unit.AppName))
                .ToString();
        }
    }
}
