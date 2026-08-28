using SentryAppBlazor.Services;
using SentryAppBlazor.Turnstile;

namespace SentryAppBlazor.Tests;
public sealed class TurnstileServicesTests
{
    [Fact] public void Polling_controller_starts_and_stops() { var c=new TurnstilePollingController(); Assert.False(c.IsActive); c.Start(); Assert.True(c.IsActive); c.Stop(); Assert.False(c.IsActive); }
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
    [Fact]
    public void Generator_writes_demo_events_to_device_logs_for_polling()
    {
        var constructor = Assert.Single(typeof(DemoDeviceLogGenerator).GetConstructors());

        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(DeviceLogWriter));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(TurnstileLogState));
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
    private static TurnstileLogEntry Entry(Guid id)=>new(id,DateTimeOffset.UtcNow,"IN","1","Person","/p","D","Gate",null,null,null,"sent");
}
