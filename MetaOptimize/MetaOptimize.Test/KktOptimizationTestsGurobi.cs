// <copyright file="KktOptimizationTestsGurobi.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Some basic optimization tests.
    /// </summary>
    [TestClass]
    public class KktOptimizationTestsGurobi : KktOptimizationTests<GRBVar, GRBModel>
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