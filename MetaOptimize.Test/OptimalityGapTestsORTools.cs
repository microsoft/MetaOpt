// <copyright file="OptimalityGapTestsGurobi.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Google.OrTools.LinearSolver;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for the optimality gap.
    /// </summary>
    [TestClass]
    public class OptimalityGapTestsORTools : OptimalityGapTests<Variable, Solver>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new ORToolsSolver();
        }
    }
}