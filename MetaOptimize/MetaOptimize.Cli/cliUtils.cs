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
                Topology topology, Heuristic h, int numPaths, int numSlices = 1, double demandPinningThreshold = 0,
                IDictionary<(string, string), int> partition = null, int numSamples = 1, IList<IDictionary<(string, string), int>> partitionsList = null)
        {
            IEncoder<TVar, TSolution> heuristicEncoder;
            switch (h)
            {
                case Heuristic.Pop:
                    if (partition == null) {
                        partition = topology.RandomPartition(numSlices);
                    }
                    Console.WriteLine("Exploring pop heuristic");
                    heuristicEncoder = new PopEncoder<TVar, TSolution>(solver, topology, numPaths, numSlices, partition);
                    break;
                case Heuristic.DemandPinning:
                    Console.WriteLine("Exploring demand pinning heuristic");
                    heuristicEncoder = new DemandPinningEncoder<TVar, TSolution>(solver, topology, numPaths, demandPinningThreshold);
                    break;
                case Heuristic.ExpectedPop:
                    if (partitionsList == null) {
                        partitionsList = new List<IDictionary<(string, string), int>>();
                        for (int i = 0; i < numSamples; i++) {
                            partitionsList.Add(topology.RandomPartition(numSlices));
                        }
                    }
                    Console.WriteLine("Exploring the expected pop heuristic");
                    heuristicEncoder = new ExpectedPopEncoder<TVar, TSolution>(solver, topology, numPaths, numSamples, numSlices, partitionsList);
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
            Topology topology, int numPaths, double threshold)
        {
            solver.CleanAll();
            var (heuristicEncoder, _, _) = getHeuristic<TVar, TSolution>(solver, topology, Heuristic.DemandPinning,
                numPaths, demandPinningThreshold: threshold);
            var optimalEncoder = new OptimalEncoder<TVar, TSolution>(solver, topology, numPaths);
            var adversarialInputGenerator = new AdversarialInputGenerator<TVar, TSolution>(topology, numPaths);
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
            var heuristicEncoding = heuristicEncoder.Encoding(demandEqualityConstraints: demands, noAdditionalConstraints: true);
            var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(heuristicEncoding.MaximizationObjective);
            var optimizationSolutionHeuristic = heuristicEncoder.GetSolution(solverSolutionHeuristic);
            var heuristicDemandMet = optimizationSolutionHeuristic.TotalDemandMet;
            var optimalEncoder = new OptimalEncoder<TVar, TSolution>(solver, topology, numPaths);
            var optimalEncoding = optimalEncoder.Encoding(demandEqualityConstraints: demands, noAdditionalConstraints: true);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
            var optimizationSolutionOptimal = optimalEncoder.GetSolution(solverSolutionHeuristic);
            var optimalDemandMet = optimizationSolutionOptimal.TotalDemandMet;
            return (optimalDemandMet, heuristicDemandMet);
        }
    }
}