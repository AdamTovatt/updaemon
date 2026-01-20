using EasyReasy.EnvironmentVariables;

namespace Updaemon.GithubDistributionService.Tests
{
    [EnvironmentVariableNameContainer]
    public static class PrivateGithubTestEnvironmentVariable
    {
        [EnvironmentVariableName(minLength: 1)]
        public static readonly VariableName Remote = new VariableName("UPDAEMON_PRIVATE_GITHUB_REMOTE");

        [EnvironmentVariableName(minLength: 20)]
        public static readonly VariableName Token = new VariableName("UPDAEMON_PRIVATE_GITHUB_TOKEN");
    }
}

