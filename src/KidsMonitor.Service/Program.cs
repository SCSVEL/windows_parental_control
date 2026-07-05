using KidsMonitor.Service;
using KidsMonitor.Service.Ipc;
using KidsMonitor.Service.Session;
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

var limitMinutes = builder.Configuration.GetValue("SessionLimits:DailyLimitMinutes", 120);
var idleResetSeconds = builder.Configuration.GetValue("SessionLimits:IdleResetSeconds", 60);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new SessionTracker(
    sp.GetRequiredService<TimeProvider>(),
    TimeSpan.FromMinutes(limitMinutes),
    TimeSpan.FromSeconds(idleResetSeconds)));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
host.Run();
