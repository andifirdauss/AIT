using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AiThread.Data.Context;

// Used only by `dotnet ef` so migrations can be generated without booting the web host.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AiThreadDbContext>
{
    public AiThreadDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AiThreadDbContext>()
            .UseSqlServer(DependencyInjection.DefaultConnectionString)
            .Options;
        return new AiThreadDbContext(options);
    }
}
