using Microsoft.Extensions.Options;
using SentryAppBlazor.Components;
using SentryAppBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddOptions<MonitoringOptions>()
    .Bind(builder.Configuration.GetSection(MonitoringOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<MonitoringState>();
builder.Services.AddSingleton<PhotoService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitoringState>());

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/photos/{photoId}", (string photoId, PhotoService photos) => photos.Get(photoId))
    .WithName("PersonnelPhoto");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
