using Updaemon.Common.Hosting;
using Updaemon.Common.Utilities;

namespace Updaemon.Distribution.ByteShelfDistribution
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using (ByteShelfHttpConnector connector = new ByteShelfHttpConnector())
            {
                ByteShelfDistributionService service = new ByteShelfDistributionService(
                    new VersionParser(),
                    new DownloadPostProcessor(),
                    connector.Connect);

                await DistributionServiceHost.RunAsync(args, service);
            }
        }
    }
}
