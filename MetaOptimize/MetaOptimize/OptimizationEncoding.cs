// <copyright file="OptimizationEncoding.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    /// <summary>
    /// The encoding of an optimization.
    /// </summary>
    public class OptimizationEncoding
    {
        /// <summary>
        /// The feasibility constraints for the encoding.
        /// </summary>
        public Zen<bool> FeasibilityConstraints { get; set; }

        /// <summary>
        /// The optimality constraints based on the KKT conditions.
        /// </summary>
        public Zen<bool> OptimalConstraints { get; set; }

        /// <summary>
        /// The maximization objective.
        /// </summary>
        public Zen<Real> MaximizationObjective { get; set; }
    }
}
