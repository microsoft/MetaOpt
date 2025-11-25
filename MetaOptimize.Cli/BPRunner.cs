using System.Diagnostics;
using Gurobi;
using ZenLib;

namespace MetaOptimize.Cli
{
    /// <summary>
    /// Main entry point for traffic engineering experiments.
    /// Executes adversarial optimization to find worst-case demand patterns for routing heuristics.
    /// </summary>
    /// <remarks>
    /// Flow: Load topology → Generate demand levels → Run adversarial optimization → Validate results.
    /// Finds demand patterns where heuristic routes significantly less than optimal.
    /// </remarks>
    public sealed class BPRunner
    {
        /// <summary>
        /// Runs Bin Packing optimization.
        /// Uses MainVBP logic (more complete than vbMain).
        /// </summary>
        public static void Run(CliArgs args)
        {
            var numBins = args.GetInt("--numBins", 6);
            var numDemands = args.GetInt("--numDemands", 9);
            var numDimensions = args.GetInt("--numDimensions", 2);
            var binCapacityStr = args.Get("--binCapacity", "1.00001,1.00001");
            var optimalBins = args.GetInt("--optimalBins", 3);
            var ffdMethod = args.Get("--ffdMethod", "FFDSum");
            var breakSymmetry = args.GetBool("--breakSymmetry", false);
            var timeout = args.GetDouble("--timeout", 1000);
            var verbose = args.GetBool("--verbose", false);

            Console.WriteLine($"Bins: {numBins}, Items: {numDemands}, Dimensions: {numDimensions}");
            Console.WriteLine($"Target optimal bins: {optimalBins}");

            // Parse bin capacity
            var binSize = binCapacityStr.Split(',').Select(double.Parse).ToList();
            if (binSize.Count != numDimensions)
            {
                throw new Exception($"Bin capacity dimensions ({binSize.Count}) must match numDimensions ({numDimensions})");
            }

            var bins = new Bins(numBins, binSize);
            var ffdMethodChoice = ffdMethod switch
            {
                "FF" => FFDMethodChoice.FF,
                "FFDProd" => FFDMethodChoice.FFDProd,
                "FFDDiv" => FFDMethodChoice.FFDDiv,
                _ => FFDMethodChoice.FFDSum,
            };
            var solver = new GurobiSOS(timeout: timeout, verbose: Convert.ToInt32(verbose));
            var optimalEncoder = new VBPOptimalEncoder<GRBVar, GRBModel>(
                solver, numDemands, numDimensions, BreakSymmetry: breakSymmetry);
            var ffdEncoder = new FFDItemCentricEncoder<GRBVar, GRBModel>(
                solver, numDemands, numDimensions);
            var adversarialGenerator = new VBPAdversarialInputGenerator<GRBVar, GRBModel>(
                bins, numDemands, numDimensions);

            var timer = Stopwatch.StartNew();
            var (optimalSolution, ffdSolution) = adversarialGenerator.MaximizeOptimalityGapFFD(
                optimalEncoder, ffdEncoder, optimalBins,
                ffdMethod: ffdMethodChoice, itemList: null, verbose: verbose);
            timer.Stop();

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"Optimal bins used: {optimalSolution.TotalNumBinsUsed}");
            Console.WriteLine($"FFD bins used: {ffdSolution.TotalNumBinsUsed}");
            Console.WriteLine($"Gap: {ffdSolution.TotalNumBinsUsed - optimalSolution.TotalNumBinsUsed}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('=', 60));

            if (verbose)
            {
                Console.WriteLine("\nOptimal Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    optimalSolution, Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine("\nFFD Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    ffdSolution, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}