using SentryAppBlazor.Turnstile;

namespace SentryAppBlazor.Tests;
public sealed class TurnstileServicesTests
{
    [Fact] public void Polling_controller_starts_and_stops() { var c=new TurnstilePollingController(); Assert.False(c.IsActive); c.Start(); Assert.True(c.IsActive); c.Stop(); Assert.False(c.IsActive); }
    [Fact] public async Task Polling_controller_wait_is_cancellable() { var c=new TurnstilePollingController(); using var cancel=new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>c.WaitUntilActiveAsync(cancel.Token)); }
    [Fact] public void Seen_ids_reject_duplicates() { var c=new RecentlySeenIds(10); var id=Guid.NewGuid(); Assert.True(c.Add(id)); Assert.False(c.Add(id)); }
    [Fact] public void Seen_ids_are_bounded_and_expire_oldest() { var c=new RecentlySeenIds(10); var first=Guid.NewGuid(); c.Add(first); for(var i=0;i<10;i++)c.Add(Guid.NewGuid()); Assert.Equal(10,c.Count); Assert.True(c.Add(first)); }
    [Fact] public void Feed_is_bounded() { var s=new TurnstileLogState(); for(var i=0;i<220;i++)s.Add(Entry(Guid.NewGuid())); Assert.Equal(200,s.Entries.Count); }
    [Fact] public void Feed_is_newest_first() { var s=new TurnstileLogState(); var a=Guid.NewGuid(); var b=Guid.NewGuid(); s.Add(Entry(a));s.Add(Entry(b));Assert.Equal(new[]{b,a},s.Entries.Select(x=>x.TimeLogId)); }
    [Fact] public void Feed_prunes_entries_older_than_monitoring_retention() { var s=new TurnstileLogState(); s.Add(Entry(Guid.NewGuid()) with { TimeLogStamp=DateTimeOffset.UtcNow.AddMinutes(-1) }); s.Add(Entry(Guid.NewGuid())); s.Prune(DateTimeOffset.UtcNow.AddSeconds(-10)); Assert.Single(s.Entries); }
    [Theory] [InlineData(null,"/img/avatar-placeholder.svg")] [InlineData("","/img/avatar-placeholder.svg")] [InlineData("../secret.jpg","/img/avatar-placeholder.svg")] [InlineData("person.jpg","/photos/person.jpg")]
    public void Photo_urls_are_safe(string? value,string expected)=>Assert.Equal(expected,new PhotoUrlBuilder().Build(value));
    [Theory] [InlineData("IN")] [InlineData("OUT")] [InlineData("BREAK OUT")] public void Generator_only_defines_allowed_log_types(string value)=>Assert.Contains(value,DemoDeviceLogGenerator.LogTypes);
    [Fact] public void Production_defaults_disable_simulation() { var o=new SimulationOptions(); Assert.True(o.IsLiveMode);Assert.False(o.EnableSimulatedLogs);Assert.False(o.EnableManualTestLogs); }
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, false)]
    public void Generator_requires_demo_mode_setting_and_active_monitoring(bool live, bool enabled, bool active, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.ShouldGenerate(new SimulationOptions { IsLiveMode=live, EnableSimulatedLogs=enabled }, active));
    }
    [Fact] public void Sms_result_preserves_failure() { var result=new SmsSendResult(false,"timeout");Assert.False(result.Success);Assert.Equal("timeout",result.Message); }
    private static TurnstileLogEntry Entry(Guid id)=>new(id,DateTimeOffset.UtcNow,"IN","1","Person","/p","D","Gate",null,null,null,"sent");
}
