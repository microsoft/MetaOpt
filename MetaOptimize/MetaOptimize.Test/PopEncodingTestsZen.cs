// <copyright file="PopEncodingTestsZen.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;

    /// <summary>
    /// Tests for the pop encoder.
    /// </summary>
    [TestClass]
    public class PopEncodingTestsZen : PopEncodingTests<Zen<Real>, ZenSolution>
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