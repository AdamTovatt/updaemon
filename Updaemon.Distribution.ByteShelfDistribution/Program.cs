using Updaemon.Common.Hosting;
using Updaemon.Common.Utilities;

namespace Updaemon.Distribution.ByteShelfDistribution
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            ByteShelfDistributionService service = new ByteShelfDistributionService(
                new VersionParser(),
                new DownloadPostProcessor());

            await DistributionServiceHost.RunAsync(args, service);
        }
    }
}
