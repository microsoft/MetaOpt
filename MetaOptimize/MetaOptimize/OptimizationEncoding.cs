// <copyright file="OptimizationEncoding.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;

    /// <summary>
    /// The encoding of an optimization.
    /// </summary>
    public class OptimizationEncoding<TVar, TSolution>
    {
        /// <summary>
        /// The maximization objective.
        /// </summary>
        public TVar MaximizationObjective { get; set; }

        /// <summary>
        /// The demand expression for any pair of nodes.
        /// </summary>
        public IDictionary<(string, string), TVar> DemandVariables { get; set; }
    }
}
