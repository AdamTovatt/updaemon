namespace Updaemon.Models
{
    /// <summary>How far apart two strings are, for suggesting what was probably meant.</summary>
    public static class TextDistance
    {
        /// <summary>The Levenshtein distance: how many single-character insertions, deletions or substitutions turn one into the other.</summary>
        public static int Between(string a, string b)
        {
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
            for (int i = 1; i <= a.Length; i++)
            {
                int[] current = new int[b.Length + 1];
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
                }

                previous = current;
            }

            return previous[b.Length];
        }
    }
}
