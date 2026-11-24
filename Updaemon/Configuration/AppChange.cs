using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Configuration
{
    /// <summary>
    /// One change to an installed app, as it is saved. Until it is committed the entry stays as it was, with a
    /// note of what is unfinished; what the change makes of it is a copy. A restart that an earlier change still
    /// owes stays noted until the service is restarted.
    /// </summary>
    public sealed class AppChange
    {
        private readonly AppEntryStore _store;
        private InstalledApp _saved;
        private bool _owesRestart;

        internal AppChange(InstalledApp before, AppValues after, AppEntryStore store)
        {
            _store = store;
            _saved = before;
            _owesRestart = before.Unfinished?.Restart == true;
            Before = before;
            Values = after;
        }

        /// <summary>The entry before the change.</summary>
        public InstalledApp Before { get; }

        /// <summary>The values of <see cref="After"/>, to edit.</summary>
        public AppValues Values { get; }

        /// <summary>The entry to edit. Saved by <see cref="CommitAsync"/> only.</summary>
        public InstalledApp After => Values.App;

        /// <summary>
        /// Saves with the entry from before what the next run is to do when this one is killed. First of all that
        /// the change writes, its secrets included.
        /// </summary>
        internal async Task SaveNoteAsync(UnfinishedChange note, CancellationToken cancellationToken) =>
            _saved = await _store.SaveEntryAsync(Before, WithOwedRestart(note), cancellationToken);

        /// <summary>Saves the secrets, if they changed, ahead of the commit: for a service that is to start with them before it.</summary>
        internal Task SaveSecretsAsync(CancellationToken cancellationToken) => _store.SaveSecretsAsync(Values, cancellationToken);

        /// <summary>From here on the change stands.</summary>
        /// <param name="left">What is left to do of it; null when nothing.</param>
        internal async Task CommitAsync(UnfinishedChange.CarryOut? left, CancellationToken cancellationToken) =>
            _saved = await _store.SaveAsync(Values, WithOwedRestart(left), cancellationToken);

        /// <summary>Once everything is back: saves the entry as it was before. Runs to its end.</summary>
        /// <param name="edit">What going back changes in it after all.</param>
        /// <param name="left">What is left to do of going back; null when nothing.</param>
        internal async Task RevertAsync(Action<InstalledApp>? edit = null, UnfinishedChange.CarryOut? left = null)
        {
            InstalledApp entry = Before.Copy();
            edit?.Invoke(entry);
            _saved = await _store.SaveEntryAsync(entry, WithOwedRestart(left), CancellationToken.None);
        }

        /// <summary>
        /// The service was restarted, so no restart is owed any more. Of a change that stands nothing is left to do
        /// then; one that is still to be put back stays noted until that is saved.
        /// </summary>
        internal async Task ServiceRestartedAsync(IOutputWriter outputWriter)
        {
            _owesRestart = false;
            if (_saved.Unfinished is UnfinishedChange.CarryOut)
            {
                await ClearNoteAsync(outputWriter);
            }
        }

        /// <summary>
        /// Nothing is left to do: saves what is saved already without its note. Runs to its end. A failure is only
        /// a warning, as what the note says is done.
        /// </summary>
        internal async Task ClearNoteAsync(IOutputWriter outputWriter)
        {
            try
            {
                _saved = await _store.SaveEntryAsync(_saved, WithOwedRestart(null), CancellationToken.None);
            }
            catch (Exception ex)
            {
                string again = _saved.Unfinished?.Restart == true ? "restarts it" : "writes its files";
                outputWriter.WriteError($"Warning: the next change to {After.Name} {again} once more: {ex.Message}");
            }
        }

        /// <summary>Once it stands: the app as it is saved, for a change to follow.</summary>
        internal PreparedApp PrepareNext() => new PreparedApp(_saved, _store);

        private UnfinishedChange? WithOwedRestart(UnfinishedChange? note) =>
            _owesRestart ? (note ?? new UnfinishedChange.CarryOut()) with { Restart = true } : note;
    }
}
