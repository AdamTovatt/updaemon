namespace Updaemon.Models
{
    /// <summary>The update timer's interval as people write and read it: <c>30s</c>, <c>5m</c>, <c>1h</c>.</summary>
    public static class TimerInterval
    {
        /// <summary>The interval <paramref name="interval"/> names, or null when it is not a positive whole number of s, m or h.</summary>
        public static TimeSpan? Parse(string interval)
        {
            interval = interval.Trim().ToLowerInvariant();
            if (interval.Length < 2 || !int.TryParse(interval[..^1], out int value) || value <= 0)
            {
                return null;
            }

            return interval[^1] switch
            {
                's' => TimeSpan.FromSeconds(value),
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                _ => null,
            };
        }

        /// <summary>In the largest unit that names it exactly.</summary>
        public static string Format(TimeSpan interval)
        {
            long seconds = (long)interval.TotalSeconds;
            return seconds % 3600 == 0 ? $"{seconds / 3600}h"
                : seconds % 60 == 0 ? $"{seconds / 60}m"
                : $"{seconds}s";
        }
    }
}
