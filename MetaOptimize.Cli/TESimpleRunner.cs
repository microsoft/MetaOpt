using System.Diagnostics;
using Gurobi;

namespace MetaOptimize.Cli
{
    /// <summary>
    /// Traffic Engineering runner - matches original TEMain behavior.
    /// </summary>
    public static class TESimpleRunner
    {
        /// <summary>
        /// Runs Traffic Engineering optimization.
        /// Uses CliUtils for topology loading and heuristic setup (same as original ssMain).
        /// </summary>
        public static void Run(CliArgs args)
        {
            var topologyFile = args.Get("--topologyFile", "simple.json");
            var heuristic = args.Get("--heuristic", "Pop");
            var paths = args.GetInt("--paths", 1);
            var verbose = args.GetBool("--verbose", false);
            var timeout = args.GetDouble("--timeout", 1000);
            var numThreads = args.GetInt("--numThreads", 1);
            var popSlices = args.GetInt("--popSlices", 2);

            Console.WriteLine($"Topology File: {topologyFile}");
            Console.WriteLine($"Heuristic: {heuristic}");
            Console.WriteLine($"Paths per pair: {paths}");

            // Load topology from JSON (simple loading, not CliUtils)
            var topology = ReadTopologyFromFile(topologyFile);

            // Create solver
            var solver = new GurobiSOS(
                timeout: timeout,
                verbose: Convert.ToInt32(verbose),
                numThreads: numThreads);

            // Create optimal encoder
            var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, maxNumPaths: paths);

            // Create heuristic encoder
            IEncoder<GRBVar, GRBModel> heuristicEncoder;
            IDictionary<(string, string), int> partition = null;

            switch (heuristic)
            {
                case "Pop":
                    partition = topology.RandomPartition(popSlices);
                    heuristicEncoder = new PopEncoder<GRBVar, GRBModel>(
                        solver, maxNumPaths: paths, numPartitions: popSlices, demandPartitions: partition);
                    break;
                case "DemandPinning":
                    var dpThreshold = args.GetDouble("--dpThreshold", 0.5);
                    heuristicEncoder = new DirectDemandPinningEncoder<GRBVar, GRBModel>(
                        solver, k: paths, threshold: dpThreshold);
                    break;
                default:
                    throw new Exception($"Unsupported heuristic: {heuristic}");
            }

            // Create adversarial generator
            var adversarialGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, maxNumPaths: paths);

            // Run optimization - SIMPLE call like original TEMain
            var timer = Stopwatch.StartNew();
            var (optimalSolution, heuristicSolution) = adversarialGenerator.MaximizeOptimalityGap(
                optimalEncoder, heuristicEncoder);
            timer.Stop();

            // Display results
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(heuristicSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");

            var optimal = optimalSolution.MaxObjective;
            var heuristicObj = heuristicSolution.MaxObjective;
            Console.WriteLine($"optimalG={optimal}, heuristicG={heuristicObj}, time={timer.ElapsedMilliseconds}ms");

            // Validation - like original TEMain
            var demands = new Dictionary<(string, string), double>(optimalSolution.Demands);

            var optGSolver = new GurobiSOS();
            var optimalEncoderG = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(optGSolver, maxNumPaths: paths);

            var popGSolver = new GurobiSOS();
            IEncoder<GRBVar, GRBModel> heuristicEncoderG;

            switch (heuristic)
            {
                case "Pop":
                    heuristicEncoderG = new PopEncoder<GRBVar, GRBModel>(
                        popGSolver, maxNumPaths: paths, numPartitions: popSlices, demandPartitions: partition);
                    break;
                case "DemandPinning":
                    var dpThreshold = args.GetDouble("--dpThreshold", 0.5);
                    heuristicEncoderG = new DirectDemandPinningEncoder<GRBVar, GRBModel>(
                        popGSolver, k: paths, threshold: dpThreshold);
                    break;
                default:
                    throw new Exception($"Unsupported heuristic: {heuristic}");
            }

            Utils.checkSolution(topology, heuristicEncoderG, optimalEncoderG, heuristicObj, optimal, demands, "gurobiCheck");
        }

        private static Topology ReadTopologyFromFile(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var topologiesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Topologies"));
            var filePath = Path.Combine(topologiesDir, fileName);

            if (!File.Exists(filePath))
            {
                topologiesDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Topologies"));
                filePath = Path.Combine(topologiesDir, fileName);
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Topology file not found: {fileName}");
            }

            Console.WriteLine($"Loading topology from: {filePath}");

            var json = File.ReadAllText(filePath);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<TopologyJson>(json);

            var topology = new Topology();

            foreach (var node in data.Nodes)
            {
                topology.AddNode(node.Id.ToString());
            }

            foreach (var link in data.Links)
            {
                topology.AddEdge(link.Source.ToString(), link.Target.ToString(), capacity: link.Capacity);
            }

            Console.WriteLine($"Loaded: {data.Nodes.Count} nodes, {data.Links.Count} edges");
            return topology;
        }

        private class TopologyJson
        {
            public List<NodeJson> Nodes { get; set; } = new ();
            public List<LinkJson> Links { get; set; } = new ();
        }

        private class NodeJson
        {
            [Newtonsoft.Json.JsonProperty("id")]
            public object Id { get; set; }
        }

        private class LinkJson
        {
            [Newtonsoft.Json.JsonProperty("source")]
            public object Source { get; set; }
            [Newtonsoft.Json.JsonProperty("target")]
            public object Target { get; set; }
            [Newtonsoft.Json.JsonProperty("capacity")]
            public double Capacity { get; set; }
        }
    }
}