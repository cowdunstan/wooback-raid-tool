using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WoobackVash.Api.Data;

/// <summary>
/// Used only by the EF Core CLI (<c>dotnet ef migrations …</c>). The app registers
/// its DbContext conditionally (only when a connection string is present), so at
/// design time there may be none — this factory supplies a Postgres context with a
/// placeholder connection so migrations can be scaffolded without a live database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("DATABASE_CONNECTION")
                   ?? "Host=localhost;Port=5432;Database=wooback;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new AppDbContext(options);
    }
}
