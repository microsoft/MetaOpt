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
    public class PopEncoder : INetworkEncoder
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        public Topology Topology { get; set; }

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
        public Dictionary<(string, string), int> DemandPartitions { get; set; }

        /// <summary>
        /// The individual encoders for each partition.
        /// </summary>
        public OptimalEncoder[] PartitionEncoders { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="PopEncoder"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="numPartitions">The number of partitions.</param>
        /// <param name="demandPartitions">The demand partitions.</param>
        public PopEncoder(Topology topology, int numPartitions, Dictionary<(string, string), int> demandPartitions)
        {
            if (numPartitions <= 0)
            {
                throw new ArgumentOutOfRangeException("Partitions must be greater than zero.");
            }

            this.Topology = topology;
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

                this.PartitionEncoders[i] = new OptimalEncoder(this.ReducedTopology, demandConstraints);
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

            return new OptimizationEncoding
            {
                FeasibilityConstraints = encodings.Select(x => x.FeasibilityConstraints).Aggregate(Zen.And),
                OptimalConstraints = encodings.Select(x => x.OptimalConstraints).Aggregate(Zen.And),
                MaximizationObjective = encodings.Select(x => x.MaximizationObjective).Aggregate(Zen.Plus),
            };
        }

        /// <summary>
        /// Display a solution to this encoding.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public void DisplaySolution(ZenSolution solution)
        {
            Console.WriteLine($"--------------------------");
            Console.WriteLine($"Overall solution");
            Console.WriteLine($"--------------------------");

            Console.WriteLine($"total demand met: {this.PartitionEncoders.Select(x => solution.Get(x.TotalDemandMetVariable)).Aggregate((a, b) => a + b)}");

            foreach (var pair in this.Topology.GetNodePairs())
            {
                var demand = this.PartitionEncoders.Select(x => solution.Get(x.DemandVariables[pair])).Aggregate((a, b) => a + b);
                if (demand > 0)
                    Console.WriteLine($"demand for {pair} = {demand}");
            }

            foreach (var (pair, paths) in this.PartitionEncoders[0].SimplePaths)
            {
                foreach (var path in paths)
                {
                    var flow = this.PartitionEncoders.Select(x => solution.Get(x.FlowVariables[pair])).Aggregate((a, b) => a + b);
                    if (flow > 0)
                        Console.WriteLine($"allocation for [{string.Join(",", path)}] = {flow}");
                }
            }

            for (int i = 0; i < this.NumPartitions; i++)
            {
                Console.WriteLine($"--------------------------");
                Console.WriteLine($"Solution for partition {i}");
                Console.WriteLine($"--------------------------");
                this.PartitionEncoders[i].DisplaySolution(solution);
            }
        }
    }
}
