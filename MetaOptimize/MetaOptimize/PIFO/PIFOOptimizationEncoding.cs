namespace MetaOptimize
{
    using System.Collections.Generic;

    /// <summary>
    /// The encoding of PIFO optimization.
    /// </summary>
    public class PIFOOptimizationEncoding<TVar, TSolution> : OptimizationEncoding<TVar, TSolution>
    {
        /// <summary>
        /// Packet ranks.
        /// </summary>
        public IDictionary<int, TVar> RankVariables { get; set; }
    }
}