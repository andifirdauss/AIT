using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Implementations;

// Calls each distinct model of the pipeline a few times with the same small piece of code. A model shared by several
// nodes is measured once, so the nodes do not compete with each other for it.
public sealed partial class NodeBenchmarkService(INodeService nodes, INineRouterService router, IOptions<AIThreadOptions> options) : INodeBenchmarkService
{
    private const int Samples = 3;
    private const int MaxReplyTokens = 512;

    private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(60);

    private const string SampleCode = """
        public decimal Total(IEnumerable<OrderLine> lines, decimal taxRate)
        {
            var subtotal = lines.Where(l => !l.Cancelled).Sum(l => l.Price * l.Quantity);
            var discount = subtotal > 1000 ? subtotal * 0.05m : 0;
            return Math.Round((subtotal - discount) * (1 + taxRate), 2);
        }


        """;

    // Long enough that a model has to read something, short enough to cost next to nothing.
    private static readonly string Probe = "In at most 30 words, say what this code does.\n\n" + string.Concat(Enumerable.Repeat(SampleCode, 12));
}
