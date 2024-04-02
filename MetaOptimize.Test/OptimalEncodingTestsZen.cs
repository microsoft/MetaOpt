// <copyright file="OptimalEncodingTestsZen.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;
    using ZenLib.ModelChecking;

    /// <summary>
    /// Tests for the optimal encoder.
    /// </summary>
    [TestClass]
    [Ignore]
    public class OptimalEncodingTestsZen : OptimalEncodingTests<Zen<Real>, ZenSolution>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        [Ignore]
        public void Initialize()
        {
            this.CreateSolver = () => new SolverZen();
        }
    }
}