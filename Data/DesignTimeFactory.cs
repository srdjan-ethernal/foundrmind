using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Foundrmind.Data;

/// <summary>Migrations target PostgreSQL (production). Local SQLite dev databases use EnsureCreated instead.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDb>().UseNpgsql("Host=localhost;Database=foundrmind_design").Options);
}
