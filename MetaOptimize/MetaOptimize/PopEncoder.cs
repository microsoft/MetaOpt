// <copyright file="PopEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The Pop encoder for splitting a network capacity into pieces.
    /// </summary>
    public class PopEncoder : IEncoder
    {
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
        public OptimalEncoder[] PartitionEncoders { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="PopEncoder"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="k">The max number of paths between nodes.</param>
        /// <param name="numPartitions">The number of partitions.</param>
        /// <param name="demandPartitions">The demand partitions.</param>
        public PopEncoder(Topology topology, int k, int numPartitions, IDictionary<(string, string), int> demandPartitions)
        {
            if (numPartitions <= 0)
            {
                throw new ArgumentOutOfRangeException("Partitions must be greater than zero.");
            }

            this.Topology = topology;
            this.K = k;
            this.ReducedTopology = topology.SplitCapacity(numPartitions);
            this.NumPartitions = numPartitions;
            this.DemandPartitions = demandPartitions;

            this.PartitionEncoders = new OptimalEncoder[this.NumPartitions];

            for (int i = 0; i < this.NumPartitions; i++)
            {
                var demandConstraints = new Dictionary<(string, string), Real>();

                foreach (var demand in this.DemandPartitions)
                {
                    if (demand.Value != i)
                    {
                        demandConstraints[demand.Key] = new Real(0);
                    }
                }

                this.PartitionEncoders[i] = new OptimalEncoder(this.ReducedTopology, this.K, demandConstraints);
            }
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding Encoding()
        {
            var encodings = new OptimizationEncoding[NumPartitions];

            for (int i = 0; i < this.NumPartitions; i++)
            {
                encodings[i] = this.PartitionEncoders[i].Encoding();
            }

            var demandExpressions = new Dictionary<(string, string), Zen<Real>>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                demandExpressions[pair] = this.PartitionEncoders.Select(e => e.DemandVariables[pair]).Aggregate(Zen.Plus);
            }

            return new OptimizationEncoding
            {
                FeasibilityConstraints = encodings.Select(x => x.FeasibilityConstraints).Aggregate(Zen.And),
                OptimalConstraints = encodings.Select(x => x.OptimalConstraints).Aggregate(Zen.And),
                MaximizationObjective = encodings.Select(x => x.MaximizationObjective).Aggregate(Zen.Plus),
                DemandExpressions = demandExpressions,
            };
        }

        /// <summary>
        /// Get the optimization solution from the solver solution.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public OptimizationSolution GetSolution(ZenSolution solution)
        {
            var demands = new Dictionary<(string, string), Real>();
            var flows = new Dictionary<(string, string), Real>();
            var flowPaths = new Dictionary<string[], Real>(new PathComparer());

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
