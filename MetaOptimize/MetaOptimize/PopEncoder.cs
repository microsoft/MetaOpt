// <copyright file="PopEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ZenLib;

    /// <summary>
    /// The Pop encoder for splitting a network capacity into pieces.
    /// </summary>
    public class PopEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The solver being used.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// The topology for the network.
        /// </summary>
        public Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        public int K { get; set; }

        /// <summary>
        /// The reduced capacity topology for the network.
        /// </summary>
        public Topology ReducedTopology { get; set; }

        /// <summary>
        /// The number of partitions to use.
        /// </summary>
        public int NumPartitions { get; set; }

        /// <summary>
        /// Partitioning of the demands.
        /// </summary>
        public IDictionary<(string, string), int> DemandPartitions { get; set; }

        /// <summary>
        /// The individual encoders for each partition.
        /// </summary>
        public OptimalEncoder<TVar, TSolution>[] PartitionEncoders { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="PopEncoder{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="solver">The solver to use.</param>
        /// <param name="topology">The network topology.</param>
        /// <param name="k">The max number of paths between nodes.</param>
        /// <param name="numPartitions">The number of partitions.</param>
        /// <param name="demandPartitions">The demand partitions.</param>
        public PopEncoder(ISolver<TVar, TSolution> solver, Topology topology, int k, int numPartitions, IDictionary<(string, string), int> demandPartitions)
        {
            if (numPartitions <= 0)
            {
                throw new ArgumentOutOfRangeException("Partitions must be greater than zero.");
            }

            this.Solver = solver;
            this.Topology = topology;
            this.K = k;
            this.ReducedTopology = topology.SplitCapacity(numPartitions);
            this.NumPartitions = numPartitions;
            this.DemandPartitions = demandPartitions;

            this.PartitionEncoders = new OptimalEncoder<TVar, TSolution>[this.NumPartitions];

            for (int i = 0; i < this.NumPartitions; i++)
            {
                var demandConstraints = new Dictionary<(string, string), double>();

                foreach (var demand in this.DemandPartitions)
                {
                    if (demand.Value != i)
                    {
                        demandConstraints[demand.Key] = 0;
                    }
                }

                this.PartitionEncoders[i] = new OptimalEncoder<TVar, TSolution>(solver, this.ReducedTopology, this.K, demandConstraints);
            }
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding<TVar, TSolution> Encoding()
        {
            var encodings = new OptimizationEncoding<TVar, TSolution>[NumPartitions];

            // get all the separate encodings.
            for (int i = 0; i < this.NumPartitions; i++)
            {
                encodings[i] = this.PartitionEncoders[i].Encoding();
            }

            // create new demand variables as the sum of the individual partitions.
            var demandVariables = new Dictionary<(string, string), TVar>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var demandVariable = this.Solver.CreateVariable("demand_pop_" + pair.Item1 + "_" + pair.Item2);
                var polynomial = new Polynomial<TVar>(new Term<TVar>(-1, demandVariable));

                foreach (var encoder in this.PartitionEncoders)
                {
                    polynomial.Terms.Add(new Term<TVar>(1, encoder.DemandVariables[pair]));
                }

                this.Solver.AddEqZeroConstraint(polynomial);

                demandVariables[pair] = demandVariable;
            }

            // compute the objective to optimize.
            var objectiveVariable = this.Solver.CreateVariable("objective_pop");
            var objective = new Polynomial<TVar>(new Term<TVar>(-1, objectiveVariable));
            foreach (var encoding in encodings)
            {
                objective.Terms.Add(new Term<TVar>(1, encoding.MaximizationObjective));
            }

            this.Solver.AddEqZeroConstraint(objective);

            return new OptimizationEncoding<TVar, TSolution>
            {
                MaximizationObjective = objectiveVariable,
                DemandVariables = demandVariables,
            };
        }

        /// <summary>
        /// Get the optimization solution from the solver solution.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var demands = new Dictionary<(string, string), double>();
            var flows = new Dictionary<(string, string), double>();
            var flowPaths = new Dictionary<string[], double>(new PathComparer());

            var solutions = this.PartitionEncoders.Select(e => e.GetSolution(solution)).ToList();

            foreach (var pair in this.Topology.GetNodePairs())
            {
                demands[pair] = solutions.Select(s => s.Demands[pair]).Aggregate((a, b) => a + b);
                flows[pair] = solutions.Select(s => s.Flows[pair]).Aggregate((a, b) => a + b);
            }

            foreach (var path in solutions[0].FlowsPaths.Keys)
            {
                flowPaths[path] = solutions.Select(s => s.FlowsPaths[path]).Aggregate((a, b) => a + b);
            }

            return new OptimizationSolution
            {
                TotalDemandMet = solutions.Select(s => s.TotalDemandMet).Aggregate((a, b) => a + b),
                Demands = demands,
                Flows = flows,
                FlowsPaths = flowPaths,
            };
        }
    }
}
