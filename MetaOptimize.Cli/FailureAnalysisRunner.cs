using System.Diagnostics;
using Gurobi;
using MetaOptimize.FailureAnalysis;
using ZenLib;
using ZenLib.ModelChecking;

namespace MetaOptimize.Cli
{
    /// <summary>
    /// Non-generic entry point that dispatches to the correct generic implementation.
    /// </summary>
    public static class FailureAnalysisRunner
    {
        /// <summary>
        /// Generic implementation of Failure Analysis optimization.
        /// </summary>
        public static void Run(CliArgs args)
        {
            var solverChoice = args.Get("--solver", "Gurobi");
            var verbose = args.GetBool("--verbose", false);
            var timeout = args.GetDouble("--timeout", 1000);

            switch (solverChoice.ToLower())
            {
                case "gurobi":
                    FailureAnalysisRunnerImpl<GRBVar, GRBModel>.CreateSolver =
                        () => new GurobiSOS(verbose: Convert.ToInt32(verbose), timeout: timeout);
                    FailureAnalysisRunnerImpl<GRBVar, GRBModel>.Run(args);
                    break;
                case "zen":
                    FailureAnalysisRunnerImpl<Zen<Real>, ZenSolution>.CreateSolver =
                        () => new SolverZen();
                    FailureAnalysisRunnerImpl<Zen<Real>, ZenSolution>.Run(args);
                    break;
                default:
                    throw new Exception($"Unsupported solver: {solverChoice}");
            }
        }
    }

    /// <summary>
    /// Generic implementation of Failure Analysis optimization.
    /// </summary>
    internal sealed class FailureAnalysisRunnerImpl<TVar, TSolution>
    {
        internal static Func<ISolver<TVar, TSolution>> CreateSolver = null;

        /// <summary>
        /// Creates default topology for failure analysis testing.
        /// </summary>
        private static Topology CreateDefaultFailureTopology()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddEdge("a", "b", capacity: 10);
            topology.AddEdge("a", "c", capacity: 10);
            topology.AddEdge("b", "d", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);
            topology.AddEdge("a", "d", capacity: 5);
            topology.AddEdge("b", "c", capacity: 3);
            return topology;
        }

        /// <summary>
        /// Reads topology from JSON file.
        /// </summary>
        private static Topology ReadTopologyFromFile(string filePath)
        {
            Console.WriteLine($"Loading topology from: {filePath}");
            // TODO: Implement proper JSON topology loading
            var topology = new Topology();
            return topology;
        }

        /// <summary>
        /// Runs Failure Analysis optimization.
        /// </summary>
        internal static void Run(CliArgs args)
        {
            var useDefaultTopology = args.GetBool("--useDefaultTopology", true);
            var maxNumFailures = args.GetInt("--maxNumFailures", 1);
            var numExtraPaths = args.GetInt("--numExtraPaths", 1);
            var demandListStr = args.Get("--demandList", "0,5,10");
            var failureProbThreshold = args.GetDouble("--failureProbThreshold", 0.25);
            var scenarioProbThreshold = args.GetDouble("--scenarioProbThreshold", 0.0);
            var innerEncoding = args.Get("--innerEncoding", "PrimalDual");
            var verbose = args.GetBool("--verbose", false);

            Console.WriteLine($"Max Failures: {maxNumFailures}, Extra Paths: {numExtraPaths}");
            Console.WriteLine($"Failure Prob Threshold: {failureProbThreshold}");

            Topology topology;
            if (useDefaultTopology)
            {
                topology = CreateDefaultFailureTopology();
                Console.WriteLine("Using default test topology");
            }
            else
            {
                var topologyFile = args.Get("--topologyFile");
                topology = ReadTopologyFromFile(topologyFile);
                Console.WriteLine($"Loaded topology from: {topologyFile}");
            }

            var demands = new Dictionary<(string, string), double>
            {
                { ("a", "d"), 10 },
                { ("b", "d"), 5 },
                { ("a", "c"), 5 },
                { ("c", "d"), 0 },
                { ("a", "b"), 0 },
                { ("b", "c"), 0 },
            };

            var demandSet = new HashSet<double>(
                demandListStr.Split(',').Select(double.Parse));
            var demandList = new GenericList(demandSet);

            var probs = new Dictionary<(string, string), double>
            {
                { ("a", "d"), 0.3 },
                { ("b", "d"), 0.2 },
                { ("a", "c"), 0 },
                { ("a", "b"), 0 },
                { ("c", "d"), 0 },
                { ("b", "c"), 0 },
            };

            var solver = CreateSolver();

            var innerEncodingType = innerEncoding == "KKT"
                ? InnerRewriteMethodChoice.KKT
                : InnerRewriteMethodChoice.PrimalDual;

            var timer = Stopwatch.StartNew();

            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);

            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(
                optimalEncoder, optimalCutEncoder,
                innerEncoding: innerEncodingType,
                constrainedDemands: demands,
                maxNumFailures: maxNumFailures,
                demandList: demandList,
                numExtraPaths: numExtraPaths,
                lagFailureProbabilities: probs,
                failureProbThreshold: failureProbThreshold);

            timer.Stop();

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"Optimal objective: {optimalSol.MaxObjective}");
            Console.WriteLine($"Failure scenario objective: {failureSol.MaxObjective}");
            Console.WriteLine($"Gap: {optimalSol.MaxObjective - failureSol.MaxObjective}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('=', 60));

            if (verbose)
            {
                Console.WriteLine("\nOptimal Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    optimalSol, Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine("\nFailure Scenario Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    failureSol, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}