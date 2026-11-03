namespace Updaemon.Commands
{
    /// <summary>
    /// Guides 'updaemon help &lt;topic&gt;' prints beside the commands' own help. Each is a file of the
    /// repository embedded at build time and printed as is, so the binary and the file cannot drift.
    /// </summary>
    public static class HelpTopics
    {
        public sealed record Topic(string Name, string Description, string ResourceName);

        public static IReadOnlyList<Topic> All { get; } = new[]
        {
            new Topic("publishing", "How to publish an app for updaemon", "PublishingWithUpdaemon.md"),
        };

        public static Topic? Find(string name) => All.FirstOrDefault(topic => topic.Name == name);

        public static string Read(Topic topic)
        {
            using Stream stream = typeof(HelpTopics).Assembly.GetManifestResourceStream(topic.ResourceName)
                ?? throw new InvalidOperationException($"This updaemon was built without {topic.ResourceName}.");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
