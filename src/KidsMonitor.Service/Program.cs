using KidsMonitor.Service;
using Serilog;

const string dataDir = @"C:\ProgramData\KidsMonitor";
Directory.CreateDirectory(Path.Combine(dataDir, "logs"));

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(dataDir, "logs", "service-.log"),
        rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "KidsMonitorService";
});
builder.Services.AddSerilog();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
