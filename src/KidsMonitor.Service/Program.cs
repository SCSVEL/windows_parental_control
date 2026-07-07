using KidsMonitor.Service;
using KidsMonitor.Service.Enforcement;
using KidsMonitor.Service.Ipc;
using KidsMonitor.Service.Security;
using KidsMonitor.Service.Session;
using Serilog;

const string dataDir = @"C:\ProgramData\KidsMonitor";
Directory.CreateDirectory(Path.Combine(dataDir, "logs"));
ProgramDataAcl.Lock(dataDir);

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

var configStore = new ConfigStore(Path.Combine(dataDir, "config.json"));
var limitMinutes = configStore.ReadDailyLimitMinutes() ?? builder.Configuration.GetValue("SessionLimits:DailyLimitMinutes", 120);
// 180s (3 min) rather than a shorter window: a kid watching a video (average length ~5 min)
// may not touch the mouse/keyboard at all for a couple of minutes at a stretch, and that should
// still count as active usage rather than the clock pausing mid-video.
var idleResetSeconds = builder.Configuration.GetValue("SessionLimits:IdleResetSeconds", 180);
var breakIntervalMinutes = configStore.ReadBreakIntervalMinutes();
var breakDurationMinutes = configStore.ReadBreakDurationMinutes();

builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton(new PasswordStore(Path.Combine(dataDir, "password.dat")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new SessionTracker(
    sp.GetRequiredService<TimeProvider>(),
    TimeSpan.FromMinutes(limitMinutes),
    TimeSpan.FromSeconds(idleResetSeconds),
    TimeSpan.FromMinutes(breakIntervalMinutes),
    TimeSpan.FromMinutes(breakDurationMinutes)));

builder.Services.AddSingleton(new EnforcementOptions(ResolveOverlayExecutablePath(builder.Configuration)));
builder.Services.AddSingleton<LockController>();
builder.Services.AddSingleton(new TrayLauncherOptions(ResolveTrayExecutablePath()));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<LockEnforcerService>();
builder.Services.AddHostedService<TrayLauncherService>();

var host = builder.Build();
host.Run();

static string ResolveOverlayExecutablePath(IConfiguration configuration)
{
    // Installed layout is Program Files\KidsMonitor\{Service,Tray,Overlay}\ as sibling folders
    // (see KidsMonitor.Installer) -- kept separate rather than one flat folder because the two
    // self-contained WinUI publishes and the self-contained Worker Service publish would
    // otherwise collide on shared framework DLL names.
    var siblingFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Overlay", "KidsMonitor.Overlay.exe"));
    if (File.Exists(siblingFolder))
    {
        return siblingFolder;
    }

    return configuration.GetValue<string>("Enforcement:OverlayExecutablePath")
        ?? throw new InvalidOperationException(
            "Overlay executable not found next to the Service and no Enforcement:OverlayExecutablePath configured.");
}

static string? ResolveTrayExecutablePath()
{
    var siblingFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Tray", "KidsMonitor.Tray.exe"));
    return File.Exists(siblingFolder) ? siblingFolder : null;
}
