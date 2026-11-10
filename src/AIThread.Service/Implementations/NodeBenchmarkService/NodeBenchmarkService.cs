using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Implementations;

// Calls each distinct model of the pipeline a few times with a context as large as its nodes really read. Nodes that
// share a model and a context size are measured once, so they do not compete with each other for it.
public sealed partial class NodeBenchmarkService(INodeService nodes, INineRouterService router, IOptions<AIThreadOptions> options) : INodeBenchmarkService
{
    private const int Samples = 3;
    private const int MaxReplyTokens = 512;
    private const int CharsPerToken = 4;

    // A planner-sized context takes a while on a strong model.
    private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(120);

    private const string SampleCode = """
        public decimal Total(IEnumerable<OrderLine> lines, decimal taxRate)
        {
            var subtotal = lines.Where(l => !l.Cancelled).Sum(l => l.Price * l.Quantity);
            var discount = subtotal > 1000 ? subtotal * 0.05m : 0;
            return Math.Round((subtotal - discount) * (1 + taxRate), 2);
        }


        """;
}
