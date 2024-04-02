// <copyright file="PopEncodingTestsGurobi.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for the pop encoder.
    /// </summary>
    [TestClass]
    [Ignore]
    public class PopEncodingTestsGurobi : PopEncodingTests<GRBVar, GRBModel>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new SolverGurobi();
        }
    }
}