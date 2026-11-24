namespace Updaemon.Models
{
    /// <summary>There is no plugin to call: it cannot be installed, or does not start. So every call to it fails.</summary>
    public sealed class PluginUnavailableException : PluginException
    {
        public PluginUnavailableException(string message)
            : base(message)
        {
        }
    }
}
