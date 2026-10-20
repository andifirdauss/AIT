using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIThread.Data.Context;

// Used only by `dotnet ef` so migrations can be generated without booting the web host.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AIThreadDbContext>
{
    public AIThreadDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AIThreadDbContext>()
            .UseSqlServer(DependencyInjection.DefaultConnectionString)
            .Options;
        return new AIThreadDbContext(options);
    }
}
