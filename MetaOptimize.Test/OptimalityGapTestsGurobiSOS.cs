// <copyright file="OptimalityGapTestsGurobiNoParams.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for the optimality gap.
    /// </summary>
    [TestClass]
    public class OptimalityGapTestsGurobiSOS : OptimalityGapTests<GRBVar, GRBModel>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new GurobiSOS();
        }
    }
}