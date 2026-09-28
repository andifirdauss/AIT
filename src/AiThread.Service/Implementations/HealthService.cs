using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public class HealthService : IHealthService
{
    public string GetStatus() => "Healthy";
}
