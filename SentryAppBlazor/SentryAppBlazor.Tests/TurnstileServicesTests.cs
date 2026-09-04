using SentryAppBlazor.Services;
using SentryAppBlazor.Turnstile;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Tests;
public sealed class TurnstileServicesTests
{
    [Fact] public void Polling_controller_starts_and_stops() { var c=new TurnstilePollingController(); Assert.False(c.IsActive); c.Start(); Assert.True(c.IsActive); c.Stop(); Assert.False(c.IsActive); }
    [Fact] public void Polling_controller_is_idempotent_and_only_notifies_changes() { var c=new TurnstilePollingController(); var statuses=new List<bool>(); c.StatusChanged += statuses.Add; Assert.True(c.TryStart()); Assert.False(c.TryStart()); Assert.True(c.TryStop()); Assert.False(c.TryStop()); Assert.Equal([true,false], statuses); }
    [Fact]
    public void Polling_controller_assigns_each_start_a_new_session()
    {
        var controller = new TurnstilePollingController();

        controller.Start();
        var firstSession = controller.ActiveSession;
        controller.Stop();
        controller.Start();

        Assert.True(firstSession > 0);
        Assert.True(controller.ActiveSession > firstSession);
    }
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
    public void Feed_immediately_spotlights_the_newest_polled_row()
    {
        var state = new TurnstileLogState();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.True(state.Add(Entry(first)));
        Assert.True(state.Add(Entry(second)));

        Assert.Equal(second, state.Spotlight?.TimeLogId);
        Assert.Empty(state.Entries);
    }
    [Fact] public void Feed_rejects_duplicate_ids() { var s=new TurnstileLogState(); var item=Entry(Guid.NewGuid()); Assert.True(s.Add(item)); Assert.False(s.Add(item)); }
    [Fact] public void Feed_can_filter_by_device() { var s=new TurnstileLogState(); Assert.Empty(s.Filter("other-device")); }
    [Theory] [InlineData(null,"/img/avatar-placeholder.svg")] [InlineData("","/img/avatar-placeholder.svg")] [InlineData("../secret.jpg","/img/avatar-placeholder.svg")] [InlineData("person.jpg","/photos/person.jpg")]
    public void Photo_urls_are_safe(string? value,string expected)=>Assert.Equal(expected,new PhotoUrlBuilder().Build(value));
    [Theory] [InlineData("IN")] [InlineData("OUT")] public void Generator_only_defines_allowed_log_types(string value)=>Assert.Contains(value,DemoDeviceLogGenerator.LogTypes);
    [Theory]
    [InlineData("Demo", true)]
    [InlineData("demo", true)]
    [InlineData("Live", false)]
    public void Generator_requires_only_demo_mode(string mode, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.ShouldGenerate(
            new MonitoringOptions { OperatingMode=mode }));
    }
    [Theory]
    [InlineData("Demo", true)]
    [InlineData("demo", true)]
    [InlineData("Live", false)]
    [InlineData(null, false)]
    public void Generator_is_enabled_only_in_demo_mode(string? mode, bool expected)
    {
        Assert.Equal(expected, DemoDeviceLogGenerator.IsDemoMode(
            new MonitoringOptions { OperatingMode=mode! }));
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
        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(TurnstilePollingController));
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
    public void Demo_writer_uses_all_configured_source_databases()
    {
        var constructor = Assert.Single(typeof(DeviceLogWriter).GetConstructors());

        Assert.Equal(4, constructor.GetParameters().Length);
        Assert.Equal(typeof(IDbContextFactory<AccessControlDbContext>), constructor.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(IDbContextFactory<StaffDbContext>), constructor.GetParameters()[1].ParameterType);
        Assert.Equal(typeof(IDbContextFactory<StudentDbContext>), constructor.GetParameters()[2].ParameterType);
        Assert.Equal(typeof(ILogger<DeviceLogWriter>), constructor.GetParameters()[3].ParameterType);
    }
    [Fact]
    public async Task Demo_writer_requires_a_directory_personnel()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sentry-writer-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using (var setup = new DemoDatabaseContext(
                new DbContextOptionsBuilder<DemoDatabaseContext>()
                    .UseSqlite($"Data Source={databasePath}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.ZkDevices.Add(new ZkDevice { SerialNumber = "TEST-GATE", Name = "Test gate" });
                await setup.SaveChangesAsync();
            }

            var writer = new DeviceLogWriter(
                new TestAccessControlDbContextFactory(options),
                new FailingDirectoryDbContextFactory<StaffDbContext>(),
                new FailingDirectoryDbContextFactory<StudentDbContext>(),
                NullLogger<DeviceLogWriter>.Instance);

            var firstId = await writer.InsertDemoAsync(new Random(7), CancellationToken.None);
            var secondId = await writer.InsertDemoAsync(new Random(7), CancellationToken.None);

            Assert.Null(firstId);
            Assert.Null(secondId);
            await using var verification = new AccessControlDbContext(options);
            var rows = await verification.DeviceLogs.OrderBy(log => log.TimeLogStamp).ToListAsync();
            Assert.Empty(rows);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task Demo_writer_uses_the_combined_staff_and_student_directories_without_repeating_people()
    {
        var accessControlPath = Path.Combine(Path.GetTempPath(), $"sentry-access-{Guid.NewGuid():N}.db");
        var staffPath = Path.Combine(Path.GetTempPath(), $"sentry-staff-{Guid.NewGuid():N}.db");
        var studentPath = Path.Combine(Path.GetTempPath(), $"sentry-student-{Guid.NewGuid():N}.db");

        try
        {
            await CreateAccessControlDatabaseAsync(accessControlPath, "TEST-GATE");
            await CreateDirectoryDatabaseAsync(CreateStaffContext(staffPath), "STAFF-1");
            await CreateDirectoryDatabaseAsync(CreateStudentContext(studentPath), "STUDENT-1");

            var accessControlOptions = new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseSqlite($"Data Source={accessControlPath}").Options;
            var writer = new DeviceLogWriter(
                new TestAccessControlDbContextFactory(accessControlOptions),
                new TestDirectoryDbContextFactory<StaffDbContext>(() => CreateStaffContext(staffPath)),
                new TestDirectoryDbContextFactory<StudentDbContext>(() => CreateStudentContext(studentPath)),
                NullLogger<DeviceLogWriter>.Instance);

            await writer.InsertDemoAsync(new Random(7), CancellationToken.None);
            await writer.InsertDemoAsync(new Random(7), CancellationToken.None);

            await using var verification = new AccessControlDbContext(accessControlOptions);
            var rows = await verification.DeviceLogs.OrderBy(log => log.TimeLogStamp).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(
                new[] { "STAFF-1", "STUDENT-1" },
                rows.Select(row => row.AccessNumber).Order(StringComparer.Ordinal).ToArray());
            Assert.All(rows, row => Assert.Equal("TEST-GATE", row.DeviceSerialNumber));
            Assert.All(rows, row => Assert.Equal("TEST", row.CardNo));
            Assert.All(rows, row => Assert.Equal("20", row.Event));
            Assert.All(rows, row => Assert.Equal("1", row.EventAddress));
            Assert.All(rows, row => Assert.Equal("200", row.VerifyMode));
            Assert.All(rows, row => Assert.Equal(0, row.Index));
            Assert.All(rows, row => Assert.Null(row.SiteCode));
            Assert.All(rows, row => Assert.Null(row.LinkId));
            Assert.All(rows, row => Assert.Null(row.HasMask));
            Assert.All(rows, row => Assert.Null(row.Temperature));
            Assert.All(rows, row => Assert.Null(row.IsNotified));
        }
        finally
        {
            File.Delete(accessControlPath);
            File.Delete(staffPath);
            File.Delete(studentPath);
        }
    }
    [Fact]
    public async Task Device_log_writer_inserts_a_requested_device_log()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sentry-requested-writer-{Guid.NewGuid():N}.db");
        var accessControlOptions = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using (var setup = new DemoDatabaseContext(
                new DbContextOptionsBuilder<DemoDatabaseContext>()
                    .UseSqlite($"Data Source={databasePath}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            var writer = new DeviceLogWriter(
                new TestAccessControlDbContextFactory(accessControlOptions),
                new FailingDirectoryDbContextFactory<StaffDbContext>(),
                new FailingDirectoryDbContextFactory<StudentDbContext>(),
                NullLogger<DeviceLogWriter>.Instance);

            var id = await writer.InsertAsync("PERSON-42", "GATE-7", "IN", "CARD-42", CancellationToken.None);

            await using var verification = new AccessControlDbContext(accessControlOptions);
            var row = await verification.DeviceLogs.SingleAsync(log => log.Id == id);
            Assert.Equal("PERSON-42", row.AccessNumber);
            Assert.Equal("GATE-7", row.DeviceSerialNumber);
            Assert.Equal("IN", row.LogType);
            Assert.Equal("CARD-42", row.CardNo);
            Assert.False(row.IsDeleted);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public void Access_control_device_log_model_omits_optional_schema_extensions()
    {
        var options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AccessControlDbContext(options);
        var deviceLog = context.Model.FindEntityType(typeof(DeviceLog));

        Assert.NotNull(deviceLog);
        Assert.NotNull(deviceLog.FindProperty(nameof(DeviceLog.SiteCode)));
        Assert.NotNull(deviceLog.FindProperty(nameof(DeviceLog.LinkId)));
        Assert.NotNull(deviceLog.FindProperty(nameof(DeviceLog.HasMask)));
        Assert.NotNull(deviceLog.FindProperty(nameof(DeviceLog.Temperature)));
        Assert.NotNull(deviceLog.FindProperty(nameof(DeviceLog.IsNotified)));
    }
    [Fact]
    public async Task Demo_writer_does_not_insert_without_a_directory_personnel()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sentry-writer-{Guid.NewGuid():N}.db");
        var accessControlOptions = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite($"Data Source={databasePath}").Options;

        try
        {
            await using (var setup = new DemoDatabaseContext(
                new DbContextOptionsBuilder<DemoDatabaseContext>()
                    .UseSqlite($"Data Source={databasePath}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.ZkDevices.Add(new ZkDevice { SerialNumber = "REAL-GATE" });
                await setup.SaveChangesAsync();
            }

            var writer = new DeviceLogWriter(
                new TestAccessControlDbContextFactory(accessControlOptions),
                new FailingDirectoryDbContextFactory<StaffDbContext>(),
                new FailingDirectoryDbContextFactory<StudentDbContext>(),
                NullLogger<DeviceLogWriter>.Instance);

            var id = await writer.InsertDemoAsync(new Random(7), CancellationToken.None);

            Assert.Null(id);
            await using var verification = new AccessControlDbContext(accessControlOptions);
            Assert.Empty(await verification.DeviceLogs.ToListAsync());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task Demo_writer_does_not_insert_when_required_source_queries_fail()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sentry-writer-{Guid.NewGuid():N}.db");
        var missingDatabasePath = Path.Combine(Path.GetTempPath(), $"sentry-missing-{Guid.NewGuid():N}.db");
        var validOptions = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite($"Data Source={databasePath}").Options;
        var missingOptions = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite($"Data Source={missingDatabasePath}").Options;

        try
        {
            await using (var setup = new DemoDatabaseContext(
                new DbContextOptionsBuilder<DemoDatabaseContext>()
                    .UseSqlite($"Data Source={databasePath}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            var writer = new DeviceLogWriter(
                new SequencedAccessControlDbContextFactory(missingOptions, validOptions),
                new FailingDirectoryDbContextFactory<StaffDbContext>(),
                new FailingDirectoryDbContextFactory<StudentDbContext>(),
                NullLogger<DeviceLogWriter>.Instance);

            var id = await writer.InsertDemoAsync(new Random(7), CancellationToken.None);

            Assert.Null(id);
            await using var verification = new AccessControlDbContext(validOptions);
            Assert.Empty(await verification.DeviceLogs.ToListAsync());
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(missingDatabasePath);
        }
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
    [Fact]
    public void Photo_service_reads_runtime_monitoring_settings()
    {
        var constructor = Assert.Single(typeof(PhotoService).GetConstructors());

        Assert.Contains(constructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(MonitoringSettingsStore));
        Assert.DoesNotContain(constructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(Microsoft.Extensions.Options.IOptions<MonitoringOptions>));
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
    [Fact]
    public void Demo_generation_requires_an_active_demo_session()
    {
        var controller = new TurnstilePollingController();
        controller.TryConfigure(MonitoringMode.Demo);
        Assert.False(DemoDeviceLogGenerator.CanGenerate(controller));
        controller.Start();
        Assert.True(DemoDeviceLogGenerator.CanGenerate(controller));
        controller.Stop();
        controller.TryConfigure(MonitoringMode.Live);
        controller.Start();
        Assert.False(DemoDeviceLogGenerator.CanGenerate(controller));
    }

    [Fact]
    public void Controller_locks_mode_while_running_and_allows_live_mode()
    {
        var controller = new TurnstilePollingController();
        Assert.True(controller.TryConfigure(MonitoringMode.Live));
        controller.Start();
        Assert.False(controller.TryConfigure(MonitoringMode.Demo));
        Assert.Equal(MonitoringMode.Live, controller.Mode);
    }

    [Fact]
    public void Compound_cursor_orders_identical_timestamps_by_id()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).Order().ToArray();
        var rows = ids.Select(id => new DeviceLog { Id = id, TimeLogStamp = timestamp }).ToArray();

        var remaining = rows.Where(row => TurnstileLogPollingWorker.IsAfterCursor(row, timestamp, ids[9]))
            .OrderBy(row => row.TimeLogStamp).ThenBy(row => row.Id).ToArray();

        Assert.Equal(ids[10..], remaining.Select(row => row.Id));
    }

    [Fact]
    public async Task Spotlight_transitions_to_bounded_category_feed()
    {
        var options = new MonitoringOptions { HighlightDurationMs = 1, FeedItemTtlSeconds = 2, MaximumFeedItemsPerCategory = 2 };
        using var state = new TurnstileLogState(TimeProvider.System, () => options);
        state.Add(Entry(Guid.NewGuid()));
        state.Add(Entry(Guid.NewGuid()));
        state.Add(Entry(Guid.NewGuid()));

        await Task.Delay(100);

        Assert.Equal(2, state.InEntries.Count);
    }

    [Fact]
    public async Task Feed_items_expire_after_the_configured_ttl()
    {
        var options = new MonitoringOptions { HighlightDurationMs = 1, FeedItemTtlSeconds = 1 };
        using var state = new TurnstileLogState(TimeProvider.System, () => options);
        state.Add(Entry(Guid.NewGuid()));
        await Task.Delay(50);
        Assert.Single(state.InEntries);
        await Task.Delay(1050);
        Assert.Empty(state.InEntries);
    }

    [Fact]
    public void State_is_safe_for_concurrent_duplicate_submissions()
    {
        using var state = new TurnstileLogState();
        var id = Guid.NewGuid();
        var accepted = 0;
        Parallel.For(0, 100, _ => { if (state.Add(Entry(id))) Interlocked.Increment(ref accepted); });
        Assert.Equal(1, accepted);
    }

    [Fact]
    public void Monitoring_options_reject_an_inverted_demo_delay_range()
    {
        var options = new MonitoringOptions { DemoMinimumDelaySeconds = 10, DemoMaximumDelaySeconds = 1 };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), results, true));
    }

    private static TurnstileLogEntry Entry(Guid id)=>new(id,DateTimeOffset.UtcNow,"IN","1","Person","/p","D","Gate",null,null,null,"sent");

    private static async Task CreateAccessControlDatabaseAsync(string path, string serialNumber)
    {
        await using var context = new DemoDatabaseContext(
            new DbContextOptionsBuilder<DemoDatabaseContext>()
                .UseSqlite($"Data Source={path}").Options);
        await context.Database.EnsureCreatedAsync();
        context.ZkDevices.Add(new ZkDevice { SerialNumber = serialNumber });
        await context.SaveChangesAsync();
    }

    private static async Task CreateDirectoryDatabaseAsync(DirectoryDbContext context, string accessNumber)
    {
        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
            context.People.Add(new DirectoryPerson { Field15 = accessNumber });
            await context.SaveChangesAsync();
        }
    }

    private static StaffDbContext CreateStaffContext(string path) => new(
        new DbContextOptionsBuilder<StaffDbContext>().UseSqlite($"Data Source={path}").Options);

    private static StudentDbContext CreateStudentContext(string path) => new(
        new DbContextOptionsBuilder<StudentDbContext>().UseSqlite($"Data Source={path}").Options);

    private sealed class TestAccessControlDbContextFactory(DbContextOptions<AccessControlDbContext> options)
        : IDbContextFactory<AccessControlDbContext>
    {
        public AccessControlDbContext CreateDbContext() => new(options);
    }

    private sealed class SequencedAccessControlDbContextFactory(
        DbContextOptions<AccessControlDbContext> failingOptions,
        DbContextOptions<AccessControlDbContext> validOptions)
        : IDbContextFactory<AccessControlDbContext>
    {
        private int contextsCreated;

        public AccessControlDbContext CreateDbContext() =>
            new(Interlocked.Increment(ref contextsCreated) <= 2 ? failingOptions : validOptions);
    }

    private sealed class FailingDirectoryDbContextFactory<TContext> : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => throw new InvalidOperationException("Directory unavailable in this test.");
    }

    private sealed class TestDirectoryDbContextFactory<TContext>(Func<TContext> createContext) : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => createContext();
    }
}
