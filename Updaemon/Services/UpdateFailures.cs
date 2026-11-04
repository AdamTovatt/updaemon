using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// What an <c>update</c> run could not update. Each failure is reported here, which both prints it
    /// and keeps it for the run's record, so no failure is shown without being recorded.
    /// </summary>
    public sealed class UpdateFailures
    {
        private readonly IOutputWriter _outputWriter;
        private readonly List<UpdateFailure> _failures = new List<UpdateFailure>();

        public UpdateFailures(IOutputWriter outputWriter)
        {
            _outputWriter = outputWriter;
        }

        public IReadOnlyList<UpdateFailure> All => _failures;

        /// <summary>Prints <c>Error: &lt;subject&gt;: &lt;reason&gt;</c> and records it.</summary>
        public void Report(string subject, string reason) => Report(new[] { subject }, reason);

        /// <summary>One line naming every subject, recorded once for each: they failed for the same reason.</summary>
        public void Report(IReadOnlyList<string> subjects, string reason)
        {
            _outputWriter.WriteError($"Error: {string.Join(", ", subjects)}: {reason}");
            _failures.AddRange(subjects.Select(subject => new UpdateFailure(subject, reason)));
        }
    }
}
