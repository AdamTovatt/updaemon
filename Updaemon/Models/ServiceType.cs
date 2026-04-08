using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter<ServiceType>))]
    public enum ServiceType
    {
        Service = 0,
        Cli = 1,
    }

    public static class ServiceTypeExtensions
    {
        /// <summary>
        /// Returns a human-readable label for the service type (e.g. "service" or "CLI tool").
        /// </summary>
        public static string ToLabel(this ServiceType serviceType)
        {
            return serviceType == ServiceType.Cli ? "CLI tool" : "service";
        }
    }
}
