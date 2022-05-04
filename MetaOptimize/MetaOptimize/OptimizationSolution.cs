// <copyright file="OptimizationSolution.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System.Collections.Generic;

    /// <summary>
    /// A solution to an optimization problem.
    /// </summary>
    public class OptimizationSolution
    {
        /// <summary>
        /// The total demand met by the optimization.
        /// </summary>
        public Real TotalDemandMet { get; set; }

        /// <summary>
        /// The demands for the problem.
        /// </summary>
        public IDictionary<(string, string), Real> Demands { get; set; }

        /// <summary>
        /// The flow allocation for the problem.
        /// </summary>
        public IDictionary<(string, string), Real> Flows { get; set; }

        /// <summary>
        /// The flow path allocation for the problem.
        /// </summary>
        public IDictionary<string[], Real> FlowsPaths { get; set; }
    }
}
