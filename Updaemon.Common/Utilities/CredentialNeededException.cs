using Updaemon.Common.Models;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// A call failed for want of a secret. A plugin throws it for updaemon to ask for the secret at a
    /// terminal, or to name the 'updaemon secret-set' command that sets it; the message says which case it is.
    /// </summary>
    public sealed class CredentialNeededException : Exception
    {
        public CredentialNeededException(CredentialNeed need, string message)
            : base(message)
        {
            Need = need;
        }

        public CredentialNeed Need { get; }
    }
}
