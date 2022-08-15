// <copyright file="OptimizationSolution.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;

    /// <summary>
    /// A solution to an optimization problem.
    /// </summary>
    public class VBPOptimizationSolution: OptimizationSolution
    {
        /// <summary>
        /// The number of bins used.
        /// </summary>
        public int TotalNumBinsUsed { get; set; }

        /// <summary>
        /// The demands for the problem.
        /// </summary>
        public IDictionary<int, List<double>> Demands { get; set; }

        /// <summary>
        /// The flow allocation for the problem.
        /// </summary>
        public IDictionary<int, List<int>> Placement { get; set; }
    }
}
