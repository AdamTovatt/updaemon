using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Helper for parsing command-line arguments.
    /// </summary>
    public class ArgumentParser
    {
        private readonly string[] _args;
        private readonly IOutputWriter _outputWriter;

        public ArgumentParser(string[] args, IOutputWriter outputWriter)
        {
            _args = args;
            _outputWriter = outputWriter;
        }

        /// <summary>
        /// Gets a positional argument at the specified index.
        /// </summary>
        public string? GetPositional(int index)
        {
            return index < _args.Length ? _args[index] : null;
        }

        /// <summary>
        /// Gets a required positional argument or returns error code.
        /// </summary>
        public bool TryGetRequiredPositional(int index, string argName, out string value, out int errorCode)
        {
            value = string.Empty;
            errorCode = 0;

            if (index >= _args.Length)
            {
                _outputWriter.WriteError($"Error: Missing required argument: {argName}");
                errorCode = 1;
                return false;
            }

            value = _args[index];
            return true;
        }

        /// <summary>
        /// Gets the value of a named flag (e.g., --from <value>).
        /// </summary>
        public string? GetFlag(string flagName)
        {
            for (int i = 0; i < _args.Length - 1; i++)
            {
                if (_args[i] == flagName)
                {
                    return _args[i + 1];
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a required flag value or returns error code.
        /// </summary>
        public bool TryGetRequiredFlag(string flagName, out string value, out int errorCode)
        {
            value = string.Empty;
            errorCode = 0;

            string? flagValue = GetFlag(flagName);
            if (string.IsNullOrEmpty(flagValue))
            {
                _outputWriter.WriteError($"Error: Missing required flag: {flagName}");
                errorCode = 1;
                return false;
            }

            value = flagValue;
            return true;
        }

        /// <summary>
        /// Gets the first non-flag argument (useful for commands like dist-install).
        /// </summary>
        public string? GetFirstNonFlag()
        {
            for (int i = 0; i < _args.Length; i++)
            {
                string arg = _args[i];
                if (!arg.StartsWith("--"))
                {
                    // Check if this is a flag value (the argument after a flag)
                    if (i > 0 && _args[i - 1].StartsWith("--"))
                    {
                        continue; // This is a flag value, skip it
                    }
                    return arg;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if a boolean flag is present (e.g., --force).
        /// </summary>
        public bool HasFlag(string flagName)
        {
            return _args.Contains(flagName);
        }

        /// <summary>
        /// Validates minimum argument count.
        /// </summary>
        public bool ValidateMinimumArgs(int minimum, string usage, out int errorCode)
        {
            errorCode = 0;

            if (_args.Length < minimum)
            {
                _outputWriter.WriteError($"Error: Insufficient arguments");
                _outputWriter.WriteLine($"Usage: {usage}");
                errorCode = 1;
                return false;
            }

            return true;
        }
    }
}

