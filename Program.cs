using mydebug_mcp;
/// <summary>
///  npx @modelcontextprotocol/inspector to test
/// </summary>
internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Always listen on 6971 on all interfaces (required for Docker port publishing),
        // regardless of launchSettings / ASPNETCORE_URLS
        builder.WebHost.UseUrls("http://0.0.0.0:6971");

        builder.Services.AddSingleton<VisualStudioService>();
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<VisualStudioTools>();

        var app = builder.Build();

        // Streamable HTTP endpoint at /mcp
        app.MapMcp("/mcp");

        app.Run();
    }
}


