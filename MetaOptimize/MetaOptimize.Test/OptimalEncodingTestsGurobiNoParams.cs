// <copyright file="OptimalEncodingTestsGurobiNoParams.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for the optimal encoder.
    /// </summary>
    [TestClass]
    public class OptimalEncodingTestsGurobiNoParams : OptimalEncodingTests<GRBVar, GRBModel>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new SolverGurobiNoParams();
        }
    }
}