namespace Updaemon.Models
{
    /// <summary>
    /// A plugin speaks another plugin protocol version than this updaemon, so no call is made to it.
    /// </summary>
    public sealed class PluginProtocolException : PluginException
    {
        public PluginProtocolException(string message)
            : base(message)
        {
        }
    }
}
