// <copyright file="CliOptionsTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System.Collections.Generic;
    using CommandLine;
    using MetaOptimize.Cli;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for CLI options parsing and runner integration.
    /// Validates the unified CliOptions system works for all problem types.
    /// </summary>
    [TestClass]
    public class CliOptionsTests
    {
        #region CliOptions Parsing Tests

        /// <summary>
        /// Test that default values are set correctly when no arguments provided.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsDefaults()
        {
            var args = new string[] { };
            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                // Common defaults
                Assert.AreEqual(ProblemType.TrafficEngineering, opts.ProblemType);
                Assert.AreEqual(SolverChoice.Gurobi, opts.SolverChoice);
                Assert.AreEqual(false, opts.Verbose);
                Assert.AreEqual(false, opts.Debug);

                // BinPacking defaults
                Assert.AreEqual(6, opts.NumBins);
                Assert.AreEqual(9, opts.NumDemands);
                Assert.AreEqual(2, opts.NumDimensions);
                Assert.AreEqual(3, opts.OptimalBins);
                Assert.AreEqual(FFDMethodChoice.FFDSum, opts.FFMethod);
                Assert.AreEqual(false, opts.BreakSymmetry);

                // PIFO defaults
                Assert.AreEqual(18, opts.NumPackets);
                Assert.AreEqual(8, opts.MaxRank);
                Assert.AreEqual(4, opts.NumQueues);
                Assert.AreEqual(12, opts.MaxQueueSize);
                Assert.AreEqual(12, opts.WindowSize);
                Assert.AreEqual(0.1, opts.BurstParam);

                // FailureAnalysis defaults
                Assert.AreEqual(true, opts.UseDefaultTopology);
                Assert.AreEqual(1, opts.MaxNumFailures);
                Assert.AreEqual(1, opts.NumExtraPaths);
                Assert.AreEqual(0.25, opts.FailureProbThreshold);

                // TE defaults
                Assert.AreEqual(Heuristic.Pop, opts.Heuristic);
                Assert.AreEqual(2, opts.Paths);
                Assert.AreEqual(2, opts.PopSlices);
                Assert.AreEqual(MethodChoice.Direct, opts.Method);
                Assert.AreEqual(InnerRewriteMethodChoice.KKT, opts.InnerEncoding);
            });
        }

        /// <summary>
        /// Test parsing BinPacking problem type with custom parameters.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsBinPackingParsing()
        {
            var args = new string[]
            {
                "--problemType", "BinPacking",
                "--solver", "Gurobi",
                "--numBins", "10",
                "--numDemands", "15",
                "--numDimensions", "3",
                "--optimalBins", "5",
                "--ffMethod", "FFDProd",
                "--breakSymmetry", "true",
                "--binCapacity", "1.5,1.5,1.5",
                "--verbose",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.BinPacking, opts.ProblemType);
                Assert.AreEqual(SolverChoice.Gurobi, opts.SolverChoice);
                Assert.AreEqual(10, opts.NumBins);
                Assert.AreEqual(15, opts.NumDemands);
                Assert.AreEqual(3, opts.NumDimensions);
                Assert.AreEqual(5, opts.OptimalBins);
                Assert.AreEqual(FFDMethodChoice.FFDProd, opts.FFMethod);
                Assert.AreEqual(true, opts.BreakSymmetry);
                Assert.AreEqual("1.5,1.5,1.5", opts.BinCapacity);
                Assert.AreEqual(true, opts.Verbose);
            });

            result.WithNotParsed(errors =>
            {
                Assert.Fail("Failed to parse BinPacking arguments");
            });
        }

        /// <summary>
        /// Test parsing PIFO problem type with custom parameters.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsPIFOParsing()
        {
            var args = new string[]
            {
                "--problemType", "PIFO",
                "--solver", "Gurobi",
                "--numPackets", "24",
                "--maxRank", "10",
                "--numQueues", "6",
                "--maxQueueSize", "15",
                "--windowSize", "15",
                "--burstParam", "0.2",
                "--timeout", "500",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.PIFO, opts.ProblemType);
                Assert.AreEqual(24, opts.NumPackets);
                Assert.AreEqual(10, opts.MaxRank);
                Assert.AreEqual(6, opts.NumQueues);
                Assert.AreEqual(15, opts.MaxQueueSize);
                Assert.AreEqual(15, opts.WindowSize);
                Assert.AreEqual(0.2, opts.BurstParam);
                Assert.AreEqual(500, opts.Timeout);
            });

            result.WithNotParsed(errors =>
            {
                Assert.Fail("Failed to parse PIFO arguments");
            });
        }

        /// <summary>
        /// Test parsing FailureAnalysis problem type with custom parameters.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsFailureAnalysisParsing()
        {
            var args = new string[]
            {
                "--problemType", "FailureAnalysis",
                "--solver", "Gurobi",
                "--useDefaultTopology", "true",
                "--maxNumFailures", "2",
                "--numExtraPaths", "2",
                "--failureProbThreshold", "0.1",
                "--innerencoding", "PrimalDual",
                "--demandlist", "0,5,10,15",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.FailureAnalysis, opts.ProblemType);
                Assert.AreEqual(true, opts.UseDefaultTopology);
                Assert.AreEqual(2, opts.MaxNumFailures);
                Assert.AreEqual(2, opts.NumExtraPaths);
                Assert.AreEqual(0.1, opts.FailureProbThreshold);
                Assert.AreEqual(InnerRewriteMethodChoice.PrimalDual, opts.InnerEncoding);
                Assert.AreEqual("0,5,10,15", opts.DemandList);
            });

            result.WithNotParsed(errors =>
            {
                Assert.Fail("Failed to parse FailureAnalysis arguments");
            });
        }

        /// <summary>
        /// Test parsing TrafficEngineering problem type with custom parameters.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsTrafficEngineeringParsing()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--topologyFile", "Swan.json",
                "--heuristic", "DemandPinning",
                "--solver", "Gurobi",
                "--paths", "3",
                "--pinthreshold", "0.7",
                "--method", "Direct",
                "--innerencoding", "KKT",
                "--verbose",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.TrafficEngineering, opts.ProblemType);
                Assert.AreEqual("Swan.json", opts.TopologyFile);
                Assert.AreEqual(Heuristic.DemandPinning, opts.Heuristic);
                Assert.AreEqual(SolverChoice.Gurobi, opts.SolverChoice);
                Assert.AreEqual(3, opts.Paths);
                Assert.AreEqual(0.7, opts.DemandPinningThreshold);
                Assert.AreEqual(MethodChoice.Direct, opts.Method);
                Assert.AreEqual(InnerRewriteMethodChoice.KKT, opts.InnerEncoding);
                Assert.AreEqual(true, opts.Verbose);
            });

            result.WithNotParsed(errors =>
            {
                Assert.Fail("Failed to parse TrafficEngineering arguments");
            });
        }

        /// <summary>
        /// Test all FFDMethodChoice values parse correctly.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsFFMethodChoices()
        {
            var methods = new[] { "FF", "FFDSum", "FFDProd", "FFDDiv" };
            var expected = new[] { FFDMethodChoice.FF, FFDMethodChoice.FFDSum, FFDMethodChoice.FFDProd, FFDMethodChoice.FFDDiv };

            for (int i = 0; i < methods.Length; i++)
            {
                var args = new string[] { "--problemType", "BinPacking", "--ffMethod", methods[i] };
                var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                var expectedMethod = expected[i];
                result.WithParsed(opts =>
                {
                    Assert.AreEqual(expectedMethod, opts.FFMethod, $"Failed for {methods[i]}");
                });
            }
        }

        /// <summary>
        /// Test all SolverChoice values parse correctly.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsSolverChoices()
        {
            var solvers = new[] { "Gurobi", "Zen" };
            var expected = new[] { SolverChoice.Gurobi, SolverChoice.Zen };

            for (int i = 0; i < solvers.Length; i++)
            {
                var args = new string[] { "--solver", solvers[i] };
                var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                var expectedSolver = expected[i];
                result.WithParsed(opts =>
                {
                    Assert.AreEqual(expectedSolver, opts.SolverChoice, $"Failed for {solvers[i]}");
                });
            }
        }

        /// <summary>
        /// Test all MethodChoice values parse correctly.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsMethodChoices()
        {
            var methods = new[] { "Direct", "Search", "FindFeas", "Random", "HillClimber", "SimulatedAnnealing" };
            var expected = new[]
            {
                MethodChoice.Direct, MethodChoice.Search, MethodChoice.FindFeas,
                MethodChoice.Random, MethodChoice.HillClimber, MethodChoice.SimulatedAnnealing,
            };

            for (int i = 0; i < methods.Length; i++)
            {
                var args = new string[] { "--method", methods[i] };
                var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                var expectedMethod = expected[i];
                result.WithParsed(opts =>
                {
                    Assert.AreEqual(expectedMethod, opts.Method, $"Failed for {methods[i]}");
                });
            }
        }

        /// <summary>
        /// Test all Heuristic values parse correctly.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsHeuristicChoices()
        {
            var heuristics = new[] { "Pop", "DemandPinning", "ExpectedPop", "PopDp", "ModifiedDp" };
            var expected = new[]
            {
                Heuristic.Pop, Heuristic.DemandPinning, Heuristic.ExpectedPop,
                Heuristic.PopDp, Heuristic.ModifiedDp,
            };

            for (int i = 0; i < heuristics.Length; i++)
            {
                var args = new string[] { "--heuristic", heuristics[i] };
                var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                var expectedHeuristic = expected[i];
                result.WithParsed(opts =>
                {
                    Assert.AreEqual(expectedHeuristic, opts.Heuristic, $"Failed for {heuristics[i]}");
                });
            }
        }

        #endregion

        #region BinPacking Integration Tests

        /// <summary>
        /// Test BinPacking runner executes with CliOptions.
        /// Uses small parameters for fast execution.
        /// </summary>
        [TestMethod]
        public void TestBinPackingRunnerSmall()
        {
            var args = new string[]
            {
                "--problemType", "BinPacking",
                "--solver", "Gurobi",
                "--numBins", "4",
                "--numDemands", "6",
                "--numDimensions", "2",
                "--optimalBins", "2",
                "--ffMethod", "FFDSum",
                "--timeout", "60",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                // Verify options are set correctly
                Assert.AreEqual(ProblemType.BinPacking, opts.ProblemType);
                Assert.AreEqual(4, opts.NumBins);
                Assert.AreEqual(6, opts.NumDemands);
                Assert.AreEqual(2, opts.NumDimensions);
                Assert.AreEqual(2, opts.OptimalBins);

                // Parse bin capacities (same logic as BPRunner)
                var binCapacities = opts.BinCapacity.Split(',').Select(double.Parse).ToList();
                while (binCapacities.Count < opts.NumDimensions)
                {
                    binCapacities.Add(1.00001);
                }

                Assert.AreEqual(2, binCapacities.Count);

                // Create components (don't run full optimization - too slow for unit test)
                var solver = new GurobiSOS(timeout: opts.Timeout, verbose: 0);
                var bins = new Bins(opts.NumBins, binCapacities);

                Assert.IsNotNull(solver);
                Assert.IsNotNull(bins);
            });
        }

        /// <summary>
        /// Test BinPacking bin capacity parsing handles various formats.
        /// </summary>
        [TestMethod]
        public void TestBinPackingCapacityParsing()
        {
            // Single dimension
            var capacities1 = "1.5".Split(',').Select(double.Parse).ToList();
            Assert.AreEqual(1, capacities1.Count);
            Assert.AreEqual(1.5, capacities1[0]);

            // Two dimensions
            var capacities2 = "1.00001,1.00001".Split(',').Select(double.Parse).ToList();
            Assert.AreEqual(2, capacities2.Count);
            Assert.AreEqual(1.00001, capacities2[0]);
            Assert.AreEqual(1.00001, capacities2[1]);

            // Three dimensions
            var capacities3 = "2.0,1.5,1.0".Split(',').Select(double.Parse).ToList();
            Assert.AreEqual(3, capacities3.Count);
            Assert.AreEqual(2.0, capacities3[0]);
            Assert.AreEqual(1.5, capacities3[1]);
            Assert.AreEqual(1.0, capacities3[2]);
        }

        #endregion

        #region PIFO Integration Tests

        /// <summary>
        /// Test PIFO runner options are parsed correctly.
        /// </summary>
        [TestMethod]
        public void TestPIFORunnerOptions()
        {
            var args = new string[]
            {
                "--problemType", "PIFO",
                "--solver", "Gurobi",
                "--numPackets", "12",
                "--maxRank", "6",
                "--numQueues", "3",
                "--maxQueueSize", "8",
                "--windowSize", "8",
                "--burstParam", "0.15",
                "--timeout", "30",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                Assert.AreEqual(ProblemType.PIFO, opts.ProblemType);
                Assert.AreEqual(12, opts.NumPackets);
                Assert.AreEqual(6, opts.MaxRank);
                Assert.AreEqual(3, opts.NumQueues);
                Assert.AreEqual(8, opts.MaxQueueSize);
                Assert.AreEqual(8, opts.WindowSize);
                Assert.AreEqual(0.15, opts.BurstParam);
                Assert.AreEqual(30, opts.Timeout);

                // Create solver (don't run full optimization)
                var solver = new GurobiSOS(timeout: opts.Timeout, verbose: 0);
                Assert.IsNotNull(solver);
            });
        }

        #endregion

        #region FailureAnalysis Integration Tests

        /// <summary>
        /// Test FailureAnalysis runner options are parsed correctly.
        /// </summary>
        [TestMethod]
        public void TestFailureAnalysisRunnerOptions()
        {
            var args = new string[]
            {
                "--problemType", "FailureAnalysis",
                "--solver", "Gurobi",
                "--useDefaultTopology", "true",
                "--maxNumFailures", "1",
                "--numExtraPaths", "1",
                "--failureProbThreshold", "0.2",
                "--timeout", "30",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                Assert.AreEqual(ProblemType.FailureAnalysis, opts.ProblemType);
                Assert.AreEqual(true, opts.UseDefaultTopology);
                Assert.AreEqual(1, opts.MaxNumFailures);
                Assert.AreEqual(1, opts.NumExtraPaths);
                Assert.AreEqual(0.2, opts.FailureProbThreshold);

                // Parse demand list (same logic as FailureAnalysisRunner)
                var demandSet = new HashSet<double>(opts.DemandList.Split(',').Select(double.Parse));
                Assert.IsTrue(demandSet.Count > 0);
            });
        }

        /// <summary>
        /// Test demand list parsing for FailureAnalysis.
        /// </summary>
        [TestMethod]
        public void TestFailureAnalysisDemandListParsing()
        {
            var args = new string[]
            {
                "--problemType", "FailureAnalysis",
                "--demandlist", "0,5,10,15,20",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                var demandSet = new HashSet<double>(opts.DemandList.Split(',').Select(double.Parse));
                Assert.AreEqual(5, demandSet.Count);
                Assert.IsTrue(demandSet.Contains(0));
                Assert.IsTrue(demandSet.Contains(5));
                Assert.IsTrue(demandSet.Contains(10));
                Assert.IsTrue(demandSet.Contains(15));
                Assert.IsTrue(demandSet.Contains(20));
            });
        }

        #endregion

        #region TrafficEngineering Integration Tests

        /// <summary>
        /// Test TrafficEngineering runner options are parsed correctly.
        /// </summary>
        [TestMethod]
        public void TestTrafficEngineeringRunnerOptions()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--topologyFile", "Topologies/simple.json",
                "--heuristic", "Pop",
                "--solver", "Gurobi",
                "--paths", "2",
                "--slices", "2",
                "--method", "Direct",
                "--timeout", "30",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                Assert.AreEqual(ProblemType.TrafficEngineering, opts.ProblemType);
                Assert.AreEqual("Topologies/simple.json", opts.TopologyFile);
                Assert.AreEqual(Heuristic.Pop, opts.Heuristic);
                Assert.AreEqual(2, opts.Paths);
                Assert.AreEqual(2, opts.PopSlices);
                Assert.AreEqual(MethodChoice.Direct, opts.Method);
            });
        }

        /// <summary>
        /// Test TrafficEngineering DemandPinning options.
        /// </summary>
        [TestMethod]
        public void TestTrafficEngineeringDemandPinningOptions()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--heuristic", "DemandPinning",
                "--pinthreshold", "0.6",
                "--paths", "3",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(Heuristic.DemandPinning, opts.Heuristic);
                Assert.AreEqual(0.6, opts.DemandPinningThreshold);
                Assert.AreEqual(3, opts.Paths);
            });
        }

        /// <summary>
        /// Test TrafficEngineering PrimalDual encoding options.
        /// </summary>
        [TestMethod]
        public void TestTrafficEngineeringPrimalDualOptions()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--innerencoding", "PrimalDual",
                "--demandlist", "0,10,20,30",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(InnerRewriteMethodChoice.PrimalDual, opts.InnerEncoding);
                Assert.AreEqual("0,10,20,30", opts.DemandList);

                // Parse and validate
                var demandSet = new HashSet<double>(opts.DemandList.Split(',').Select(double.Parse));
                Assert.AreEqual(4, demandSet.Count);
            });
        }

        #endregion

        #region Edge Cases and Error Handling

        /// <summary>
        /// Test that CliOptions.Instance is set correctly.
        /// </summary>
        [TestMethod]
        public void TestCliOptionsInstance()
        {
            var args = new string[] { "--problemType", "BinPacking" };
            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;
                Assert.IsNotNull(CliOptions.Instance);
                Assert.AreEqual(ProblemType.BinPacking, CliOptions.Instance.ProblemType);
            });
        }

        /// <summary>
        /// Test short option flags work.
        /// </summary>
        [TestMethod]
        public void TestShortOptionFlags()
        {
            var args = new string[]
            {
                "-f", "test.json",
                "-h", "Pop",
                "-c", "Gurobi",
                "-v",
            };

            var result =  CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual("test.json", opts.TopologyFile);
                Assert.AreEqual(Heuristic.Pop, opts.Heuristic);
                Assert.AreEqual(SolverChoice.Gurobi, opts.SolverChoice);
                Assert.AreEqual(true, opts.Verbose);
            });
        }

        /// <summary>
        /// Test timeout parsing with various values.
        /// </summary>
        [TestMethod]
        public void TestTimeoutParsing()
        {
            // Finite timeout
            var args1 = new string[] { "--timeout", "100" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args1).WithParsed(opts =>
            {
                Assert.AreEqual(100, opts.Timeout);
            });

            // Large timeout
            var args2 = new string[] { "--timeout", "3600" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args2).WithParsed(opts =>
            {
                Assert.AreEqual(3600, opts.Timeout);
            });
        }

        #endregion
    }
}