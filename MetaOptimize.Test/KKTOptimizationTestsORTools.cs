// <copyright file="KktOptimizationTestsGurobiNoParams.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using Google.OrTools.LinearSolver;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Some basic optimization tests.
    /// </summary>
    [TestClass]
    public class KktOptimizationTestsORTools : KktOptimizationTests<Variable, Solver>
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