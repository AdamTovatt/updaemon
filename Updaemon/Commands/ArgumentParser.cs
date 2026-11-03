namespace Updaemon.Commands
{
    /// <summary>
    /// Parses a command's arguments. A <c>--name</c> argument takes the next argument as its value
    /// unless it is one of the boolean flags given to the constructor. Everything after a bare
    /// <c>--</c> is taken literally, as <see cref="Rest"/>.
    /// </summary>
    public class ArgumentParser
    {
        private const string EndOfOptions = "--";

        private readonly string[] _args;
        private readonly HashSet<string> _booleanFlags;

        public ArgumentParser(string[] args, params string[] booleanFlags)
        {
            int end = Array.IndexOf(args, EndOfOptions);
            _args = end >= 0 ? args[..end] : args;
            Rest = end >= 0 ? args[(end + 1)..] : null;
            _booleanFlags = new HashSet<string>(booleanFlags, StringComparer.Ordinal);
        }

        /// <summary>The arguments after <c>--</c>; null when there is no <c>--</c>.</summary>
        public IReadOnlyList<string>? Rest { get; }

        /// <summary>
        /// Arguments before <c>--</c> that are neither flags nor flag values.
        /// </summary>
        public IReadOnlyList<string> Positionals
        {
            get
            {
                List<string> positionals = new List<string>();
                for (int i = 0; i < _args.Length; i++)
                {
                    string arg = _args[i];
                    if (IsFlag(arg))
                    {
                        if (!_booleanFlags.Contains(arg))
                        {
                            i++; // skip the flag's value
                        }

                        continue;
                    }

                    positionals.Add(arg);
                }

                return positionals;
            }
        }

        public string? GetPositional(int index)
        {
            IReadOnlyList<string> positionals = Positionals;
            return index < positionals.Count ? positionals[index] : null;
        }

        /// <summary>
        /// Gets the first non-flag argument (useful for commands like dist-install).
        /// </summary>
        public string? GetFirstNonFlag() => GetPositional(0);

        /// <summary>
        /// Gets the value of a named flag (e.g., --from &lt;value&gt;); the first one if it is repeated.
        /// </summary>
        public string? GetFlag(string flagName) => GetFlagValues(flagName).FirstOrDefault();

        /// <summary>
        /// Gets every value of a flag that may be repeated (e.g., --set A=1 --set B=2).
        /// </summary>
        public IReadOnlyList<string> GetFlagValues(string flagName)
        {
            List<string> values = new List<string>();
            for (int i = 0; i < _args.Length - 1; i++)
            {
                if (_args[i] == flagName)
                {
                    values.Add(_args[i + 1]);
                    i++;
                }
            }

            return values;
        }

        /// <summary>
        /// Checks if a boolean flag is present (e.g., --force).
        /// </summary>
        public bool HasFlag(string flagName) => _args.Contains(flagName);

        /// <summary>
        /// Flags given before <c>--</c> that are not in <paramref name="knownFlags"/>.
        /// </summary>
        public IReadOnlyList<string> GetUnknownFlags(params string[] knownFlags)
        {
            return _args.Where(arg => IsFlag(arg) && !knownFlags.Contains(arg)).ToList();
        }

        private static bool IsFlag(string arg) => arg.StartsWith("--", StringComparison.Ordinal);
    }
}
