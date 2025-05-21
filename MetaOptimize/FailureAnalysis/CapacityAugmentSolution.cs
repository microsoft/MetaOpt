namespace MetaOptimize
{
    using System.Collections.Generic;
    /// <summary>
    /// This is the set of links that we augment.
    /// </summary>
    public class CapacityAugmentSolution : OptimizationSolution
    {
        /// <summary>
        /// The status of the links (whether we augment or not).
        /// </summary>
        public IDictionary<(string, string), double> LagStatus { get; set; }
    }
}