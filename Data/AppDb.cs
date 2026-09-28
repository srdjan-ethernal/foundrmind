using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ModuleRun> ModuleRuns => Set<ModuleRun>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<PublishedPage> Pages => Set<PublishedPage>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<ScheduledPost> Posts => Set<ScheduledPost>();
    public DbSet<AiUsage> AiUsage => Set<AiUsage>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<CustomDomain> CustomDomains => Set<CustomDomain>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<User>().HasIndex(u => u.StripeCustomerId);
        b.Entity<PublishedPage>().HasIndex(p => p.Slug).IsUnique();
        b.Entity<ModuleRun>().HasIndex(r => new { r.ProjectId, r.ModuleKey, r.CreatedAt });
        b.Entity<Lead>().Property(l => l.Value).HasPrecision(12, 2);
        b.Entity<SocialAccount>().HasIndex(a => new { a.UserId, a.Provider }).IsUnique();
        b.Entity<ScheduledPost>().HasIndex(p => new { p.Status, p.ScheduledAt });
        b.Entity<AiUsage>().HasIndex(u => u.CreatedAt);
        b.Entity<TeamMember>().HasIndex(t => new { t.OwnerUserId, t.Email }).IsUnique();
        b.Entity<TeamMember>().HasIndex(t => t.MemberUserId);
        b.Entity<CustomDomain>().HasIndex(d => d.Host).IsUnique();
        b.Entity<AiUsage>().Property(u => u.CostUsd).HasPrecision(12, 6);
        b.Entity<ModuleRun>().Property(r => r.CostUsd).HasPrecision(12, 6);
    }
}
