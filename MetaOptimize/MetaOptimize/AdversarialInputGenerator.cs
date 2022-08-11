// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Gurobi;

    /// <summary>
    /// Meta-optimization utility functions for maximizing optimality gaps.
    /// </summary>
    public class AdversarialInputGenerator<TVar, TSolution>
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        protected Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        protected int K { get; set; }

        /// <summary>
        /// number of processors to use for multiprocessing purposes.
        /// </summary>
        protected int NumProcesses { get; set; }

        /// <summary>
        /// The demand variables.
        /// </summary>
        protected Dictionary<(string, string), Polynomial<TVar>> DemandVariables { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public AdversarialInputGenerator(Topology topology, int k, int numProcesses = -1) {
            this.Topology = topology;
            this.K = k;
            this.NumProcesses = numProcesses;
        }

        private TSolution SimplifyAdversarialInputs(bool simplify, IEncoder<TVar, TSolution> optimalEncoder, IEncoder<TVar, TSolution> heuristicEncoder,
            TSolution solution, Polynomial<TVar> objective)
        {
            if (simplify) {
                var solver = optimalEncoder.Solver;
                Console.WriteLine("===== Going to simplify the solution....");
                var simplifier = new AdversarialInputSimplifier<TVar, TSolution>(Topology, K, DemandVariables);
                var optimalObj = optimalEncoder.GetSolution(solution).TotalDemandMet;
                var heuristicObj = heuristicEncoder.GetSolution(solution).TotalDemandMet;
                var gap = optimalObj - heuristicObj;
                var simplifyObj = simplifier.AddDirectMinConstraintsAndObjectives(solver, objective, gap);
                solution = solver.Maximize(simplifyObj, reset: true);
            }
            return solution;
        }
        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT,
            IDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false,
            bool cleanUpSolver = true,
            IDictionary<(string, string), double> perDemandUB = null)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (innerEncoding == InnerEncodingMethodChoice.PrimalDual & demandList == null)
            {
                throw new Exception("should provide the demand list if inner encoding method is primal dual.");
            }
            if (demandUB != -1 & perDemandUB != null) {
                throw new Exception("if global demand ub is enabled, then perDemandUB should be null");
            }

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver) {
                solver.CleanAll();
            }

            Utils.logger("creating demand variables.", verbose);
            CreateDemandVariables(solver, innerEncoding, demandList);
            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, preDemandVariables: this.DemandVariables,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preDemandVariables: this.DemandVariables,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Utils.logger("adding constraints for upper bound on demands.", verbose);
            if (perDemandUB != null) {
                EnsureDemandUB(solver, perDemandUB);
            } else {
                EnsureDemandUB(solver, demandUB);
            }
            Utils.logger("adding equality constraints for specified demands.", verbose);
            EnsureDemandEquality(solver, constrainedDemands);

            // var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
            // foreach (var (pair, demandVar) in this.DemandVariables) {
            //     var constrName = solver.AddLeqZeroConstraint(demandVar);
            //     pairNameToConstraintMapping[pair] = constrName;
            // }
            // var objectiveVariable = solver.CreateVariable("objective");
            Utils.logger("setting the objective.", verbose);
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
            var solution = solver.Maximize(objective, reset: true);

            // var allNodeNames = this.Topology.GetAllNodes();
            // if (demandUB < 0) {
            //     demandUB = this.K * this.Topology.MaxCapacity();
            // }

            // var newBstGapFound = false;
            // var iterNo = 0;
            // var currBstGap = 0.0;
            // var rng = new Random();
            // do {
            //     var seenNodes = new HashSet<string>();
            //     var prevBstGap = currBstGap;
            //     newBstGapFound = false;
            //     var randomized1 = allNodeNames.OrderBy(item => rng.Next()).ToList<string>();
            //     var randomized2 = allNodeNames.OrderBy(item => rng.Next()).ToList<string>();
            //     foreach (var id in Enumerable.Range(0, allNodeNames.Count())) {
            //         var node1 = randomized1[id];
            //         var node2 = randomized2[id];
            //         Utils.logger("== greedy iteration " + iterNo + " demand from " + node1 + " and " + node2 + " curr bst gap=" + currBstGap + " prev bst gap=" + prevBstGap, verbose);
            //         foreach (var (pair, demandVar) in this.DemandVariables) {
            //             if (pair.Item1.Equals(node1) || pair.Item1.Equals(node2)) {
            //                 // Console.WriteLine("[INFO] setting demand from " + pair.Item1 + " to " + pair.Item2 + " max!!");
            //                 solver.ChangeConstraintRHS(pairNameToConstraintMapping[pair], demandUB);
            //             }
            //         }
            //         solution = solver.Maximize(objective, reset: true);
            //         // solution = SimplifyAdversarialInputs(simplify, optimalEncoder, heuristicEncoder, solution, objective);
            //         var demands = optimalEncoder.GetSolution(solution).Demands;
            //         var currGap = optimalEncoder.GetSolution(solution).TotalDemandMet - heuristicEncoder.GetSolution(solution).TotalDemandMet;
            //         if (currGap >= currBstGap + 0.01) {
            //             currBstGap = currGap;
            //             newBstGapFound = true;
            //         }
            //         seenNodes.Add(node1);
            //         seenNodes.Add(node2);
            //         foreach (var (pair, rate) in demands) {
            //             if (seenNodes.Contains(pair.Item1)) {
            //                 // Console.WriteLine("[INFO] setting demand from " + pair.Item1 + " to " + pair.Item2 + " = " + rate + "!!");
            //                 solver.ChangeConstraintRHS(pairNameToConstraintMapping[pair], rate);
            //                 // var ratePoly = this.DemandVariables[pair].Copy();
            //                 // ratePoly.Add(new Term<TVar>(-1 * rate));
            //                 // solver.AddEqZeroConstraint(ratePoly);
            //             }
            //         }
            //     }
            //     iterNo += 1;
            // } while (newBstGapFound);

            solution = SimplifyAdversarialInputs(simplify, optimalEncoder, heuristicEncoder, solution, objective);
            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));

            /* if (!solution.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (Opt):");
            optimalEncoder.DisplaySolution(solution);
            Console.WriteLine();
            Console.WriteLine("Optimal (Pop):");
            heuristicEncoder.DisplaySolution(solution); */
        }

        /// <summary>
        /// Maximize optimality gap with clustering method used for scale up.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGapWithClusteringV2(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT,
            GenericDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false)
        {
            var seenNode = new HashSet<string>();
            foreach (var cluster in clusters) {
                foreach (var node in cluster.GetAllNodes()) {
                    if (seenNode.Contains(node)) {
                        throw new Exception("duplicate nodes over two clusters");
                    }
                    seenNode.Add(node);
                }
            }
            if (seenNode.Count() != this.Topology.GetAllNodes().Count()) {
                throw new Exception("missmatch between number of nodes in original problem and clustered version");
            }
            if (constrainedDemands == null) {
                constrainedDemands = new Dictionary<(string, string), double>();
            }

            var solver = optimalEncoder.Solver;
            solver.CleanAll();
            Utils.logger("creating demand variables.", verbose);
            CreateDemandVariables(solver, innerEncoding, demandList);
            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, preDemandVariables: this.DemandVariables,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preDemandVariables: this.DemandVariables,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Utils.logger("adding constraints for upper bound on demands.", verbose);
            EnsureDemandUB(solver, demandUB);
            Utils.logger("adding equality constraints for specified demands.", verbose);
            EnsureDemandEquality(solver, constrainedDemands);

            var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
            foreach (var (pair, demandVar) in this.DemandVariables) {
                if (constrainedDemands.ContainsKey(pair)) {
                    continue;
                }
                var constrName = solver.AddLeqZeroConstraint(demandVar);
                pairNameToConstraintMapping[pair] = constrName;
            }
            solver.ModelUpdate();

            if (demandUB < 0) {
                demandUB = this.K * this.Topology.MaxCapacity();
            }

            var demandMatrix = new Dictionary<(string, string), double>();
            var consideredPairs = new HashSet<(string, string)>();
            // find gap for all the clusters
            foreach (var cluster in clusters) {
                Utils.logger(
                    string.Format("finding adversarial demand for cluster with {0} nodes and {1} edges", cluster.GetAllNodes().Count(), cluster.GetAllEdges().Count()),
                    verbose);
                foreach (var pair in cluster.GetNodePairs()) {
                    if (constrainedDemands.ContainsKey(pair)) {
                        continue;
                    }
                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[pair], demandUB);
                    consideredPairs.Add(pair);
                }
                Utils.logger("setting the objective.", verbose);
                var objective = new Polynomial<TVar>(
                            new Term<TVar>(1, optimalEncoding.GlobalObjective),
                            new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
                var solution = solver.Maximize(objective, reset: true);
                var optimalSolution = optimalEncoder.GetSolution(solution);
                foreach (var pair in consideredPairs) {
                    demandMatrix[pair] = optimalSolution.Demands[pair];
                    // AddSingleDemandEquality(solver, pair, demandMatrix[pair]);
                    AddSingleDemandUB(solver, pair, demandMatrix[pair]);
                }

                if (verbose) {
                    var numPairs = 0.0;
                    var numNonZeroDemands = 0.0;
                    foreach (var pair in cluster.GetNodePairs()) {
                        var demand = demandMatrix[pair];
                        if (demand > 0) {
                            numNonZeroDemands += 1;
                        }
                        numPairs += 1;
                    }
                    Utils.logger(
                        string.Format("fraction of non zero demands {0}", numNonZeroDemands / numPairs),
                        verbose);
                }
            }

            for (int cid1 = 0; cid1 < clusters.Count(); cid1++) {
                for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++) {
                    Utils.logger(
                        string.Format("inter-cluster adversarial demand between cluster {0} and cluster {1}", cid1, cid2),
                        verbose);
                    var cluster1Nodes = clusters[cid1].GetAllNodes().ToList();
                    var cluster2Nodes = clusters[cid2].GetAllNodes().ToList();
                    bool neighbor = false;
                    foreach (var node1 in cluster1Nodes) {
                        foreach (var node2 in cluster2Nodes) {
                            if (this.Topology.ContaintsEdge(node1, node2)) {
                                neighbor = true;
                                break;
                            }
                        }
                        if (neighbor) {
                            break;
                        }
                    }
                    if (!neighbor) {
                        Utils.logger("skipping the cluster pairs since they are not neighbors", verbose);
                        continue;
                    }
                    foreach (var node1 in cluster1Nodes) {
                        foreach (var node2 in cluster2Nodes) {
                            if (constrainedDemands.ContainsKey((node1, node2))) {
                                continue;
                            }
                            solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node1, node2)], demandUB);
                            consideredPairs.Add((node1, node2));

                            if (constrainedDemands.ContainsKey((node2, node1))) {
                                continue;
                            }
                            solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node2, node1)], demandUB);
                            consideredPairs.Add((node2, node1));
                        }
                    }
                    Utils.logger("setting the objective.", verbose);
                    var objective = new Polynomial<TVar>(
                                new Term<TVar>(1, optimalEncoding.GlobalObjective),
                                new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    var optimalSolution = optimalEncoder.GetSolution(solution);
                    foreach (var pair in consideredPairs) {
                        demandMatrix[pair] = optimalSolution.Demands[pair];
                        // AddSingleDemandEquality(solver, (node1, node2), demandMatrix[(node1, node2)]);
                        AddSingleDemandUB(solver, pair, demandMatrix[pair]);
                    }
                }
            }

            foreach (var pair in this.Topology.GetNodePairs()) {
                if (!demandMatrix.ContainsKey(pair)) {
                    demandMatrix[pair] = 0;
                }
            }
            var output = GetGap(optimalEncoder, heuristicEncoder, demandMatrix);
            Utils.logger("Final gap: " + output.Item1, verbose);
            return output.Item2;
        }

        /// <summary>
        /// Maximize optimality gap with clustering method used for scale up.
        /// First Optimizes all the clusters. Then, uses random sampling for.
        /// inter-cluster traffic.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGapWithClusteringV1(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT,
            GenericDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false)
        {
            var seenNode = new HashSet<string>();
            foreach (var cluster in clusters) {
                foreach (var node in cluster.GetAllNodes()) {
                    if (seenNode.Contains(node)) {
                        throw new Exception("duplicate nodes over two clusters");
                    }
                    seenNode.Add(node);
                }
            }
            if (seenNode.Count() != this.Topology.GetAllNodes().Count()) {
                throw new Exception("missmatch between number of nodes in original problem and clustered version");
            }

            if (constrainedDemands != null) {
                throw new Exception("the constrained demand option is not implemented yet!!!");
            }

            var demandMatrix = new Dictionary<(string, string), double>();
            foreach (var cluster in clusters) {
                optimalEncoder.Solver.CleanAll();
                Utils.logger("Cluster with " + cluster.GetAllNodes().Count() + " nodes and " + cluster.GetAllEdges().Count() + " edges", verbose);
                var adversarialInputGenerator = new AdversarialInputGenerator<TVar, TSolution>(cluster, this.K, this.NumProcesses);
                var clusterResult = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB, innerEncoding, demandList: demandList,
                        simplify: simplify, verbose: verbose);
                foreach (var pair in cluster.GetNodePairs()) {
                    if (demandMatrix.ContainsKey(pair)) {
                        throw new Exception("cluster are not independepnt");
                    }
                    demandMatrix[pair] = clusterResult.Item1.Demands[pair];
                }
            }

            if (numInterClusterSamples > 0) {
                Debug.Assert(numNodePerCluster > 0);
                if (verbose) {
                    var preDemandMatrix = new Dictionary<(string, string), double>();
                    foreach (var pair in this.Topology.GetNodePairs()) {
                        if (demandMatrix.ContainsKey(pair)) {
                            preDemandMatrix[pair] = demandMatrix[pair];
                        } else {
                            preDemandMatrix[pair] = 0;
                        }
                    }
                    var preInterCluster = GetGap(optimalEncoder, heuristicEncoder, preDemandMatrix);
                    Utils.logger("pre inter-cluster gap: " + preInterCluster.Item1, verbose);
                }
                var solver = optimalEncoder.Solver;
                solver.CleanAll();
                Utils.logger("creating demand variables.", verbose);
                CreateDemandVariables(solver, innerEncoding, demandList);
                Utils.logger("generating optimal encoding.", verbose);
                var optimalEncoding = optimalEncoder.Encoding(this.Topology, preDemandVariables: this.DemandVariables,
                                        demandEqualityConstraints: demandMatrix, innerEncoding: innerEncoding,
                                        numProcesses: this.NumProcesses, verbose: verbose);
                Utils.logger("generating heuristic encoding.", verbose);
                var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preDemandVariables: this.DemandVariables,
                                        demandEqualityConstraints: demandMatrix, innerEncoding: innerEncoding,
                                        numProcesses: this.NumProcesses, verbose: verbose);

                // ensures that demand in both problems is the same and lower than demand upper bound constraint.
                Utils.logger("adding constraints for upper bound on demands.", verbose);
                EnsureDemandUB(solver, demandUB);
                Utils.logger("adding equality constraints for specified demands.", verbose);
                EnsureDemandEquality(solver, constrainedDemands);

                var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
                foreach (var (pair, demandVar) in this.DemandVariables) {
                    if (demandMatrix.ContainsKey(pair)) {
                        continue;
                    }
                    var constrName = solver.AddLeqZeroConstraint(demandVar);
                    pairNameToConstraintMapping[pair] = constrName;
                }

                // Console.WriteLine("adding eq = 0 for {0}", string.Join(",", pairNameToConstraintMapping.Keys));
                // var objectiveVariable = solver.CreateVariable("objective");
                var rng = new Random();
                for (int l = 0; l < numInterClusterSamples; l++) {
                    Utils.logger(
                        string.Format("trying the {0}-th set of inter-cluster nodes each of size {1}", l, numNodePerCluster), verbose);
                    solver.ModelUpdate();
                    var interClusterNodes = new List<List<string>>();
                    foreach (var cluster in clusters) {
                        var nodeNames = cluster.GetAllNodes().ToList();
                        var repNodes = new List<string>();
                        for (int i = 0; i < numNodePerCluster; i++) {
                            var idx = rng.Next(nodeNames.Count());
                            repNodes.Add(nodeNames[idx]);
                        }
                        Console.WriteLine(String.Format("cluster rep nodes; {0}", string.Join("_", repNodes)));
                        interClusterNodes.Add(repNodes);
                    }

                    if (demandUB < 0) {
                        demandUB = this.K * this.Topology.MaxCapacity();
                    }

                    var currPairs = new HashSet<(string, string)>();
                    for (int cid1 = 0; cid1 < clusters.Count(); cid1++) {
                        for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++) {
                            var cluster1Nodes = interClusterNodes[cid1];
                            var cluster2Nodes = interClusterNodes[cid2];
                            foreach (var node1 in cluster1Nodes) {
                                foreach (var node2 in cluster2Nodes) {
                                    // Console.WriteLine(string.Format("node 1 {0} cluster {1} node 2 {2} cluster {3}",
                                    //         node1, cid1, node2, cid2));
                                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node1, node2)], demandUB);
                                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node2, node1)], demandUB);
                                    currPairs.Add((node1, node2));
                                    currPairs.Add((node2, node1));
                                }
                            }
                        }
                    }

                    Utils.logger("setting the objective.", verbose);
                    var objective = new Polynomial<TVar>(
                                new Term<TVar>(1, optimalEncoding.GlobalObjective),
                                new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    var optimalSolution = optimalEncoder.GetSolution(solution);
                    foreach (var pair in this.Topology.GetNodePairs()) {
                        if (demandMatrix.ContainsKey(pair)) {
                            // Console.WriteLine(demandMatrix[pair].ToString() + " " + optimalSolution.Demands[pair].ToString());
                            if (optimalSolution.Demands.ContainsKey(pair)) {
                                Debug.Assert(Math.Abs(demandMatrix[pair] - optimalSolution.Demands[pair]) <= 0.001);
                            } else {
                                Debug.Assert(demandMatrix[pair] <= 0.001);
                            }
                        } else if (currPairs.Contains(pair)) {
                            demandMatrix[pair] = optimalSolution.Demands[pair];
                            var ratePoly = this.DemandVariables[pair].Copy();
                            ratePoly.Add(new Term<TVar>(-1 * demandMatrix[pair]));
                            solver.AddEqZeroConstraint(ratePoly);
                            // Utils.logger(
                            //     string.Format("demand from {0} to {1} is: {2}", pair.Item1, pair.Item2, demandMatrix[pair]),
                            //     verbose);
                        }
                    }
                }
            }

            foreach (var pair in this.Topology.GetNodePairs()) {
                if (!demandMatrix.ContainsKey(pair)) {
                    demandMatrix[pair] = 0;
                }
            }
            var output = GetGap(optimalEncoder, heuristicEncoder, demandMatrix);
            Utils.logger("Final gap: " + output.Item1, verbose);
            return output.Item2;
        }

        /// <summary>
        /// Maximize optimality gap with clustering method used for scale up.
        /// First optimizes each cluster. Then, finds the optimal input for the
        /// inter-cluster traffic on abstracted topology and randomly assings the
        /// flows to each cluster.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGapWithClusteringV3(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT,
            GenericDemandList demandList = null,
            int numInterClusterQuantizations = -1,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false)
        {
            var seenNode = new HashSet<string>();
            foreach (var cluster in clusters) {
                foreach (var node in cluster.GetAllNodes()) {
                    if (seenNode.Contains(node)) {
                        throw new Exception("duplicate nodes over two clusters");
                    }
                    seenNode.Add(node);
                }
            }
            if (seenNode.Count() != this.Topology.GetAllNodes().Count()) {
                throw new Exception("missmatch between number of nodes in original problem and clustered version");
            }

            if (constrainedDemands != null) {
                throw new Exception("the constrained demand option is not implemented yet!!!");
            }

            var demandMatrix = new Dictionary<(string, string), double>();
            foreach (var cluster in clusters) {
                optimalEncoder.Solver.CleanAll();
                Utils.logger("Cluster with " + cluster.GetAllNodes().Count() + " nodes and " + cluster.GetAllEdges().Count() + " edges", verbose);
                var clusterAdversarialInputGenerator = new AdversarialInputGenerator<TVar, TSolution>(cluster, this.K, this.NumProcesses);
                var clusterResult = clusterAdversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB, innerEncoding,
                        demandList: demandList, simplify: simplify, verbose: verbose);
                foreach (var pair in cluster.GetNodePairs()) {
                    if (demandMatrix.ContainsKey(pair)) {
                        throw new Exception("cluster are not independepnt");
                    }
                    demandMatrix[pair] = clusterResult.Item1.Demands[pair];
                }
            }

            if (verbose) {
                var preDemandMatrix = new Dictionary<(string, string), double>();
                foreach (var pair in this.Topology.GetNodePairs()) {
                    if (demandMatrix.ContainsKey(pair)) {
                        preDemandMatrix[pair] = demandMatrix[pair];
                    } else {
                        preDemandMatrix[pair] = 0;
                    }
                }
                var preInterCluster = GetGap(optimalEncoder, heuristicEncoder, preDemandMatrix);
                Utils.logger("pre inter-cluster gap: " + preInterCluster.Item1, verbose);
            }

            Utils.logger("starting to model abstracted topology", verbose);
            var abstractTopology = new Topology();
            var clusterToNoteID = new Dictionary<Topology, string>();
            var clusterIDToCluster = new Dictionary<string, Topology>();
            var nodeID = 0;
            var clusterNumNodeList = new List<int>();
            Utils.logger("creating abstracted topology", verbose);
            foreach (var cluster in clusters) {
                abstractTopology.AddNode(nodeID.ToString());
                clusterToNoteID[cluster] = nodeID.ToString();
                clusterIDToCluster[nodeID.ToString()] = cluster;
                clusterNumNodeList.Add(cluster.GetAllNodes().Count());
                nodeID++;
            }

            var edgeToCapacity = new Dictionary<(string, string), double>();
            foreach (var edge in this.Topology.GetAllEdges()) {
                edgeToCapacity[(edge.Source, edge.Target)] = edge.Capacity;
            }

            var pairToDemandUB = new Dictionary<(string, string), double>();
            for (var cid1 = 0; cid1 < clusters.Count() - 1; cid1++) {
                for (var cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++) {
                    var cluster1 = clusters[cid1];
                    var cluster2 = clusters[cid2];
                    var cluster1Nodes = cluster1.GetAllNodes();
                    var cluster2Nodes = cluster2.GetAllNodes();
                    var nodeID1 = clusterToNoteID[cluster1];
                    var nodeID2 = clusterToNoteID[cluster2];
                    var cap1To2 = 0.0;
                    var cap2To1 = 0.0;
                    foreach (var node1 in cluster1Nodes) {
                        foreach (var node2 in cluster2Nodes) {
                            if (edgeToCapacity.ContainsKey((node1, node2))) {
                                cap1To2 += edgeToCapacity[(node1, node2)];
                            }
                            if (edgeToCapacity.ContainsKey((node2, node1))) {
                                cap2To1 += edgeToCapacity[(node2, node1)];
                            }
                        }
                    }
                    if (cap1To2 > 0) {
                        abstractTopology.AddEdge(nodeID1, nodeID2, cap1To2);
                        pairToDemandUB[(nodeID1, nodeID2)] = cluster1Nodes.Count() * cluster2Nodes.Count();
                        Utils.logger(
                            string.Format("abstract topology edge from {0} to {1} with cap {2}", nodeID1, nodeID2, cap1To2),
                            verbose);
                    }
                    if (cap2To1 > 0) {
                        abstractTopology.AddEdge(nodeID2, nodeID1, cap2To1);
                        pairToDemandUB[(nodeID2, nodeID1)] = cluster1Nodes.Count() * cluster2Nodes.Count();
                        Utils.logger(
                            string.Format("abstract topology edge from {1} to {0} with cap {2}", nodeID1, nodeID2, cap2To1),
                            verbose);
                    }
                }
            }

            var clusterPairToNumNodePairs = new Dictionary<(Topology, Topology), int>();
            for (int cid1 = 0; cid1 < clusters.Count(); cid1++) {
                for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++) {
                    int numPairs = clusters[cid1].GetAllNodes().Count() * clusters[cid2].GetAllNodes().Count();
                    clusterPairToNumNodePairs[(clusters[cid1], clusters[cid2])] = numPairs;
                    clusterPairToNumNodePairs[(clusters[cid2], clusters[cid1])] = numPairs;
                }
            }

            Utils.logger("computing updated demand list", verbose);
            var demandlvls = demandList.demandList;
            demandlvls.Add(0);
            var abstractDemandList = new Dictionary<(string, string), ISet<double>>();
            foreach (var ((cluster1, cluster2), numPairs) in clusterPairToNumNodePairs) {
                var perClusterPairAbstractDemandList = new HashSet<double>(demandlvls);
                for (int num1 = 1; num1 < numPairs; num1++) {
                    var newDemandsToAdd = new HashSet<double>();
                    foreach (var demand1 in demandlvls) {
                        foreach (var demand2 in perClusterPairAbstractDemandList) {
                            newDemandsToAdd.Add(demand1 + demand2);
                        }
                    }
                    perClusterPairAbstractDemandList = perClusterPairAbstractDemandList.Union(newDemandsToAdd).ToHashSet();
                }

                HashSet<double> finalClusterPairAbstractDemandList = null;
                if (numInterClusterQuantizations > 0) {
                    finalClusterPairAbstractDemandList = new HashSet<double>();
                    var numAggDemandlvls = perClusterPairAbstractDemandList.Count();
                    var perClusterDemandlvls = perClusterPairAbstractDemandList.ToList();
                    var alpha = Math.Pow(numAggDemandlvls, 1.0 / numInterClusterQuantizations);
                    for (int q = 0; q < numInterClusterQuantizations + 1; q++) {
                        finalClusterPairAbstractDemandList.Add(perClusterDemandlvls[Convert.ToInt32(Math.Pow(alpha, q))]);
                    }
                } else {
                    finalClusterPairAbstractDemandList = perClusterPairAbstractDemandList;
                }
                abstractDemandList[(clusterToNoteID[cluster1], clusterToNoteID[cluster2])] = finalClusterPairAbstractDemandList;
                Utils.logger(
                    string.Format(
                        "new demand list between abstract node {0} and {1} = {2}",
                        clusterToNoteID[cluster1],
                        clusterToNoteID[cluster2],
                        string.Join("_", finalClusterPairAbstractDemandList)),
                    verbose);
            }

            Utils.logger("Abstract topology with " + abstractTopology.GetAllNodes().Count() +
                    " nodes and " + abstractTopology.GetAllEdges().Count() + " edges", verbose);
            optimalEncoder.Solver.CleanAll();
            var adversarialInputGenerator = new AdversarialInputGenerator<TVar, TSolution>(abstractTopology, this.K, this.NumProcesses);
            var abstractResult = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder,
                    innerEncoding: innerEncoding, demandList: new PairwiseDemandList(abstractDemandList),
                    simplify: simplify, verbose: verbose, cleanUpSolver: false, perDemandUB: pairToDemandUB);

            Utils.logger("Assigning Demands randomly...", verbose);
            if (demandUB < 0) {
                demandUB = this.K * this.Topology.MaxCapacity();
            }

            var rng = new Random();
            foreach (var pair in abstractTopology.GetNodePairs()) {
                var nodeCluster1 = clusterIDToCluster[pair.Item1].GetAllNodes().ToList();
                var nodeCluster2 = clusterIDToCluster[pair.Item2].GetAllNodes().ToList();
                var remDemand = abstractResult.Item1.Demands[pair];
                Utils.logger(
                    string.Format("demand from {0} to {1} in abstract topo = {2}", pair.Item1, pair.Item2, remDemand),
                    verbose);
                while (remDemand > 0.001) {
                    var node1 = nodeCluster1[rng.Next(nodeCluster1.Count())];
                    var node2 = nodeCluster2[rng.Next(nodeCluster2.Count())];
                    var demand = demandList.GetRandomNonZeroDemandForPair(rng, node1, node2);
                    if (demandMatrix.ContainsKey((node1, node2)) || remDemand < demand) {
                        continue;
                    }
                    demandMatrix[(node1, node2)] = demand;
                    Utils.logger(
                        string.Format("adding demand from {0} to {1} = {2}", node1, node2, demand),
                        verbose);
                    remDemand -= demand;
                }
            }

            foreach (var pair in this.Topology.GetNodePairs()) {
                if (!demandMatrix.ContainsKey(pair)) {
                    demandMatrix[pair] = 0;
                }
            }
            var output = GetGap(optimalEncoder, heuristicEncoder, demandMatrix);
            Utils.logger("Final gap: " + output.Item1, verbose);
            return output.Item2;
        }

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// <param name="optimalEncoder">The optimal encoder.</param>
        /// <param name="heuristicEncoder">The heuristic encoder.</param>
        /// <param name="minDifference">The minimum difference.</param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        /// <param name="innerEncoding">The method for encoding the inner problem.</param>
        /// <param name="demandList">the quantized list of demands, will only use if method=PrimalDual.</param>
        /// <param name="simplify">will simplify the final solution if this parameter is true.</param>,
        public (OptimizationSolution, OptimizationSolution) FindOptimalityGapAtLeast(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double minDifference,
            double demandUB = -1,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT,
            GenericDemandList demandList = null,
            bool simplify = false)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (innerEncoding == InnerEncodingMethodChoice.PrimalDual & demandList == null)
            {
                throw new Exception("should provide the demand list if inner encoding method is primal dual.");
            }
            var solver = optimalEncoder.Solver;
            solver.CleanAll();

            CreateDemandVariables(solver, innerEncoding, demandList);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, this.DemandVariables, numProcesses: this.NumProcesses);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, this.DemandVariables, numProcesses: this.NumProcesses);

            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            EnsureDemandUB(solver, demandUB);

            var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
            solver.SetObjective(objective);

            // solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                // new Term<TVar>(-1, objectiveVariable), new Term<TVar>(minDifference)));

            // var solution = solver.Maximize(solver.CreateVariable("dummy"));
            var solution = solver.CheckFeasibility(minDifference);
            solution = SimplifyAdversarialInputs(simplify, optimalEncoder, heuristicEncoder, solution, objective);
            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));
            /* if (!solution.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (Opt):");
            optimalEncoder.DisplaySolution(solution);
            Console.WriteLine();
            Console.WriteLine("Optimal (Pop):");
            heuristicEncoder.DisplaySolution(solution); */
        }

        private void EnsureDemandUB(
            ISolver<TVar, TSolution> solver,
            double demandUB)
        {
            if (demandUB < 0) {
                demandUB = double.PositiveInfinity;
            }
            demandUB = Math.Min(this.Topology.MaxCapacity() * this.K, demandUB);
            foreach (var (pair, variable) in this.DemandVariables)
            {
                // var heuristicVariable = heuristicEncoding.DemandVariables[pair];
                // solver.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1, heuristicVariable)));
                var poly = variable.Copy();
                poly.Add(new Term<TVar>(-1 * demandUB));
                solver.AddLeqZeroConstraint(poly);
                // solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1 * demandUB), new Term<TVar>(1, heuristicVariable)));
            }
        }

        private void EnsureDemandUB(
            ISolver<TVar, TSolution> solver,
            IDictionary<(string, string), double> demandUB)
        {
            foreach (var (pair, perDemandUb) in demandUB) {
                var ub = perDemandUb;
                if (ub < 0) {
                    ub = double.PositiveInfinity;
                }
                ub = Math.Min(this.Topology.MaxCapacity() * this.K, ub);
                var poly = DemandVariables[pair].Copy();
                poly.Add(new Term<TVar>(-1 * ub));
                solver.AddLeqZeroConstraint(poly);
            }
        }

        private void AddSingleDemandEquality(
            ISolver<TVar, TSolution> solver,
            (string, string) pair,
            double demand)
        {
            var poly = this.DemandVariables[pair].Copy();
            poly.Add(new Term<TVar>(-1 * demand));
            solver.AddEqZeroConstraint(poly);
        }

        private void AddSingleDemandUB(
            ISolver<TVar, TSolution> solver,
            (string, string) pair,
            double demand)
        {
            var poly = this.DemandVariables[pair].Copy();
            poly.Add(new Term<TVar>(-1 * demand));
            solver.AddLeqZeroConstraint(poly);
        }

        private void EnsureDemandEquality(
            ISolver<TVar, TSolution> solver,
            IDictionary<(string, string), double> constrainedDemands)
        {
            if (constrainedDemands == null) {
                return;
            }
            foreach (var (pair, demand) in constrainedDemands) {
                AddSingleDemandEquality(solver, pair, demand);
            }
        }

        private void CreateDemandVariables(ISolver<TVar, TSolution> solver, InnerEncodingMethodChoice innerEncoding, IDemandList demandList) {
            this.DemandVariables = new Dictionary<(string, string), Polynomial<TVar>>();
            Console.WriteLine("[INFO] In total " + this.Topology.GetNodePairs().Count() + " pairs");
            // var sumAllAuxVars = new Polynomial<TVar>(new Term<TVar>(-0.01 * this.Topology.GetNodePairs().Count()));
            // var sumAllAuxVars = new Polynomial<TVar>(new Term<TVar>(-5));
            foreach (var pair in this.Topology.GetNodePairs())
            {
                switch (innerEncoding) {
                    case InnerEncodingMethodChoice.KKT:
                        this.DemandVariables[pair] = new Polynomial<TVar>(new Term<TVar>(1, solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2)));
                        break;
                    case InnerEncodingMethodChoice.PrimalDual:
                        var demands = demandList.GetDemandsForPair(pair.Item1, pair.Item2);
                        demands.Remove(0);
                        var axVariableConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                        var demandLvlEnforcement = new Polynomial<TVar>();
                        foreach (double demandlvl in demands) {
                            var demandAuxVar = solver.CreateVariable("aux_demand_" + pair.Item1 + "_" + pair.Item2, type: GRB.BINARY);
                            demandLvlEnforcement.Add(new Term<TVar>(demandlvl, demandAuxVar));
                            axVariableConstraint.Add(new Term<TVar>(1, demandAuxVar));
                            // sumAllAuxVars.Add(new Term<TVar>(1, demandAuxVar));
                        }
                        solver.AddLeqZeroConstraint(axVariableConstraint);
                        this.DemandVariables[pair] = demandLvlEnforcement;
                        break;
                    default:
                        throw new Exception("wrong method for inner problem encoder!");
                }
            }
            // solver.AddLeqZeroConstraint(sumAllAuxVars);
        }

        /// <summary>
        /// Finds an adversarial input that is within intervalConf of the maximum gap.
        /// </summary>
        /// <param name="optimalEncoder"> </param>
        /// <param name="heuristicEncoder"> </param>
        /// <param name="intervalConf"></param>
        /// <param name="startGap"></param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        /// <param name="innerEncoding">The method for encoding the inner problem.</param>
        /// <param name="demandList">the quantized list of demands, will only use if method=PrimalDual.</param>
        public (OptimizationSolution, OptimizationSolution) FindMaximumGapInterval(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double intervalConf,
            double startGap,
            double demandUB = double.PositiveInfinity,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT,
            GenericDemandList demandList = null)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("[ERROR] Solver mismatch....");
            }

            if (startGap <= 0.001)
            {
                throw new System.Exception("[ERROR] Starting Gap too small...");
            }
            var solver = optimalEncoder.Solver;
            solver.CleanAll();

            CreateDemandVariables(solver, innerEncoding, demandList);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, this.DemandVariables, numProcesses: this.NumProcesses);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, this.DemandVariables, numProcesses: this.NumProcesses);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            EnsureDemandUB(solver, demandUB);

            // setting demand as objective
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
            solver.SetObjective(objective);

            double lbGap = 0;
            double ubGap = startGap;
            bool found_infeas = false;
            TSolution solution;
            while (!found_infeas) {
                Console.WriteLine("************** Current Gap Interval (Phase 1) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("nxt=" + ubGap);
                Console.WriteLine("**************************************************");
                try {
                    solution = solver.CheckFeasibility(ubGap);
                    lbGap = ubGap;
                    ubGap = ubGap * 2;
                }
                catch (InfeasibleOrUnboundSolution) {
                    found_infeas = true;
                }
                // solver.ChangeConstraintRHS(nameLBConst, -1 * ubGap);
            }

            while ((ubGap - lbGap) / lbGap > intervalConf) {
                double midGap = (lbGap + ubGap) / 2;
                Console.WriteLine("************** Current Gap Interval (Phase 2) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("ub=" + ubGap);
                Console.WriteLine("nxt=" + midGap);
                Console.WriteLine("**************************************************");
                // solver.ChangeConstraintRHS(nameLBConst, -1 * midGap);
                try {
                    solution = solver.CheckFeasibility(midGap);
                    lbGap = midGap;
                }
                catch (InfeasibleOrUnboundSolution) {
                    ubGap = midGap;
                }
            }
            Console.WriteLine("************** Final Gap Interval ****************");
            Console.WriteLine("lb=" + lbGap);
            Console.WriteLine("ub=" + ubGap);
            Console.WriteLine("**************************************************");
            // solver.ChangeConstraintRHS(nameLBConst, -1 * lbGap);
            solution = solver.CheckFeasibility(lbGap);
            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));
        }

        private (double, (OptimizationSolution, OptimizationSolution)) GetGap (
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            Dictionary<(string, string), double> demands)
        {
            // solving the hueristic for the demand
            heuristicEncoder.Solver.CleanAll();
            var encodingHeuristic = heuristicEncoder.Encoding(this.Topology, demandEqualityConstraints: demands,
                    noAdditionalConstraints: true, numProcesses: this.NumProcesses);
            var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
            var optimizationSolutionHeuristic = heuristicEncoder.GetSolution(solverSolutionHeuristic);

            // solving the optimal for the demand
            optimalEncoder.Solver.CleanAll();
            var encodingOptimal = optimalEncoder.Encoding(this.Topology, demandEqualityConstraints: demands,
                    noAdditionalConstraints: true, numProcesses: this.NumProcesses);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(encodingOptimal.MaximizationObjective);
            var optimizationSolutionOptimal = optimalEncoder.GetSolution(solverSolutionOptimal);
            double currGap = optimizationSolutionOptimal.TotalDemandMet - optimizationSolutionHeuristic.TotalDemandMet;
            return (currGap, (optimizationSolutionOptimal, optimizationSolutionHeuristic));
        }

        /// <summary>
        /// Generate some random inputs and takes the max gap as the adversary.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) RandomAdversarialGenerator(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTrials,
            double demandUB,
            int seed = 0,
            bool verbose = false,
            bool storeProgress = false,
            string logPath = null,
            double timeout = Double.PositiveInfinity)
        {
            // if (optimalEncoder.Solver == heuristicEncoder.Solver) {
            //     throw new Exception("solvers should be different for random generator!!!");
            // }
            if (numTrials < 1) {
                throw new Exception("num trials for random generator should be positive but got " + numTrials + "!!");
            }
            if (demandUB <= 0) {
                demandUB = this.Topology.MaxCapacity() * this.K;
            }
            if (storeProgress) {
                if (logPath == null) {
                    throw new Exception("should specify logPath if storeprogress = true!");
                } else {
                    logPath = Utils.CreateFile(logPath, removeIfExist: true, addFid: true);
                }
            }
            double currMaxGap = 0;
            OptimizationSolution zero_solution = new OptimizationSolution {
                    TotalDemandMet = 0,
                    Demands = new Dictionary<(string, string), double> { },
                    Flows = new Dictionary<(string, string), double> { },
                    FlowsPaths = new Dictionary<string[], double> { },
                };
            (OptimizationSolution, OptimizationSolution) worstResult = (zero_solution, zero_solution);
            Random rng = new Random(seed);
            double timeout_ms = timeout * 1000;
            Stopwatch timer = Stopwatch.StartNew();
            // Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

            foreach (int i in Enumerable.Range(0, numTrials)) {
                Dictionary<(string, string), double> demands = new Dictionary<(string, string), double>();
                // initializing some random demands
                foreach (var pair in this.Topology.GetNodePairs()) {
                    demands[pair] = rng.NextDouble() * demandUB;
                }
                var (currGap, result) = GetGap(optimalEncoder, heuristicEncoder, demands);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("======== try " + i + " found a solution with gap " + currGap, verbose);
                if (currGap > currMaxGap) {
                    Utils.WriteToConsole("updating the max gap from " + currMaxGap + " to " + currGap, verbose);
                    currMaxGap = currGap;
                    worstResult = result;
                } else {
                    Utils.WriteToConsole("the max gap remains the same =" + currMaxGap, verbose);
                }
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                if (timer.ElapsedMilliseconds > timeout_ms) {
                    break;
                }
            }
            return worstResult;
        }

        private double GaussianRandomNumberGenerator(Random rng, double mean, double stddev) {
            // Box–Muller_transform
            double rnd1 = 1.0 - rng.NextDouble();
            double rnd2 = 1.0 - rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(rnd1)) * Math.Sin(2.0 * Math.PI * rnd2);
            return mean + stddev * randStdNormal;
        }

        /// <summary>
        /// Using some hill climbers to generate some adversary inputs.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) HillClimbingAdversarialGenerator(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTrials,
            int numNeighbors,
            double demandUB,
            double stddev,
            int seed = 0,
            bool verbose = false,
            bool storeProgress = false,
            string logPath = null,
            double timeout = Double.PositiveInfinity)
        {
            if (numTrials < 1) {
                throw new Exception("num trials for hill climber should be positive but got " + numTrials + "!!");
            }
            if (demandUB <= 0) {
                demandUB = this.Topology.MaxCapacity() * this.K;
            }
            if (storeProgress) {
                if (logPath == null) {
                    throw new Exception("should specify logPath if storeprogress = true!");
                } else {
                    logPath = Utils.CreateFile(logPath, removeIfExist: true, addFid: true);
                }
            }

            double currMaxGap = 0;
            OptimizationSolution zero_solution = new OptimizationSolution {
                    TotalDemandMet = 0,
                    Demands = new Dictionary<(string, string), double> { },
                    Flows = new Dictionary<(string, string), double> { },
                    FlowsPaths = new Dictionary<string[], double> { },
                };
            (OptimizationSolution, OptimizationSolution) worstResult = (zero_solution, zero_solution);
            Random rng = new Random(seed);
            double timeout_ms = timeout * 1000;
            Stopwatch timer = Stopwatch.StartNew();
            // Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

            bool timeoutReached = false;
            foreach (int i in Enumerable.Range(0, numTrials)) {
                Dictionary<(string, string), double> currDemands = new Dictionary<(string, string), double>();
                // initializing some random demands
                foreach (var pair in this.Topology.GetNodePairs()) {
                    currDemands[pair] = rng.NextDouble() * demandUB;
                }
                var (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
                Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + Math.Max(currGap, currMaxGap), storeProgress);
                bool localMax = true;
                do {
                    localMax = true;
                    foreach (int j in Enumerable.Range(0, numNeighbors)) {
                        // generating neighbor demands
                        Dictionary<(string, string), double> neighborDemands = new Dictionary<(string, string), double>();
                        foreach (var pair in this.Topology.GetNodePairs()) {
                            neighborDemands[pair] = Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev));
                        }
                        // finding gap for the neighbor
                        var (neighborGap, neighborResult) = GetGap(optimalEncoder, heuristicEncoder, neighborDemands);
                        // check if better advers input
                        if (neighborGap > currGap) {
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("======== try " + i + " neighbor " + j + " found a neighbor with gap " + neighborGap + " higher than " + currGap, verbose);
                            currDemands = neighborDemands;
                            currResult = neighborResult;
                            currGap = neighborGap;
                            localMax = false;
                        } else {
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("======== try " + i + " neighbor " + j + " has a lower gap " + neighborGap + " than curr gap " + currGap, verbose);
                        }
                        Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + Math.Max(currGap, currMaxGap), storeProgress);
                        if (timer.ElapsedMilliseconds > timeout_ms) {
                            timeoutReached = true;
                            break;
                        }
                    }
                } while (!localMax);

                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("======== try " + i + " found a local maximum with gap " + currGap, verbose);
                if (currGap > currMaxGap) {
                    Utils.WriteToConsole("updating the max gap from " + currMaxGap + " to " + currGap, verbose);
                    currMaxGap = currGap;
                    worstResult = currResult;
                } else {
                    Utils.WriteToConsole("the max gap remains the same =" + currMaxGap, verbose);
                }
                if (timeoutReached) {
                    break;
                }
            }
            return worstResult;
        }

        private Dictionary<(string, string), double> getRandomDemand(Random rng, double demandUB)
        {
            Dictionary<(string, string), double> currDemands = new Dictionary<(string, string), double>();
            // initializing some random demands
            foreach (var pair in this.Topology.GetNodePairs()) {
                currDemands[pair] = rng.NextDouble() * demandUB;
            }
            return currDemands;
        }

        /// <summary>
        /// Using Simulated Annealing to generate adversarial inputs.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) SimulatedAnnealing(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTmpSteps,
            int numNeighbors,
            double demandUB,
            double stddev,
            double initialTmp,
            double tmpDecreaseFactor,
            int numNoIncreaseToReset = -1,
            double NoChangeRelThreshold = 0.01,
            int seed = 0,
            bool verbose = false,
            bool storeProgress = false,
            string logPath = null,
            double timeout = Double.PositiveInfinity)
        {
            if (numTmpSteps < 1) {
                throw new Exception("num temperature steps should be positive but got " + numTmpSteps + "!!");
            }
            if (initialTmp <= 0) {
                throw new Exception("initial temperature should be positive but got " + initialTmp + "!!");
            }
            if (tmpDecreaseFactor >= 1 | tmpDecreaseFactor < 0) {
                throw new Exception("temperature decrease factor should be between 0 and 1 but got " + tmpDecreaseFactor + "!!");
            }
            if (demandUB <= 0) {
                demandUB = this.Topology.MaxCapacity() * this.K;
            }
            if (storeProgress) {
                if (logPath == null) {
                    throw new Exception("should specify logPath if storeprogress = true!");
                } else {
                    logPath = Utils.CreateFile(logPath, removeIfExist: true, addFid: true);
                }
            }
            if (numNoIncreaseToReset == -1) {
                numNoIncreaseToReset = numNeighbors * 2;
            }

            double currTmp = initialTmp;
            Random rng = new Random(seed);
            bool timeoutReached = false;
            var timeout_ms = timeout * 1000;
            Stopwatch timer = Stopwatch.StartNew();
            Dictionary<(string, string), double> currDemands = getRandomDemand(rng, demandUB);
            var (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
            (OptimizationSolution, OptimizationSolution) worstResult = currResult;
            double currMaxGap = currGap;
            double restartMaxGap = currGap;
            Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

            int noIncrease = 0;
            foreach (int p in Enumerable.Range(0, numTmpSteps)) {
                foreach (int Mp in Enumerable.Range(0, numNeighbors)) {
                    // generating neighbor demands
                    Dictionary<(string, string), double> neighborDemands = new Dictionary<(string, string), double>();
                    foreach (var pair in this.Topology.GetNodePairs()) {
                        neighborDemands[pair] = Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev));
                    }
                    // finding gap for the neighbor
                    var (neighborGap, neighborResult) = GetGap(optimalEncoder, heuristicEncoder, neighborDemands);
                    Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

                    // check if better advers input
                    if (neighborGap > currGap) {
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("======== try " + p + " neighbor " + Mp + " found a neighbor with gap " + neighborGap + " higher than " + currGap +
                            " max gap = " + currMaxGap, verbose);
                        currDemands = neighborDemands;
                        currResult = neighborResult;
                        currGap = neighborGap;
                        if (neighborGap > currMaxGap) {
                            worstResult = currResult;
                            currMaxGap = currGap;
                        }
                    } else {
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("======== try " + p + " neighbor " + Mp + " has a lower gap " + neighborGap + " than curr gap " + currGap +
                            " max gap = " + currMaxGap, verbose);
                        double currProbability = Math.Exp((neighborGap - currGap) / currTmp);
                        double randomNumber = rng.NextDouble();
                        // Console.WriteLine("current temperature is " + currTmp);
                        // Console.WriteLine("current gap difference is " + (neighborGap - currGap));
                        // Console.WriteLine("current probability is " + currProbability + " and the random number is " + randomNumber);
                        if (randomNumber <= currProbability) {
                            // Console.WriteLine("accepting the lower gap");
                            currDemands = neighborDemands;
                            currResult = neighborResult;
                            currGap = neighborGap;
                        }
                    }
                    Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                    if (timer.ElapsedMilliseconds > timeout_ms) {
                        timeoutReached = true;
                        break;
                    }
                    if ((currGap - restartMaxGap) / restartMaxGap > NoChangeRelThreshold) {
                        noIncrease = 0;
                        restartMaxGap = currGap;
                    } else {
                        noIncrease += 1;
                    }
                }
                if (timeoutReached) {
                    break;
                }
                currTmp = currTmp * tmpDecreaseFactor;
                // reset the initial point if no increase in numNoIncreaseToReset iterations
                if (noIncrease > numNoIncreaseToReset) {
                    currDemands = getRandomDemand(rng, demandUB);
                    (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
                    if (currGap > currMaxGap) {
                        worstResult = currResult;
                        currMaxGap = currGap;
                    }
                    currTmp = initialTmp;
                    noIncrease = 0;
                    restartMaxGap = currGap;
                }
            }
            Console.WriteLine("final max gap is " + currMaxGap);
            return worstResult;
        }
    }
}
