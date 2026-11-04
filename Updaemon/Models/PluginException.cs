namespace Updaemon.Models
{
    /// <summary>
    /// A plugin refused or failed a call. The message is the plugin's own, meant for the
    /// user as is: it says what went wrong and what to change, with no stack trace.
    /// </summary>
    public class PluginException : Exception
    {
        public PluginException(string message)
            : base(message)
        {
        }
    }
}
