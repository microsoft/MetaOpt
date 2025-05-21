namespace MetaOptimize
{
    using System.Collections.Generic;
    /// <summary>
    /// interface for Capacity Plan optimization solution.
    /// </summary>
    public class FailureAnalysisOptimizationSolution : OptimizationSolution
    {
        /// <summary>
        /// The demands for the problem.
        /// </summary>
        public IDictionary<(string, string), double> Demands { get; set; }
        /// <summary>
        /// The up/down status for the links.
        /// </summary>
        public IDictionary<Edge, double> LagStatus { get; set; }

        /// <summary>
        /// The flow allocation for the problem.
        /// </summary>
        public IDictionary<(string, string), double> Flows { get; set; }

        /// <summary>
        /// The flow path allocation for the problem.
        /// </summary>
        public IDictionary<string[], double> FlowsPaths { get; set; }

        /// <summary>
        /// The total flow on each link.
        /// </summary>
        public IDictionary<Edge, double> LagFlows { get; set; }

        /// <summary>
        /// The objective by the optimization.
        /// </summary>
        public double MaxObjective { get; set; }
    }
}