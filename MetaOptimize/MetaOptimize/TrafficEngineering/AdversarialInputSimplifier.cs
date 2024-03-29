// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Gurobi;

    /// <summary>
    /// Meta-optimization utility functions for simplifying optimality gaps.
    /// </summary>
    public class TEAdversarialInputSimplifier<TVar, TSolution>
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        protected Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        protected int maxNumPaths { get; set; }

        /// <summary>
        /// The demand variables.
        /// </summary>
        protected Dictionary<(string, string), Polynomial<TVar>> DemandVariables { get; set; }

        /// <summary>
        /// This class simplifies the TE problem and is written to be specific to TE.
        /// The idea is you find a gap and then try to find the demand that has the minimum number of non-zero elements that achieves the gap.
        /// </summary>
        public TEAdversarialInputSimplifier(Topology topology, int maxNumPath, Dictionary<(string, string), Polynomial<TVar>> DemandVariables)
        {
            this.Topology = topology;
            this.maxNumPaths = maxNumPath;
            this.DemandVariables = DemandVariables;
        }

        /// <summary>
        /// find minimum number of non-zero demands that achieves the desiredGap
        /// using Gurobi Direct Optimzation form.
        /// </summary>
        /// TODO - Engineering: Later add a version of this to a utility
        /// class so that other encoders can use it too.
        public Polynomial<TVar> AddDirectMinConstraintsAndObjectives(
            ISolver<TVar, TSolution> solver,
            Polynomial<TVar> gapObjective,
            double desiredGap)
        {
            // adding optimal - heuristic >= desiredGap
            var gapPoly = gapObjective.Negate();
            gapPoly.Add(new Term<TVar>(desiredGap));
            solver.AddLeqZeroConstraint(gapPoly);

            // adding f_i <= Mx_i where x_i is binary
            var minObj = new Polynomial<TVar>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var auxDemandMinVar = solver.CreateVariable("aux_mindemand_" + pair.Item1 + "_" + pair.Item2, type: GRB.BINARY);
                var poly = this.DemandVariables[pair].Copy();
                poly.Add(new Term<TVar>(-1 * this.Topology.MaxCapacity() * this.maxNumPaths, auxDemandMinVar));
                solver.AddLeqZeroConstraint(poly);
                minObj.Add(new Term<TVar>(1, auxDemandMinVar));
            }
            return minObj.Negate();
        }
    }
}