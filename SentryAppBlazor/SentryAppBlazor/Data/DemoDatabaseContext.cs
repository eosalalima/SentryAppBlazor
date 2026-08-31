using Microsoft.EntityFrameworkCore;

namespace SentryAppBlazor.Data;

/// <summary>A complete, local schema used by the zero-configuration demo.</summary>
public sealed class DemoDatabaseContext(DbContextOptions<DemoDatabaseContext> options) : DbContext(options)
{
    public DbSet<DeviceLog> DeviceLogs => Set<DeviceLog>();
    public DbSet<ZkDevice> ZkDevices => Set<ZkDevice>();
    public DbSet<Personnel> Personnels => Set<Personnel>();
    public DbSet<DirectoryPerson> DirectoryPeople => Set<DirectoryPerson>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DeviceLog>().ToTable("DeviceLogs");
        builder.Entity<DeviceLog>().Property(x => x.DateCreated).HasConversion<long>();
        builder.Entity<DeviceLog>().Property(x => x.TimeLogStamp).HasConversion<long>();
        builder.Entity<ZkDevice>().ToTable("ZKDevices").HasKey(x => x.SerialNumber);
        builder.Entity<Personnel>().ToTable("Personnels").HasKey(x => x.AccessNumber);
        builder.Entity<DirectoryPerson>().ToTable("MyDataTable").HasKey(x => x.Field15);
    }
}
