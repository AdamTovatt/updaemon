using Updaemon.Common.Hosting;
using Updaemon.Common.Utilities;
using Updaemon.GithubDistributionService.Services;

namespace Updaemon.GithubDistributionService
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            GithubDistributionService service = new GithubDistributionService(
                new GithubApiClient(),
                new VersionParser(),
                new DownloadPostProcessor());

            await DistributionServiceHost.RunAsync(args, service);
        }
    }
}
