using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Tests;

public sealed class DataProtectionServiceCollectionExtensionsTests
{
    [Fact]
    public void Unwritable_key_ring_does_not_prevent_application_startup()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"sentry-data-protection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        var filePath = Path.Combine(contentRoot, "not-a-directory");
        File.WriteAllText(filePath, "occupied");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeysPath"] = filePath
            })
            .Build();

        try
        {
            using var services = CreateServices(configuration, contentRoot);
            var protector = services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("startup-test");

            var protectedValue = protector.Protect("expected-value");

            Assert.Equal("expected-value", protector.Unprotect(protectedValue));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void Separate_service_providers_can_read_tokens_from_the_persisted_key_ring()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"sentry-data-protection-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        try
        {
            using var firstServices = CreateServices(configuration, contentRoot);
            var firstProtector = firstServices.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("antiforgery-test");
            var protectedValue = firstProtector.Protect("expected-value");

            using var restartedServices = CreateServices(configuration, contentRoot);
            var restartedProtector = restartedServices.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("antiforgery-test");

            Assert.Equal("expected-value", restartedProtector.Unprotect(protectedValue));
            Assert.NotEmpty(Directory.EnumerateFiles(
                Path.Combine(contentRoot, "data-protection-keys"),
                "*.xml"));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static ServiceProvider CreateServices(IConfiguration configuration, string contentRoot)
    {
        var services = new ServiceCollection();
        services.AddPersistentDataProtection(configuration, contentRoot);
        return services.BuildServiceProvider();
    }
}
