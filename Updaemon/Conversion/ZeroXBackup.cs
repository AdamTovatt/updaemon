namespace Updaemon.Conversion
{
    /// <summary>
    /// The 0.x backup directory is what <c>migrate</c> converts from, so nothing in it is ever
    /// replaced: the first copy of a file is the one kept.
    /// </summary>
    public static class ZeroXBackup
    {
        /// <summary>Copies <paramref name="file"/> into <paramref name="backupDirectory"/> unless a file of that name is already there.</summary>
        public static void Keep(string file, string backupDirectory)
        {
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, Path.GetFileName(file));
            if (!File.Exists(backup))
            {
                File.Copy(file, backup);
            }
        }
    }
}
