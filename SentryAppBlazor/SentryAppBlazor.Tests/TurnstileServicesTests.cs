using SentryAppBlazor.Services;
using SentryAppBlazor.Turnstile;
using Microsoft.Extensions.Configuration;

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
    [Fact] public void Feed_rejects_duplicate_ids() { var s=new TurnstileLogState(); var item=Entry(Guid.NewGuid()); Assert.True(s.Add(item)); Assert.False(s.Add(item)); }
    [Fact] public void Feed_can_filter_by_device() { var s=new TurnstileLogState(); Assert.Empty(s.Filter("other-device")); }
    [Theory] [InlineData(null,"/img/avatar-placeholder.svg")] [InlineData("","/img/avatar-placeholder.svg")] [InlineData("../secret.jpg","/img/avatar-placeholder.svg")] [InlineData("person.jpg","/photos/person.jpg")]
    public void Photo_urls_are_safe(string? value,string expected)=>Assert.Equal(expected,new PhotoUrlBuilder().Build(value));
    [Theory] [InlineData("IN")] [InlineData("OUT")] [InlineData("BREAK OUT")] public void Generator_only_defines_allowed_log_types(string value)=>Assert.Contains(value,DemoDeviceLogGenerator.LogTypes);
    [Fact] public void Production_defaults_disable_simulation() { var o=new SimulationOptions(); Assert.True(o.IsLiveMode);Assert.False(o.EnableSimulatedLogs);Assert.False(o.EnableManualTestLogs); }
    [Theory]
    [InlineData(false, "Demo", true, true, true)]
    [InlineData(false, "Demo", false, true, false)]
    [InlineData(true, "Demo", true, true, false)]
    [InlineData(false, "Live", true, true, false)]
    [InlineData(false, "Demo", true, false, false)]
    public void Generator_requires_all_demo_safety_settings_and_active_monitoring(bool live, string mode, bool enabled, bool active, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.ShouldGenerate(
            new SimulationOptions { IsLiveMode=live },
            new MonitoringOptions { OperatingMode=mode, EnableSimulatedLogs=enabled },
            active));
    }
    [Theory]
    [InlineData(false, "Demo", true, true)]
    [InlineData(true, "Demo", true, false)]
    [InlineData(false, "Live", true, false)]
    [InlineData(false, "Demo", false, false)]
    public void Generator_auto_starts_only_for_safely_enabled_demo_mode(bool live, string mode, bool enabled, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.IsDemoEnabled(
            new SimulationOptions { IsLiveMode=live },
            new MonitoringOptions { OperatingMode=mode, EnableSimulatedLogs=enabled }));
    }
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void Generator_starts_monitoring_only_when_demo_becomes_enabled(
        bool wasEnabled, bool isEnabled, bool expected) =>
        Assert.Equal(expected, DemoDeviceLogGenerator.ShouldStartMonitoring(wasEnabled, isEnabled));
    [Fact]
    public void Generator_uses_the_configured_demo_delay_range()
    {
        var options = new SimulationOptions { MinimumDelaySeconds = 17, MaximumDelaySeconds = 17 };

        Assert.Equal(TimeSpan.FromSeconds(17), DemoDeviceLogGenerator.GetDelay(options, new Random(1)));
    }
    [Fact]
    public void Generator_writes_demo_events_to_device_logs_for_polling()
    {
        var constructor = Assert.Single(typeof(DemoDeviceLogGenerator).GetConstructors());

        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(DeviceLogWriter));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(TurnstileLogState));
    }
    [Fact]
    public void Demo_writer_uses_a_random_source()
    {
        var method = typeof(DeviceLogWriter).GetMethod(nameof(DeviceLogWriter.InsertDemoAsync));

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(Random));
    }
    [Fact]
    public void Demo_entry_contains_displayable_person_and_device_data()
    {
        var entry = DemoDeviceLogGenerator.CreateEntry(new Random(1), DateTimeOffset.UnixEpoch);

        Assert.Equal(DateTimeOffset.UnixEpoch, entry.TimeLogStamp);
        Assert.False(string.IsNullOrWhiteSpace(entry.AccessNumber));
        Assert.False(string.IsNullOrWhiteSpace(entry.PersonnelName));
        Assert.StartsWith("DEMO-GATE-", entry.DeviceSerialNumber);
        Assert.Contains(entry.LogType, DemoDeviceLogGenerator.LogTypes);
    }
    [Fact] public void Sms_result_preserves_failure() { var result=new SmsSendResult(false,"timeout");Assert.False(result.Success);Assert.Equal("timeout",result.Message); }
    [Theory] [InlineData(null,null,"UNKNOWN")] [InlineData(" Ada ",null,"Ada")] [InlineData(null,"Lovelace","Lovelace")] [InlineData("Ada","Lovelace","LOVELACE, Ada")]
    public void Personnel_names_follow_display_rules(string? first,string? last,string expected)=>Assert.Equal(expected,TurnstileLogPollingWorker.FormatPersonnelName(first,last));
    [Fact] public void Polling_configuration_uses_defaults() { var configuration=new ConfigurationBuilder().Build(); var options=TurnstilePollingOptions.FromConfiguration(configuration); Assert.Equal(500,options.IntervalMs);Assert.Equal(3,options.LookbackSecondsOnStart);Assert.Equal(20,options.MaxRowsPerPoll); }
    [Fact] public void Polling_configuration_supports_legacy_keys() { var values=new Dictionary<string,string?>{{"TurnstilePolling:IntervalsMs","750"},{"TurnstilePolling:LookbackSecondsOntart","9"}}; var options=TurnstilePollingOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()); Assert.Equal(750,options.IntervalMs);Assert.Equal(9,options.LookbackSecondsOnStart); }
    private static TurnstileLogEntry Entry(Guid id)=>new(id,DateTimeOffset.UtcNow,"IN","1","Person","/p","D","Gate",null,null,null,"sent");
}
