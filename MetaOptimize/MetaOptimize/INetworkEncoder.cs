// <copyright file="INetworkEncoding.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    /// <summary>
    /// A class for the optimal encoding.
    /// </summary>
    public interface INetworkEncoder
    {
        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding Encoding();

        /// <summary>
        /// Display a solution to this encoding.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public void DisplaySolution(ZenSolution solution);
    }
}
