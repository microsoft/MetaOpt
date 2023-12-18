namespace MetaOptimize
{
    using System.Collections.Generic;

    /// <summary>
    /// Solution to PIFO optimization.
    /// </summary>
    public class PIFOOptimizationSolution : OptimizationSolution
    {
        /// <summary>
        /// cost of allocation.
        /// </summary>
        public double Cost;

        /// <summary>
        /// rank of incomming packets.
        /// </summary>
        public IDictionary<int, double> Ranks;

        /// <summary>
        /// order of dequeued packets.
        /// </summary>
        public IDictionary<int, int> Order;

        /// <summary>
        /// packet admitted or dropped.
        /// </summary>
        public IDictionary<int, int> Admit;
    }
}