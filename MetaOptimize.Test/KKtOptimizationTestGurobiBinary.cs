﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    /// <summary>
    /// Tests Gurobi Or Version.
    /// </summary>
    [TestClass]
    [Ignore]
    public class KKtOptimizationTestGurobiBinary : PopEncodingTests<GRBVar, GRBModel>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new GurobiBinary();
        }
    }
}