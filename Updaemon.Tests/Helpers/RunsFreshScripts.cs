namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// For tests that write a script and execute it: alone, as a process another test starts in between
    /// inherits the open file for an instant, and executing it then fails with "Text file busy".
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class RunsFreshScripts
    {
        public const string Name = "Runs fresh scripts";
    }
}
