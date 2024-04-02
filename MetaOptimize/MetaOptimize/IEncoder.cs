// <copyright file="IEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;
    /// <summary>
    /// An interface for an optimization encoder.
    /// </summary>
    /// TODO: we need a better comment here since this is one of the functions one would heavily use in order to
    /// write a new heuristic.
    public interface IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The solver used for the encoder.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// reset the heuristic.
        /// </summary>
        public void CleanAll()
        {
            throw new System.Exception("Not Implemented....");
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        /// TODO: need to change the variable names to be more general and not be specific to the TE problem.
        public OptimizationEncoding<TVar, TSolution> Encoding(Topology topology, Dictionary<(string, string), Polynomial<TVar>> preInputVariables = null,
            Dictionary<(string, string), double> inputEqualityConstraints = null, bool noAdditionalConstraints = false,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            PathType pathType = PathType.KSP, Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicInputConstraints = null,
            int numProcesses = -1, bool verbose = false)
        {
            throw new System.Exception("TE Not Implemented....");
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding<TVar, TSolution> Encoding(Bins bins,
            Dictionary<int, List<TVar>> preInputVariables = null,
            Dictionary<int, List<double>> inputEqualityConstraints = null,
            Dictionary<int, int> inputPlacementEqualityConstraints = null,
            bool verbose = false)
        {
            throw new System.Exception("VBP Not Implemented....");
        }
        /// <summary>
        ///  Encode the PIFO problem.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(
            Dictionary<int, TVar> preRankVariables = null,
            Dictionary<int, int> rankEqualityConstraints = null,
            bool verbose = false)
        {
            throw new System.Exception("PIFO Not Implemented....");
        }

        /// <summary>
        /// Get the optimization solution from the solver.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public OptimizationSolution GetSolution(TSolution solution);

        /// <summary>
        /// Get the optimization solution from the solver.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <param name="solutionNumber">which solution to return if multiple exist.</param>
        public OptimizationSolution GetSolution(TSolution solution, int solutionNumber)
        {
            throw new System.Exception("Not implemented for multiple solutions");
        }
    }
}
