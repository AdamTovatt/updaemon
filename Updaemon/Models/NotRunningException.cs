namespace Updaemon.Models
{
    /// <summary>A service did not stay up on the versions switched to, and what was current before is again.</summary>
    public sealed class NotRunningException : Exception
    {
        public NotRunningException(string message)
            : base(message)
        {
        }
    }
}
