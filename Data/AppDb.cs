using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ModuleRun> ModuleRuns => Set<ModuleRun>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<PublishedPage> Pages => Set<PublishedPage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<PublishedPage>().HasIndex(p => p.Slug).IsUnique();
        b.Entity<ModuleRun>().HasIndex(r => new { r.ProjectId, r.ModuleKey, r.CreatedAt });
        b.Entity<Lead>().Property(l => l.Value).HasPrecision(12, 2);
    }
}
