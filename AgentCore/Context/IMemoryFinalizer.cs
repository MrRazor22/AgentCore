using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Context
{
    /// <summary>
    /// Capability interface for context layers that need to run processing or side-effects at the end of a turn.
    /// </summary>
    public interface IMemoryFinalizer
    {
        /// <summary>
        /// Finalizes the execution turn, allowing memory consolidation, pruning, or fact extraction to run.
        /// </summary>
        Task FinalizeTurnAsync(CancellationToken ct = default);
    }
}
