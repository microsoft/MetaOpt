namespace MetaOptimize.Cli {
    using System;
    using System.IO;
    /// <summary>
    /// Implements a utility function with some .
    /// </summary>
    public static class CliUtils {
        /// <summary>
        /// Returns the heuristic encoder, partition and partitionlist
        /// based on inputs.
        /// </summary>
        public static (IEncoder<TVar, TSolution>, IDictionary<(string, string), int>, IList<IDictionary<(string, string), int>>) getHeuristic<TVar, TSolution>(
                ISolver<TVar, TSolution> solver,
                Topology topology, Heuristic h, int numPaths, int numSlices = -1, double demandPinningThreshold = -1,
                IDictionary<(string, string), int> partition = null, int numSamples = -1, IList<IDictionary<(string, string), int>> partitionsList = null,
                double partitionSensitivity = -1, bool DirectEncoder = false)
        {
            IEncoder<TVar, TSolution> heuristicEncoder;
            switch (h)
            {
                case Heuristic.Pop:
                    if (partition == null) {
                        partition = topology.RandomPartition(numSlices);
                    }
                    Console.WriteLine("Exploring pop heuristic");
                    if (DirectEncoder) {
                        heuristicEncoder = new TEOptimalEncoder<TVar, TSolution>(solver, numPaths);
                        throw new Exception("should verify the above implementation...");
                    } else {
                        heuristicEncoder = new PopEncoder<TVar, TSolution>(solver, numPaths, numSlices, partition, partitionSensitivity: partitionSensitivity);
                    }
                    break;
                case Heuristic.DemandPinning:
                    Console.WriteLine("Exploring demand pinning heuristic");
                    if (DirectEncoder) {
                        Console.WriteLine("Direct DP");
                        heuristicEncoder = new DirectDemandPinningEncoder<TVar, TSolution>(solver, numPaths, demandPinningThreshold);
                    } else {
                        Console.WriteLine("Indirect DP");
                        heuristicEncoder = new DemandPinningEncoder<TVar, TSolution>(solver, numPaths, demandPinningThreshold);
                    }
                    break;
                case Heuristic.ExpectedPop:
                    if (partitionsList == null) {
                        partitionsList = new List<IDictionary<(string, string), int>>();
                        for (int i = 0; i < numSamples; i++) {
                            partitionsList.Add(topology.RandomPartition(numSlices));
                        }
                    }
                    Console.WriteLine("Exploring the expected pop heuristic");
                    heuristicEncoder = new ExpectedPopEncoder<TVar, TSolution>(solver, numPaths, numSamples, numSlices, partitionsList);
                    break;
                default:
                    throw new Exception("No heuristic selected.");
            }
            return (heuristicEncoder, partition, partitionsList);
        }

        /// <summary>
        /// Solves the adversarial input for demand pinning heuristic
        /// with specified parameters.
        /// </summary>
        public static (double, double, IDictionary<(string, string), double>) maximizeOptimalityGapDemandPinning<TVar, TSolution>(ISolver<TVar, TSolution> solver,
            Topology topology, int numPaths, double threshold, int numProcessors)
        {
            solver.CleanAll();
            var (heuristicEncoder, _, _) = getHeuristic<TVar, TSolution>(solver, topology, Heuristic.DemandPinning,
                numPaths, demandPinningThreshold: threshold);
            var optimalEncoder = new TEOptimalEncoder<TVar, TSolution>(solver, numPaths);
            var adversarialInputGenerator = new AdversarialInputGenerator<TVar, TSolution>(topology, numPaths, numProcessors);
            (OptimizationSolution, OptimizationSolution) result =
                adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder);
            double optimal = result.Item1.TotalDemandMet;
            double heuristic = result.Item2.TotalDemandMet;
            var demands = result.Item1.Demands;
            return (optimal, heuristic, demands);
        }

        /// <summary>
        /// get optimal and deamnd pinning total flow for a given demand.
        /// </summary>
        public static (double, double) getOptimalDemandPinningTotalDemand<TVar, TSolution>(ISolver<TVar, TSolution> solver, Dictionary<(string, string), double> demands,
            Topology topology, int numPaths, double threshold)
        {
            solver.CleanAll();
            var (heuristicEncoder, _, _) = getHeuristic<TVar, TSolution>(solver, topology, Heuristic.DemandPinning,
                numPaths, demandPinningThreshold: threshold);
            var heuristicEncoding = heuristicEncoder.Encoding(topology, demandEqualityConstraints: demands, noAdditionalConstraints: true);
            var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(heuristicEncoding.MaximizationObjective);
            var optimizationSolutionHeuristic = heuristicEncoder.GetSolution(solverSolutionHeuristic);
            var heuristicDemandMet = optimizationSolutionHeuristic.TotalDemandMet;
            var optimalEncoder = new TEOptimalEncoder<TVar, TSolution>(solver, numPaths);
            var optimalEncoding = optimalEncoder.Encoding(topology, demandEqualityConstraints: demands, noAdditionalConstraints: true);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
            var optimizationSolutionOptimal = optimalEncoder.GetSolution(solverSolutionOptimal);
            var optimalDemandMet = optimizationSolutionOptimal.TotalDemandMet;
            return (optimalDemandMet, heuristicDemandMet);
        }

        /// <summary>
        /// find interesting gap for expected pop.
        /// </summary>
        public static void findGapExpectedPopAdversarialDemandOnIndependentPartitions<TVar, TSolution>(CliOptions opts, Topology topology,
                Dictionary<(string, string), double> demands, double optimal)
        {
                var newSolver = (ISolver<TVar, TSolution>)new GurobiBinary(timeout: 3600, verbose: 0, timeToTerminateNoImprovement: 60);
                // Console.WriteLine($"gap for each sample for Expected Pop:");
                // for (int i = 0; i < opts.NumRandom; i++) {
                //     newSolver.CleanAll();
                //     var demandMet = result.Item2.TotalDemmandMetSample[i];
                //     var newOptimalEncoder = new OptimalEncoder<TVar, TSolution>(newSolver, topology, opts.Paths);
                //     var newHeuristicEncoder = new PopEncoder<TVar, TSolution>(newSolver, topology, k: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: partitionList[i]);
                //     var nresult = adversarialInputGenerator.MaximizeOptimalityGap(newOptimalEncoder, (IEncoder<TVar, TSolution>)newHeuristicEncoder, opts.DemandUB);
                //     var maxGap = nresult.Item1.TotalDemandMet - nresult.Item2.TotalDemandMet;
                //     Console.WriteLine("sample" + i + "-->" + "total demand = " + demandMet + " gap = " + (optimal - demandMet) + " max gap = " + maxGap);
                // }
                Console.WriteLine("trying on some random partitions to see the quality;");
                for (int i = 0; i < 100 - opts.NumRandom; i++) {
                    topology.RandomPartition(opts.PopSlices);
                }
                int num_r = 10;
                double sum_r = 0;
                var randomPartitionList = new List<IDictionary<(string, string), int>>();
                for (int i = 0; i < num_r; i++) {
                    newSolver.CleanAll();
                    var newPartition = topology.RandomPartition(opts.PopSlices);
                    randomPartitionList.Add(newPartition);
                    var newHeuristicEncoder = new PopEncoder<TVar, TSolution>(newSolver, k: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: newPartition);
                    var encodingHeuristic = newHeuristicEncoder.Encoding(topology, demandEqualityConstraints: demands, noAdditionalConstraints: true);
                    var solverSolutionHeuristic = newHeuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
                    var optimizationSolutionHeuristic = newHeuristicEncoder.GetSolution(solverSolutionHeuristic);
                    var demandMet = optimizationSolutionHeuristic.TotalDemandMet;
                    // var newOptimalEncoder = new OptimalEncoder<TVar, TSolution>(newSolver, topology, opts.Paths);
                    // newHeuristicEncoder = new PopEncoder<TVar, TSolution>(newSolver, topology, k: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: newPartition);
                    // var nresult = adversarialInputGenerator.MaximizeOptimalityGap(newOptimalEncoder, (IEncoder<TVar, TSolution>)newHeuristicEncoder, opts.DemandUB);
                    // var maxGap = nresult.Item1.TotalDemandMet - nresult.Item2.TotalDemandMet;
                    // Console.WriteLine("random sample" + i + "-->" + "total demand = " + demandMet + " gap = " + (optimal - demandMet) + " max gap = " + maxGap);
                    Console.WriteLine("random sample" + i + "-->" + "total demand = " + demandMet + " gap = " + (optimal - demandMet));
                    sum_r += (optimal - demandMet);
                }
                Console.WriteLine("==================== avg gap on " + num_r + " random partitions: " + (sum_r / num_r));
                // Console.WriteLine("==================== trying all-to-all demand");
                // var all2allDemand = new Dictionary<(string, string), double>();
                // double demandub = topology.MaxCapacity() * opts.Paths;
                // if (opts.DemandUB > 0) {
                //     demandub = opts.DemandUB;
                // }
                // foreach (var pair in topology.GetNodePairs()) {
                //     all2allDemand[pair] = demandub;
                // }
                // num_r = 10;
                // sum_r = 0;
                // for (int i = 0; i < num_r; i++) {
                //     newSolver.CleanAll();
                //     var newPartition = randomPartitionList[i];
                //     var newHeuristicEncoder = new PopEncoder<TVar, TSolution>(newSolver, topology, k: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: newPartition);
                //     var encodingHeuristic = newHeuristicEncoder.Encoding(demandEqualityConstraints: all2allDemand, noAdditionalConstraints: true);
                //     var solverSolutionHeuristic = newHeuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
                //     var optimizationSolutionHeuristic = newHeuristicEncoder.GetSolution(solverSolutionHeuristic);
                //     var newOptimalEncoder = new OptimalEncoder<TVar, TSolution>(newSolver, topology, opts.Paths);
                //     var optimalEncoding = newOptimalEncoder.Encoding(demandEqualityConstraints: all2allDemand, noAdditionalConstraints: true);
                //     var solverSolutionOptimal = newOptimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
                //     var optimizationSolutionOptimal = newOptimalEncoder.GetSolution(solverSolutionOptimal);
                //     var demandMetHeuristic = optimizationSolutionHeuristic.TotalDemandMet;
                //     var demandMetOptimal = optimizationSolutionOptimal.TotalDemandMet;
                //     Console.WriteLine("random sample" + i + "-->" + "total demand: optimal = " + demandMetOptimal + " heuristic = " +
                //             demandMetHeuristic + " gap = " + (demandMetOptimal - demandMetHeuristic));
                //     sum_r += (demandMetOptimal - demandMetHeuristic);
                // }
                // Console.WriteLine("==================== avg gap all-to-all on " + num_r + " random partitions: " + (sum_r / num_r));
                // var distance = 2;
                // Console.WriteLine("========================= trying all nodes sending to other nodes with distance <= " + distance);
                // var distanceDemand = new Dictionary<(string, string), double>();
                // demandub = topology.MaxCapacity() * opts.Paths;
                // if (opts.DemandUB > 0) {
                //     demandub = opts.DemandUB;
                // }
                // foreach (var pair in topology.GetNodePairs()) {
                //     var shortestPaths = topology.ShortestKPaths(1, pair.Item1, pair.Item2);
                //     if (shortestPaths[0].Count() <= distance + 1) {
                //         distanceDemand[pair] = demandub;
                //     }
                // }
                // FillEmptyPairsWithZeroDemand(topology, distanceDemand);
                // num_r = 10;
                // sum_r = 0;
                // for (int i = 0; i < num_r; i++) {
                //     newSolver.CleanAll();
                //     var newPartition = randomPartitionList[i];
                //     var newHeuristicEncoder = new PopEncoder<TVar, TSolution>(newSolver, topology, k: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: newPartition);
                //     var encodingHeuristic = newHeuristicEncoder.Encoding(demandEqualityConstraints: distanceDemand, noAdditionalConstraints: true);
                //     var solverSolutionHeuristic = newHeuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
                //     var optimizationSolutionHeuristic = newHeuristicEncoder.GetSolution(solverSolutionHeuristic);
                //     var newOptimalEncoder = new OptimalEncoder<TVar, TSolution>(newSolver, topology, opts.Paths);
                //     var optimalEncoding = newOptimalEncoder.Encoding(demandEqualityConstraints: distanceDemand, noAdditionalConstraints: true);
                //     var solverSolutionOptimal = newOptimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
                //     var optimizationSolutionOptimal = newOptimalEncoder.GetSolution(solverSolutionOptimal);
                //     var demandMetHeuristic = optimizationSolutionHeuristic.TotalDemandMet;
                //     var demandMetOptimal = optimizationSolutionOptimal.TotalDemandMet;
                //     Console.WriteLine("random sample" + i + "-->" + "total demand: optimal = " + demandMetOptimal + " heuristic = " +
                //             demandMetHeuristic + " gap = " + (demandMetOptimal - demandMetHeuristic));
                //     sum_r += (demandMetOptimal - demandMetHeuristic);
                // }
                // Console.WriteLine("==================== avg gap all nodes sending to other nodes with distance <= " + distance +
                //     " on " + num_r + " random partitions: " + (sum_r / num_r));
                // var numFlowPerEdge = 2;
                // Console.WriteLine("========================= trying filling all edges with  " + numFlowPerEdge + " flows!!");
                // var flowPerEdgeDemand = new Dictionary<(string, string), double>();
                // var edgeToNumFlowMapping = new Dictionary<(string, string), double>();
                // foreach (var edge in topology.GetAllEdges()) {
                //     edgeToNumFlowMapping[(edge.Source, edge.Target)] = 0;
                // }
                // demandub = topology.MaxCapacity() * opts.Paths;
                // if (opts.DemandUB > 0) {
                //     demandub = opts.DemandUB;
                // }
                // var spl = 1;
                // var allFilled = true;
                // var addedDemand = false;
                // do {
                //     addedDemand = false;
                //     Console.WriteLine("====spl= " + spl.ToString());
                //     foreach (var pair in topology.GetNodePairs()) {
                //         var shortestPaths = topology.ShortestKPaths(1, pair.Item1, pair.Item2)[0];
                //         if (shortestPaths.Count() == spl + 1) {
                //             var foundValid = false;
                //             for (int n = 0; n < spl; n++) {
                //                 if (edgeToNumFlowMapping[(shortestPaths[n], shortestPaths[n + 1])] < numFlowPerEdge) {
                //                     foundValid = true;
                //                     break;
                //                 }
                //             }
                //             if (foundValid) {
                //                 addedDemand = true;
                //                 flowPerEdgeDemand[(pair)] = demandub;
                //                 for (int n = 0; n < spl; n++) {
                //                     edgeToNumFlowMapping[(shortestPaths[n], shortestPaths[n + 1])] += 1;
                //                 }
                //             }
                //         }
                //     }
                //     spl += 1;
                //     foreach (var edge in topology.GetAllEdges()) {
                //         if (edgeToNumFlowMapping[(edge.Source, edge.Target)] < numFlowPerEdge) {
                //             allFilled = false;
                //         }
                //     }
                // } while (!allFilled & addedDemand);
                // FillEmptyPairsWithZeroDemand(topology, flowPerEdgeDemand);
                // num_r = 10;
                // sum_r = 0;
                // for (int i = 0; i < num_r; i++) {
                //     newSolver.CleanAll();
                //     var newPartition = randomPartitionList[i];
                //     var newHeuristicEncoder = new PopEncoder<TVar, TSolution>(newSolver, topology, k: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: newPartition);
                //     var encodingHeuristic = newHeuristicEncoder.Encoding(demandEqualityConstraints: flowPerEdgeDemand, noAdditionalConstraints: true);
                //     var solverSolutionHeuristic = newHeuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
                //     var optimizationSolutionHeuristic = newHeuristicEncoder.GetSolution(solverSolutionHeuristic);
                //     var newOptimalEncoder = new OptimalEncoder<TVar, TSolution>(newSolver, topology, opts.Paths);
                //     var optimalEncoding = newOptimalEncoder.Encoding(demandEqualityConstraints: flowPerEdgeDemand, noAdditionalConstraints: true);
                //     var solverSolutionOptimal = newOptimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
                //     var optimizationSolutionOptimal = newOptimalEncoder.GetSolution(solverSolutionOptimal);
                //     var demandMetHeuristic = optimizationSolutionHeuristic.TotalDemandMet;
                //     var demandMetOptimal = optimizationSolutionOptimal.TotalDemandMet;
                //     Console.WriteLine("random sample" + i + "-->" + "total demand: optimal = " + demandMetOptimal + " heuristic = " +
                //             demandMetHeuristic + " gap = " + (demandMetOptimal - demandMetHeuristic));
                //     sum_r += (demandMetOptimal - demandMetHeuristic);
                // }
                // Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(edgeToNumFlowMapping, Newtonsoft.Json.Formatting.Indented));
                // Console.WriteLine("==================== avg gap all fix " + numFlowPerEdge + " flow per edge demands" +
                //     " on " + num_r + " random partitions: " + (sum_r / num_r));
        }
    }
}