using Microsoft.Extensions.Configuration;

namespace PristonTale.Server.Hosting;

// The period of the server's report lines (Server:ReportSeconds), shared by the world tick, the TCP server and the
// character saves; below 1 second the server refuses to start.
internal static class ReportInterval
{
    public const int DefaultSeconds = 30;

    public static TimeSpan From(IConfiguration configuration)
    {
        var seconds = configuration.GetValue("Server:ReportSeconds", DefaultSeconds);
        if (seconds < 1)
        {
            throw new InvalidOperationException("Server:ReportSeconds must be at least 1.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
