using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Components;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;
using SentryAppBlazor.Turnstile;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(MonitoringSettingsStore.ConfigFileName, optional: true, reloadOnChange: true);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Database monitoring is an auxiliary workload. If a hosted worker ever escapes its
// own retry loop, keep the IIS-hosted web application and its diagnostics UI alive.
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
builder.Services.AddOptions<MonitoringOptions>().Bind(builder.Configuration.GetSection(MonitoringOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
// Resolve this connection for every context so Apply in Monitoring Settings is
// immediately honored by both the DeviceLogs poller and demo-data generator.
builder.Services.AddSingleton<IDbContextFactory<AccessControlDbContext>, MonitoringAccessControlDbContextFactory>();
builder.Services.AddSingleton<IDbContextFactory<PersonnelsDbContext>, MonitoringPersonnelsDbContextFactory>();
builder.Services.AddSingleton<IDbContextFactory<StaffDbContext>, MonitoringStaffDbContextFactory>();
builder.Services.AddSingleton<IDbContextFactory<StudentDbContext>, MonitoringStudentDbContextFactory>();
builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton<TurnstilePollingController>(); builder.Services.AddSingleton<TurnstileLogState>();
builder.Services.AddSingleton<PersonnelLookupService>(); builder.Services.AddSingleton<DeviceLogWriter>();
builder.Services.AddSingleton<IPhotoUrlBuilder,PhotoUrlBuilder>(); builder.Services.AddSingleton<ISmsSender,LoggingSmsSender>();
builder.Services.AddHostedService<DemoDatabaseInitializer>();
builder.Services.AddHostedService<TurnstileLogPollingWorker>(); builder.Services.AddHostedService<DemoDeviceLogGenerator>();
builder.Services.AddSingleton<MonitoringSettingsStore>(); builder.Services.AddSingleton<PhotoService>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseAntiforgery();
app.MapGet("/photos/{photoId}", (string photoId, PhotoService photos) => photos.Get(photoId));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(); app.Run();
public partial class Program { }
