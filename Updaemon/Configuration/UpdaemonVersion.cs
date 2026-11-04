using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Updaemon.Configuration
{
    /// <summary>
    /// The version of an updaemon release: always three numbers, as its tag <c>v1.2.0</c> names it.
    /// <see cref="Current"/> is the running updaemon's. Plugins, self-update and the versions
    /// directory all go by this, so a four-part assembly version or a two-part tag cannot be mixed in.
    /// </summary>
    [JsonConverter(typeof(UpdaemonVersionJsonConverter))]
    public sealed record UpdaemonVersion : IComparable<UpdaemonVersion>
    {
        private UpdaemonVersion(Version value)
        {
            Value = value;
        }

        private Version Value { get; }

        // The assembly version, not the informational one, which the SDK may suffix with a commit hash.
        public static UpdaemonVersion Current { get; } = FromAssembly(typeof(UpdaemonVersion).Assembly.GetName().Version!);

        public static UpdaemonVersion Of(int major, int minor, int patch) => new UpdaemonVersion(new Version(major, minor, patch));

        /// <summary>The release an assembly version (<c>1.2.0.0</c>) was built for.</summary>
        public static UpdaemonVersion FromAssembly(Version assemblyVersion) =>
            Of(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0));

        /// <summary>Reads <c>x.y.z</c>; anything else, such as <c>1.3</c> or <c>1.3.0.0</c>, is not a release version.</summary>
        public static bool TryParse(string? text, [NotNullWhen(true)] out UpdaemonVersion? version)
        {
            version = text?.Split('.').Length == 3 && Version.TryParse(text, out Version? parsed)
                ? new UpdaemonVersion(parsed)
                : null;
            return version != null;
        }

        public int CompareTo(UpdaemonVersion? other) => other == null ? 1 : Value.CompareTo(other.Value);

        public static bool operator <(UpdaemonVersion left, UpdaemonVersion right) => left.CompareTo(right) < 0;

        public static bool operator >(UpdaemonVersion left, UpdaemonVersion right) => left.CompareTo(right) > 0;

        public static bool operator <=(UpdaemonVersion left, UpdaemonVersion right) => left.CompareTo(right) <= 0;

        public static bool operator >=(UpdaemonVersion left, UpdaemonVersion right) => left.CompareTo(right) >= 0;

        /// <summary>For example <c>1.2.0</c>: what <c>--version</c> prints and the release tag names after its <c>v</c>.</summary>
        public override string ToString() => Value.ToString();
    }

    /// <summary>Stores an <see cref="UpdaemonVersion"/> as its <c>x.y.z</c> text.</summary>
    public sealed class UpdaemonVersionJsonConverter : JsonConverter<UpdaemonVersion>
    {
        public override UpdaemonVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return UpdaemonVersion.TryParse(text, out UpdaemonVersion? version)
                ? version
                : throw new JsonException($"'{text}' is not an updaemon release version (x.y.z).");
        }

        public override void Write(Utf8JsonWriter writer, UpdaemonVersion value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
