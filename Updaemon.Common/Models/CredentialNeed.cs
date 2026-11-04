using System.Text.Json.Serialization;

namespace Updaemon.Common.Models
{
    /// <summary>Why a plugin needs one of its secrets set, or set to another value.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<CredentialProblem>))]
    public enum CredentialProblem
    {
        /// <summary>The thing asked for was not found without the secret: it may be private.</summary>
        Missing,

        /// <summary>The secret that is set was refused.</summary>
        Rejected,

        /// <summary>The service's rate limit is used up; the secret raises it. The message says when it resets.</summary>
        RateLimited,
    }

    /// <summary>
    /// A call failed for want of a secret: which one, and why. Sent beside the error message so
    /// updaemon can ask for the secret instead of only showing the message.
    /// </summary>
    public sealed class CredentialNeed
    {
        /// <summary>The secret's name, as the plugin's service information lists it.</summary>
        public string Secret { get; set; } = string.Empty;

        public CredentialProblem Problem { get; set; }
    }
}
