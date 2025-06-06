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
    /// uses Gurobi to test demand pinning.
    /// </summary>
    [TestClass]
    [Ignore]
    public class DemandPinningGurobiBinary : DemandPinningTests<GRBVar, GRBModel>
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
