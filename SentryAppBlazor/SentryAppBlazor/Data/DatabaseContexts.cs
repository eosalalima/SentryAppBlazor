using Microsoft.EntityFrameworkCore;

namespace SentryAppBlazor.Data;

public sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    public DbSet<DeviceLog> DeviceLogs => Set<DeviceLog>();
    public DbSet<Personnel> Personnels => Set<Personnel>();
    public DbSet<ZkDevice> ZkDevices => Set<ZkDevice>();
    public DbSet<TurnstileLogRow> TurnstileLogRows => Set<TurnstileLogRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DeviceLog>().ToTable("DeviceLogs", "dbo");
        b.Entity<Personnel>().ToTable("Personnels", "dbo").HasKey(x => x.AccessNumber);
        b.Entity<ZkDevice>().ToTable("ZKDevices", "dbo").HasKey(x => x.SerialNumber);
        b.Entity<TurnstileLogRow>().HasNoKey();
    }
}
public abstract class DirectoryDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<DirectoryPerson> People => Set<DirectoryPerson>();
    protected override void OnModelCreating(ModelBuilder b) => b.Entity<DirectoryPerson>().ToTable("MyDataTable", "dbo").HasKey(x => x.Field15);
}
public sealed class StaffDbContext(DbContextOptions<StaffDbContext> options) : DirectoryDbContext(options);
public sealed class StudentDbContext(DbContextOptions<StudentDbContext> options) : DirectoryDbContext(options);
