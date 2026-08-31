using SentryAppBlazor.Services;
using SentryAppBlazor.Turnstile;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Tests;
public sealed class TurnstileServicesTests
{
    [Fact] public void Polling_controller_starts_and_stops() { var c=new TurnstilePollingController(); Assert.False(c.IsActive); c.Start(); Assert.True(c.IsActive); c.Stop(); Assert.False(c.IsActive); }
    [Fact] public void Polling_controller_is_idempotent_and_only_notifies_changes() { var c=new TurnstilePollingController(); var statuses=new List<bool>(); c.StatusChanged += statuses.Add; Assert.True(c.TryStart()); Assert.False(c.TryStart()); Assert.True(c.TryStop()); Assert.False(c.TryStop()); Assert.Equal([true,false], statuses); }
    [Fact] public async Task Polling_controller_wait_is_cancellable() { var c=new TurnstilePollingController(); using var cancel=new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>c.WaitUntilActiveAsync(cancel.Token)); }
    [Fact]
    public async Task Polling_controller_start_releases_all_waiting_workers()
    {
        var controller = new TurnstilePollingController();
        var poller = controller.WaitUntilActiveAsync(CancellationToken.None);
        var generator = controller.WaitUntilActiveAsync(CancellationToken.None);

        controller.Start();

        await Task.WhenAll(poller, generator).WaitAsync(TimeSpan.FromSeconds(1));
    }
    [Fact] public void Seen_ids_reject_duplicates() { var c=new RecentlySeenIds(10); var id=Guid.NewGuid(); Assert.True(c.Add(id)); Assert.False(c.Add(id)); }
    [Fact] public void Seen_ids_are_bounded_and_expire_oldest() { var c=new RecentlySeenIds(10); var first=Guid.NewGuid(); c.Add(first); for(var i=0;i<10;i++)c.Add(Guid.NewGuid()); Assert.Equal(10,c.Count); Assert.True(c.Add(first)); }
    [Fact] public void Feed_places_new_entry_in_spotlight_immediately() { var s=new TurnstileLogState(); var id=Guid.NewGuid(); Assert.True(s.Add(Entry(id))); Assert.Equal(id,s.Spotlight?.TimeLogId); Assert.Empty(s.Entries); }
    [Fact]
    public void Feed_queues_polled_rows_without_replacing_the_current_spotlight()
    {
        var state = new TurnstileLogState();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.True(state.Add(Entry(first)));
        Assert.True(state.Add(Entry(second)));

        Assert.Equal(first, state.Spotlight?.TimeLogId);
        Assert.Empty(state.Entries);
    }
    [Fact] public void Feed_rejects_duplicate_ids() { var s=new TurnstileLogState(); var item=Entry(Guid.NewGuid()); Assert.True(s.Add(item)); Assert.False(s.Add(item)); }
    [Fact] public void Feed_can_filter_by_device() { var s=new TurnstileLogState(); Assert.Empty(s.Filter("other-device")); }
    [Theory] [InlineData(null,"/img/avatar-placeholder.svg")] [InlineData("","/img/avatar-placeholder.svg")] [InlineData("../secret.jpg","/img/avatar-placeholder.svg")] [InlineData("person.jpg","/photos/person.jpg")]
    public void Photo_urls_are_safe(string? value,string expected)=>Assert.Equal(expected,new PhotoUrlBuilder().Build(value));
    [Theory] [InlineData("IN")] [InlineData("OUT")] [InlineData("BREAK OUT")] public void Generator_only_defines_allowed_log_types(string value)=>Assert.Contains(value,DemoDeviceLogGenerator.LogTypes);
    [Theory]
    [InlineData("Demo", true, true)]
    [InlineData("demo", true, true)]
    [InlineData("Live", true, false)]
    [InlineData("Demo", false, false)]
    public void Generator_requires_demo_mode_and_active_monitoring(string mode, bool active, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.ShouldGenerate(
            new MonitoringOptions { OperatingMode=mode },
            active));
    }
    [Theory]
    [InlineData("Demo", true)]
    [InlineData("demo", true)]
    [InlineData("Live", false)]
    public void Generator_is_enabled_only_in_demo_mode(string mode, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.IsDemoMode(
            new MonitoringOptions { OperatingMode=mode }));
    }
    [Fact]
    public void Generator_uses_the_configured_demo_interval()
    {
        var options = new MonitoringOptions { DemoLogIntervalSeconds = 17 };

        Assert.Equal(TimeSpan.FromSeconds(17), DemoDeviceLogGenerator.GetDelay(options));
    }
    [Fact]
    public void Generator_writes_demo_records_to_device_logs()
    {
        var constructor = Assert.Single(typeof(DemoDeviceLogGenerator).GetConstructors());

        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(DeviceLogWriter));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(TurnstileLogState));
        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(MonitoringSettingsStore));
    }
    [Fact]
    public void Generator_does_not_have_an_automatic_monitoring_start_path()
    {
        Assert.DoesNotContain(
            typeof(DemoDeviceLogGenerator).GetMethods(
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic),
            method => method.Name.Contains("StartMonitoring", StringComparison.Ordinal));
    }
    [Fact]
    public void Poller_reads_persisted_runtime_settings()
    {
        var constructor = Assert.Single(typeof(TurnstileLogPollingWorker).GetConstructors());

        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(MonitoringSettingsStore));
    }
    [Fact]
    public void Demo_writer_uses_a_random_source()
    {
        var method = typeof(DeviceLogWriter).GetMethod(nameof(DeviceLogWriter.InsertDemoAsync));

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(Random));
    }
    [Fact]
    public void Demo_writer_uses_access_control_and_personnels_databases()
    {
        var constructor = Assert.Single(typeof(DeviceLogWriter).GetConstructors());

        Assert.Equal(3, constructor.GetParameters().Length);
        Assert.Equal("factory", constructor.GetParameters()[0].Name);
        Assert.Equal(typeof(IDbContextFactory<PersonnelsDbContext>), constructor.GetParameters()[1].ParameterType);
        Assert.Equal(typeof(ILogger<DeviceLogWriter>), constructor.GetParameters()[2].ParameterType);
    }
    [Fact]
    public void Access_control_factory_reads_the_monitoring_settings_store()
    {
        var constructor = Assert.Single(typeof(MonitoringAccessControlDbContextFactory).GetConstructors());

        Assert.Equal(typeof(MonitoringSettingsStore), Assert.Single(constructor.GetParameters()).ParameterType);
        Assert.Contains(
            typeof(IDbContextFactory<AccessControlDbContext>),
            typeof(MonitoringAccessControlDbContextFactory).GetInterfaces());
    }
    [Theory]
    [InlineData(typeof(MonitoringPersonnelsDbContextFactory), typeof(IDbContextFactory<PersonnelsDbContext>))]
    [InlineData(typeof(MonitoringStaffDbContextFactory), typeof(IDbContextFactory<StaffDbContext>))]
    [InlineData(typeof(MonitoringStudentDbContextFactory), typeof(IDbContextFactory<StudentDbContext>))]
    public void Directory_factories_read_runtime_monitoring_settings(Type factoryType, Type interfaceType)
    {
        Assert.Equal(typeof(MonitoringSettingsStore), Assert.Single(factoryType.GetConstructors()).GetParameters().Single().ParameterType);
        Assert.Contains(interfaceType, factoryType.GetInterfaces());
    }
    [Fact]
    public void Poller_uses_the_separate_personnels_database()
    {
        var constructor = Assert.Single(typeof(TurnstileLogPollingWorker).GetConstructors());

        Assert.Contains(constructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(IDbContextFactory<PersonnelsDbContext>));
    }
    [Fact] public void Sms_result_preserves_failure() { var result=new SmsSendResult(false,"timeout");Assert.False(result.Success);Assert.Equal("timeout",result.Message); }
    [Theory] [InlineData(null,null,"UNKNOWN")] [InlineData(" Ada ",null,"Ada")] [InlineData(null,"Lovelace","Lovelace")] [InlineData("Ada","Lovelace","LOVELACE, Ada")]
    public void Personnel_names_follow_display_rules(string? first,string? last,string expected)=>Assert.Equal(expected,TurnstileLogPollingWorker.FormatPersonnelName(first,last));
    [Fact] public void Polling_configuration_uses_defaults() { var configuration=new ConfigurationBuilder().Build(); var options=TurnstilePollingOptions.FromConfiguration(configuration); Assert.Equal(500,options.IntervalMs);Assert.Equal(3,options.LookbackSecondsOnStart);Assert.Equal(20,options.MaxRowsPerPoll); }
    [Fact] public void Polling_configuration_supports_legacy_keys() { var values=new Dictionary<string,string?>{{"TurnstilePolling:IntervalsMs","750"},{"TurnstilePolling:LookbackSecondsOntart","9"}}; var options=TurnstilePollingOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()); Assert.Equal(750,options.IntervalMs);Assert.Equal(9,options.LookbackSecondsOnStart); }
    [Fact]
    public void Polling_configuration_uses_runtime_monitoring_settings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TurnstilePolling:IntervalMs"] = "500",
            ["TurnstilePolling:LookbackSecondsOnStart"] = "3",
            ["TurnstilePolling:MaxRowsPerPoll"] = "20"
        }).Build();
        var monitoring = new MonitoringOptions { PollingInterval = 1250, LookbackSecondsOnStart = 15, MaxRowsPerPoll = 75 };

        var options = TurnstilePollingOptions.FromConfiguration(configuration, monitoring);

        Assert.Equal(1250, options.IntervalMs);
        Assert.Equal(15, options.LookbackSecondsOnStart);
        Assert.Equal(75, options.MaxRowsPerPoll);
    }
    private static TurnstileLogEntry Entry(Guid id)=>new(id,DateTimeOffset.UtcNow,"IN","1","Person","/p","D","Gate",null,null,null,"sent");
}
