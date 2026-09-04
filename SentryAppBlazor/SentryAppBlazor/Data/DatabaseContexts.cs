using Microsoft.EntityFrameworkCore;

namespace SentryAppBlazor.Data;

public sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    public DbSet<DeviceLog> DeviceLogs => Set<DeviceLog>();
    public DbSet<ZkDevice> ZkDevices => Set<ZkDevice>();
    public DbSet<TurnstileLogRow> TurnstileLogRows => Set<TurnstileLogRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        var deviceLog = b.Entity<DeviceLog>();
        deviceLog.ToTable("DeviceLogs", "dbo");

        if (Database.IsSqlite())
        {
            deviceLog.Property(x => x.DateCreated).HasConversion<long>();
            deviceLog.Property(x => x.TimeLogStamp).HasConversion<long>();
        }
        b.Entity<ZkDevice>().ToTable("ZKDevices", "dbo").HasKey(x => x.SerialNumber);
        b.Entity<Personnel>().ToTable("Personnels", "dbo").HasKey(x => x.AccessNumber);
        b.Entity<TurnstileLogRow>().HasNoKey();
    }
}
public sealed class PersonnelsDbContext(DbContextOptions<PersonnelsDbContext> options) : DbContext(options)
{
    public DbSet<Personnel> Personnels => Set<Personnel>();
    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<Personnel>().ToTable("Personnels", "dbo").HasKey(x => x.AccessNumber);
}
public abstract class DirectoryDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<DirectoryPerson> People => Set<DirectoryPerson>();
    protected override void OnModelCreating(ModelBuilder b) => b.Entity<DirectoryPerson>().ToTable("MyDataTable", "dbo").HasKey(x => x.Field15);
}
public sealed class StaffDbContext(DbContextOptions<StaffDbContext> options) : DirectoryDbContext(options);
public sealed class StudentDbContext(DbContextOptions<StudentDbContext> options) : DirectoryDbContext(options);
