using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using SentryAppBlazor.Components;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;
using SentryAppBlazor.Turnstile;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(MonitoringSettingsStore.ConfigFileName, optional: true, reloadOnChange: true);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddOptions<MonitoringOptions>().Bind(builder.Configuration.GetSection(MonitoringOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SimulationOptions>().BindConfiguration("Simulation").ValidateDataAnnotations().Validate(x => x.MaximumDelaySeconds >= x.MinimumDelaySeconds, "Maximum delay must be at least minimum delay.").ValidateOnStart();
builder.Services.AddDbContextFactory<AccessControlDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("AccessControlDb")));
builder.Services.AddDbContextFactory<StaffDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("StaffDb")));
builder.Services.AddDbContextFactory<StudentDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("StudentDb")));
builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton<TurnstilePollingController>(); builder.Services.AddSingleton<TurnstileLogState>();
builder.Services.AddSingleton<PersonnelLookupService>(); builder.Services.AddSingleton<DeviceLogWriter>();
builder.Services.AddSingleton<IPhotoUrlBuilder,PhotoUrlBuilder>(); builder.Services.AddSingleton<ISmsSender,LoggingSmsSender>();
builder.Services.AddHostedService<TurnstileLogPollingWorker>(); builder.Services.AddHostedService<DemoDeviceLogGenerator>();
builder.Services.AddSingleton<MonitoringSettingsStore>(); builder.Services.AddSingleton<PhotoService>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseAntiforgery();
app.MapGet("/photos/{photoId}", (string photoId, PhotoService photos) => photos.Get(photoId));
app.MapPost("/admin/test-logs", async (HttpRequest http, ManualLogRequest request, DeviceLogWriter writer, IOptions<SimulationOptions> options, CancellationToken token) =>
{
    var configured=options.Value.AdministrationKey; var supplied=http.Headers["X-Test-Log-Key"].ToString();
    if(options.Value.IsLiveMode || !options.Value.EnableManualTestLogs) return Results.NotFound();
    if(string.IsNullOrEmpty(configured) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured),Encoding.UTF8.GetBytes(supplied))) return Results.Unauthorized();
    return Results.Ok(new { Id=await writer.InsertAsync(request.AccessNumber,request.DeviceSerialNumber,request.LogType,"MANUAL-TEST",token) });
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(); app.Run();
public sealed record ManualLogRequest(string AccessNumber,string DeviceSerialNumber,string LogType);
public partial class Program { }
