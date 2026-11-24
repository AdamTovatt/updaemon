namespace Updaemon.IO
{
    /// <summary>File operations .NET leaves a step short of what updaemon needs.</summary>
    public static class Files
    {
        /// <summary>Deletes the file if it is there. File.Delete throws when the file's directory is missing; this does not.</summary>
        public static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Deletes the directory and everything in it, if it is there.</summary>
        public static void DeleteDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
