using InvictiScanner.Configuration;
using InvictiScanner.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
    .AddEnvironmentVariables(prefix: "SCB_");

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Services.Configure<AppSettings>(builder.Configuration);

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);
builder.Services.AddSingleton<ScanResultWriter>();
builder.Services.AddSingleton<ScanOrchestrator>();

builder.Services
    .AddHttpClient<InvictiApiClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value.Invicti;
        InvictiApiClient.ConfigureHttpClient(client, settings);
    })
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value.Invicti;
        return InvictiApiClient.BuildHandler(settings);
    });

using var host = builder.Build();
var orchestrator = host.Services.GetRequiredService<ScanOrchestrator>();

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellationSource.Cancel();
};

var exitCode = await orchestrator.RunAsync(args, cancellationSource.Token);
return exitCode;
