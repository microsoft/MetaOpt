// <copyright file="INetworkEncoding.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A class for the optimal encoding.
    /// </summary>
    public interface INetworkEncoding<TConstraint, TMax, TSolution>
    {
        /// <summary>
        /// Computes the optimization objective.
        /// </summary>
        /// <returns>The optimization objective.</returns>
        public TMax MaximizationObjective();

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public IList<TConstraint> Constraints();

        /// <summary>
        /// Display a solution to this encoding.
        /// </summary>
        /// <param name="solution"></param>
        public void DisplaySolution(TSolution solution);
    }
}
