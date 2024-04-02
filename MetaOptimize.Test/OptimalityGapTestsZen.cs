// <copyright file="OptimalityGapTestsZen.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;
    using ZenLib.ModelChecking;

    /// <summary>
    /// Tests for the optimality gap.
    /// </summary>
    [TestClass]
    [Ignore]
    public class OptimalityGapTestsZen : OptimalityGapTests<Zen<Real>, ZenSolution>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new SolverZen();
        }
    }
}