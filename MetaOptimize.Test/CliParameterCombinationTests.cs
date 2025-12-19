// <copyright file="CliParameterCombinationTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using System.Linq;
    using CommandLine;
    using Gurobi;
    using MetaOptimize.Cli;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Comprehensive tests for CLI parameter combinations.
    /// Validates that all parameter combinations parse correctly and runners can be initialized.
    /// </summary>
    [TestClass]
    public class CliParameterCombinationTests
    {
        #region Problem Type x Solver Combinations

        /// <summary>
        /// Test all ProblemType and SolverChoice combinations parse correctly.
        /// </summary>
        [TestMethod]
        public void TestAllProblemTypeSolverCombinations()
        {
            var problemTypes = new[] { "TrafficEngineering", "BinPacking", "PIFO", "FailureAnalysis" };
            var solvers = new[] { "Gurobi", "Zen" };

            foreach (var problemType in problemTypes)
            {
                foreach (var solver in solvers)
                {
                    var args = new string[] { "--problemType", problemType, "--solver", solver };
                    var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                    result.WithParsed(opts =>
                    {
                        Assert.IsNotNull(opts, $"Failed to parse: {problemType} + {solver}");
                    });

                    result.WithNotParsed(errors =>
                    {
                        Assert.Fail($"Parse failed for {problemType} + {solver}");
                    });
                }
            }
        }

        #endregion

        #region Traffic Engineering - Heuristic Combinations

        /// <summary>
        /// Test all TE heuristic types parse correctly.
        /// </summary>
        [TestMethod]
        public void TestTEAllHeuristics()
        {
            var heuristics = new[]
            {
                "Pop", "DemandPinning", "ExpectedPop", "PopDp",
                "ExpectedPopDp", "ParallelPop", "ParallelPopDp", "ModifiedDp",
            };

            foreach (var heuristic in heuristics)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--heuristic", heuristic,
                    "--solver", "Gurobi",
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(ProblemType.TrafficEngineering, opts.ProblemType);
                    Assert.AreEqual(heuristic, opts.Heuristic.ToString(), $"Heuristic mismatch for {heuristic}");
                });

                result.WithNotParsed(errors =>
                {
                    Assert.Fail($"Parse failed for heuristic: {heuristic}");
                });
            }
        }

        /// <summary>
        /// Test Pop heuristic with various slice counts.
        /// </summary>
        [TestMethod]
        public void TestTEPopWithDifferentSlices()
        {
            var sliceCounts = new[] { 1, 2, 3, 4, 5, 10 };

            foreach (var slices in sliceCounts)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--heuristic", "Pop",
                    "--slices", slices.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(slices, opts.PopSlices, $"Slices mismatch for {slices}");
                });
            }
        }

        /// <summary>
        /// Test DemandPinning with various thresholds.
        /// </summary>
        [TestMethod]
        public void TestTEDemandPinningThresholds()
        {
            var thresholds = new[] { 0.1, 0.25, 0.5, 0.75, 1.0, 5.0, 10.0 };

            foreach (var threshold in thresholds)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--heuristic", "DemandPinning",
                    "--pinthreshold", threshold.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(threshold, opts.DemandPinningThreshold, $"Threshold mismatch for {threshold}");
                });
            }
        }

        /// <summary>
        /// Test ModifiedDp with various max shortest path lengths.
        /// </summary>
        [TestMethod]
        public void TestTEModifiedDpMaxPathLengths()
        {
            var lengths = new[] { -1, 1, 2, 3, 5, 10 };

            foreach (var length in lengths)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--heuristic", "ModifiedDp",
                    "--maxshortestlen", length.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(length, opts.MaxShortestPathLen, $"MaxShortestPathLen mismatch for {length}");
                });
            }
        }

        #endregion

        #region Traffic Engineering - Method Combinations

        /// <summary>
        /// Test all TE method choices.
        /// </summary>
        [TestMethod]
        public void TestTEAllMethods()
        {
            var methods = new[]
            {
                "Direct", "Search", "FindFeas", "Random", "HillClimber", "SimulatedAnnealing",
            };

            foreach (var method in methods)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--method", method,
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(method, opts.Method.ToString(), $"Method mismatch for {method}");
                });
            }
        }

        /// <summary>
        /// Test Search method with various confidence levels.
        /// </summary>
        [TestMethod]
        public void TestTESearchMethodConfidenceLevels()
        {
            var confidences = new[] { 0.01, 0.05, 0.1, 0.2, 0.5 };

            foreach (var confidence in confidences)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--method", "Search",
                    "--confidence", confidence.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(confidence, opts.Confidencelvl, $"Confidence mismatch for {confidence}");
                });
            }
        }

        /// <summary>
        /// Test Search/FindFeas methods with various starting gaps.
        /// </summary>
        [TestMethod]
        public void TestTEStartingGaps()
        {
            var gaps = new[] { 1.0, 5.0, 10.0, 20.0, 50.0, 100.0 };

            foreach (var gap in gaps)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--method", "FindFeas",
                    "--startinggap", gap.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(gap, opts.StartingGap, $"StartingGap mismatch for {gap}");
                });
            }
        }

        /// <summary>
        /// Test Random method with various trial counts.
        /// </summary>
        [TestMethod]
        public void TestTERandomMethodTrials()
        {
            var trials = new[] { 1, 5, 10, 50, 100 };

            foreach (var trial in trials)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--method", "Random",
                    "--num", trial.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(trial, opts.NumRandom, $"NumRandom mismatch for {trial}");
                });
            }
        }

        /// <summary>
        /// Test HillClimber with various neighbor counts.
        /// </summary>
        [TestMethod]
        public void TestTEHillClimberNeighbors()
        {
            var neighbors = new[] { 1, 2, 5, 10, 20 };

            foreach (var neighbor in neighbors)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--method", "HillClimber",
                    "--neighbors", neighbor.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(neighbor, opts.NumNeighbors, $"NumNeighbors mismatch for {neighbor}");
                });
            }
        }

        /// <summary>
        /// Test SimulatedAnnealing temperature parameters.
        /// </summary>
        [TestMethod]
        public void TestTESimulatedAnnealingParams()
        {
            var initTemps = new[] { 0.5, 1.0, 2.0, 10.0 };
            var lambdas = new[] { 0.5, 0.9, 0.95, 0.99 };

            foreach (var temp in initTemps)
            {
                foreach (var lambda in lambdas)
                {
                    var args = new string[]
                    {
                        "--problemType", "TrafficEngineering",
                        "--method", "SimulatedAnnealing",
                        "--inittmp", temp.ToString(),
                        "--lambda", lambda.ToString(),
                    };

                    var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                    result.WithParsed(opts =>
                    {
                        Assert.AreEqual(temp, opts.InitTmp, $"InitTmp mismatch for {temp}");
                        Assert.AreEqual(lambda, opts.TmpDecreaseFactor, $"Lambda mismatch for {lambda}");
                    });
                }
            }
        }

        #endregion

        #region Traffic Engineering - Inner Encoding

        /// <summary>
        /// Test KKT and PrimalDual inner encodings.
        /// </summary>
        [TestMethod]
        public void TestTEInnerEncodings()
        {
            var encodings = new[] { "KKT", "PrimalDual" };

            foreach (var encoding in encodings)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--innerencoding", encoding,
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    var expected = encoding == "KKT"
                        ? InnerRewriteMethodChoice.KKT
                        : InnerRewriteMethodChoice.PrimalDual;
                    Assert.AreEqual(expected, opts.InnerEncoding, $"InnerEncoding mismatch for {encoding}");
                });
            }
        }

        /// <summary>
        /// Test PrimalDual with various demand lists.
        /// </summary>
        [TestMethod]
        public void TestTEPrimalDualDemandLists()
        {
            var demandLists = new[]
            {
                "0",
                "0,5",
                "0,5,10",
                "0,5,10,15,20",
                "0,1,2,3,4,5,6,7,8,9,10",
            };

            foreach (var demandList in demandLists)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--innerencoding", "PrimalDual",
                    "--demandlist", demandList,
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(demandList, opts.DemandList, $"DemandList mismatch for {demandList}");

                    // Verify parsing
                    var parsed = opts.DemandList.Split(',').Select(double.Parse).ToList();
                    var expectedCount = demandList.Split(',').Length;
                    Assert.AreEqual(expectedCount, parsed.Count);
                });
            }
        }

        #endregion

        #region Traffic Engineering - Clustering Options

        /// <summary>
        /// Test clustering enable/disable.
        /// </summary>
        [TestMethod]
        public void TestTEClusteringToggle()
        {
            // Clustering disabled (default)
            var args1 = new string[] { "--problemType", "TrafficEngineering" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args1).WithParsed(opts =>
            {
                Assert.AreEqual(false, opts.EnableClustering);
            });

            // Clustering enabled
            var args2 = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--enableclustering", "true",
            };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args2).WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.EnableClustering);
            });
        }

        /// <summary>
        /// Test clustering with various cluster counts.
        /// </summary>
        [TestMethod]
        public void TestTEClusteringNumClusters()
        {
            var clusterCounts = new[] { 2, 3, 4, 5, 10 };

            foreach (var count in clusterCounts)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--enableclustering", "true",
                    "--numclusters", count.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(count, opts.NumClusters, $"NumClusters mismatch for {count}");
                });
            }
        }

        /// <summary>
        /// Test clustering versions.
        /// </summary>
        [TestMethod]
        public void TestTEClusteringVersions()
        {
            var versions = new[] { 1, 2, 3 };

            foreach (var version in versions)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--enableclustering", "true",
                    "--clusterversion", version.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(version, opts.ClusterVersion, $"ClusterVersion mismatch for {version}");
                });
            }
        }

        #endregion

        #region Traffic Engineering - Path Options

        /// <summary>
        /// Test various path counts.
        /// </summary>
        [TestMethod]
        public void TestTEPathCounts()
        {
            var pathCounts = new[] { 1, 2, 3, 4, 5 };

            foreach (var paths in pathCounts)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--paths", paths.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(paths, opts.Paths, $"Paths mismatch for {paths}");
                });
            }
        }

        #endregion

        #region Traffic Engineering - Demand Constraints

        /// <summary>
        /// Test demand upper bound values.
        /// </summary>
        [TestMethod]
        public void TestTEDemandUpperBounds()
        {
            var bounds = new[] { -1.0, 10.0, 50.0, 100.0, 1000.0 };

            foreach (var bound in bounds)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--demandub", bound.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(bound, opts.DemandUB, $"DemandUB mismatch for {bound}");
                });
            }
        }

        /// <summary>
        /// Test max density values.
        /// </summary>
        [TestMethod]
        public void TestTEMaxDensity()
        {
            var densities = new[] { 0.1, 0.25, 0.5, 0.75, 1.0 };

            foreach (var density in densities)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--maxdensity", density.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(density, opts.MaxDensity, $"MaxDensity mismatch for {density}");
                });
            }
        }

        #endregion

        #region Bin Packing - FFD Method Combinations

        /// <summary>
        /// Test all FFD methods with various bin/item counts.
        /// </summary>
        [TestMethod]
        public void TestBPAllFFMethodsWithSizes()
        {
            var ffMethods = new[] { "FF", "FFDSum", "FFDProd", "FFDDiv" };
            var configs = new[]
            {
                (bins: 4, items: 6, optimal: 2),
                (bins: 6, items: 9, optimal: 3),
                (bins: 8, items: 12, optimal: 4),
                (bins: 10, items: 15, optimal: 5),
            };

            foreach (var method in ffMethods)
            {
                foreach (var config in configs)
                {
                    var args = new string[]
                    {
                        "--problemType", "BinPacking",
                        "--ffMethod", method,
                        "--numBins", config.bins.ToString(),
                        "--numDemands", config.items.ToString(),
                        "--optimalBins", config.optimal.ToString(),
                    };

                    var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                    result.WithParsed(opts =>
                    {
                        Assert.AreEqual(config.bins, opts.NumBins);
                        Assert.AreEqual(config.items, opts.NumDemands);
                        Assert.AreEqual(config.optimal, opts.OptimalBins);
                    });

                    result.WithNotParsed(errors =>
                    {
                        Assert.Fail($"Parse failed for {method} with config ({config.bins}, {config.items}, {config.optimal})");
                    });
                }
            }
        }

        /// <summary>
        /// Test BinPacking with various dimension counts.
        /// </summary>
        [TestMethod]
        public void TestBPDimensions()
        {
            var dimensions = new[] { 1, 2, 3, 4, 5 };

            foreach (var dim in dimensions)
            {
                // Build capacity string
                var capacities = string.Join(",", Enumerable.Repeat("1.00001", dim));

                var args = new string[]
                {
                    "--problemType", "BinPacking",
                    "--numDimensions", dim.ToString(),
                    "--binCapacity", capacities,
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(dim, opts.NumDimensions, $"NumDimensions mismatch for {dim}");

                    var parsedCapacities = opts.BinCapacity.Split(',').Select(double.Parse).ToList();
                    Assert.AreEqual(dim, parsedCapacities.Count, $"Capacity count mismatch for {dim}");
                });
            }
        }

        /// <summary>
        /// Test BinPacking with various bin capacities.
        /// </summary>
        [TestMethod]
        public void TestBPBinCapacities()
        {
            var capacityConfigs = new[]
            {
                "1.0,1.0",
                "1.00001,1.00001",
                "1.5,1.5",
                "2.0,1.0",
                "1.0,2.0,1.5",
            };

            foreach (var capacities in capacityConfigs)
            {
                var args = new string[]
                {
                    "--problemType", "BinPacking",
                    "--binCapacity", capacities,
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(capacities, opts.BinCapacity, $"BinCapacity mismatch for {capacities}");
                });
            }
        }

        /// <summary>
        /// Test BinPacking symmetry breaking toggle.
        /// </summary>
        [TestMethod]
        public void TestBPSymmetryBreaking()
        {
            // Default (false)
            var args1 = new string[] { "--problemType", "BinPacking" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args1).WithParsed(opts =>
            {
                Assert.AreEqual(false, opts.BreakSymmetry);
            });

            // Enabled
            var args2 = new string[] { "--problemType", "BinPacking", "--breakSymmetry", "true" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args2).WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.BreakSymmetry);
            });
        }

        #endregion

        #region PIFO - Parameter Combinations

        /// <summary>
        /// Test PIFO with various packet counts.
        /// </summary>
        [TestMethod]
        public void TestPIFOPacketCounts()
        {
            var packetCounts = new[] { 10, 12, 15, 18, 20, 24 };

            foreach (var packets in packetCounts)
            {
                var args = new string[]
                {
                    "--problemType", "PIFO",
                    "--numPackets", packets.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(packets, opts.NumPackets, $"NumPackets mismatch for {packets}");
                });
            }
        }

        /// <summary>
        /// Test PIFO with various rank values.
        /// </summary>
        [TestMethod]
        public void TestPIFORankValues()
        {
            var ranks = new[] { 4, 6, 8, 10, 12, 16 };

            foreach (var rank in ranks)
            {
                var args = new string[]
                {
                    "--problemType", "PIFO",
                    "--maxRank", rank.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(rank, opts.MaxRank, $"MaxRank mismatch for {rank}");
                });
            }
        }

        /// <summary>
        /// Test PIFO with various queue configurations.
        /// </summary>
        [TestMethod]
        public void TestPIFOQueueConfigs()
        {
            var configs = new[]
            {
                (queues: 2, queueSize: 8),
                (queues: 4, queueSize: 12),
                (queues: 6, queueSize: 15),
                (queues: 8, queueSize: 20),
            };

            foreach (var config in configs)
            {
                var args = new string[]
                {
                    "--problemType", "PIFO",
                    "--numQueues", config.queues.ToString(),
                    "--maxQueueSize", config.queueSize.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(config.queues, opts.NumQueues);
                    Assert.AreEqual(config.queueSize, opts.MaxQueueSize);
                });
            }
        }

        /// <summary>
        /// Test PIFO AIFO parameters.
        /// </summary>
        [TestMethod]
        public void TestPIFOAIFOParams()
        {
            var configs = new[]
            {
                (windowSize: 8, burstParam: 0.05),
                (windowSize: 12, burstParam: 0.1),
                (windowSize: 15, burstParam: 0.15),
                (windowSize: 20, burstParam: 0.2),
            };

            foreach (var config in configs)
            {
                var args = new string[]
                {
                    "--problemType", "PIFO",
                    "--windowSize", config.windowSize.ToString(),
                    "--burstParam", config.burstParam.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(config.windowSize, opts.WindowSize);
                    Assert.AreEqual(config.burstParam, opts.BurstParam);
                });
            }
        }

        #endregion

        #region Failure Analysis - Parameter Combinations

        /// <summary>
        /// Test FailureAnalysis with various failure counts.
        /// </summary>
        [TestMethod]
        public void TestFAFailureCounts()
        {
            var failureCounts = new[] { 1, 2, 3, 4, 5 };

            foreach (var failures in failureCounts)
            {
                var args = new string[]
                {
                    "--problemType", "FailureAnalysis",
                    "--maxNumFailures", failures.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(failures, opts.MaxNumFailures, $"MaxNumFailures mismatch for {failures}");
                });
            }
        }

        /// <summary>
        /// Test FailureAnalysis with various extra path counts.
        /// </summary>
        [TestMethod]
        public void TestFAExtraPaths()
        {
            var extraPaths = new[] { 1, 2, 3, 4, 5 };

            foreach (var paths in extraPaths)
            {
                var args = new string[]
                {
                    "--problemType", "FailureAnalysis",
                    "--numExtraPaths", paths.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(paths, opts.NumExtraPaths, $"NumExtraPaths mismatch for {paths}");
                });
            }
        }

        /// <summary>
        /// Test FailureAnalysis failure probability thresholds.
        /// </summary>
        [TestMethod]
        public void TestFAFailureProbThresholds()
        {
            var thresholds = new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 1.0 };

            foreach (var threshold in thresholds)
            {
                var args = new string[]
                {
                    "--problemType", "FailureAnalysis",
                    "--failureProbThreshold", threshold.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(threshold, opts.FailureProbThreshold, $"FailureProbThreshold mismatch for {threshold}");
                });
            }
        }

        /// <summary>
        /// Test FailureAnalysis scenario probability thresholds.
        /// </summary>
        [TestMethod]
        public void TestFAScenarioProbThresholds()
        {
            var thresholds = new[] { 0.0, 0.01, 0.05, 0.1 };

            foreach (var threshold in thresholds)
            {
                var args = new string[]
                {
                    "--problemType", "FailureAnalysis",
                    "--scenarioProbThreshold", threshold.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(threshold, opts.ScenarioProbThreshold, $"ScenarioProbThreshold mismatch for {threshold}");
                });
            }
        }

        /// <summary>
        /// Test FailureAnalysis topology toggle.
        /// </summary>
        [TestMethod]
        public void TestFATopologyToggle()
        {
            // With flag = true
            var args1 = new string[]
            {
                "--problemType", "FailureAnalysis",
                "--useDefaultTopology",
            };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args1).WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.UseDefaultTopology);
            });

            // Without flag = false (uses default)
            var args2 = new string[]
            {
                "--problemType", "FailureAnalysis",
                "--topologyFile", "custom.json",  "--useDefaultTopology", "false",
            };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args2).WithParsed(opts =>
            {
                Assert.AreEqual(false, opts.UseDefaultTopology);
                Assert.AreEqual("custom.json", opts.TopologyFile);
            });
        }

        #endregion

        #region Common Options - Timeout and Threads

        /// <summary>
        /// Test various timeout values.
        /// </summary>
        [TestMethod]
        public void TestTimeoutValues()
        {
            var timeouts = new[] { 10.0, 60.0, 300.0, 1000.0, 3600.0 };

            foreach (var timeout in timeouts)
            {
                var args = new string[]
                {
                    "--problemType", "BinPacking",
                    "--timeout", timeout.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(timeout, opts.Timeout, $"Timeout mismatch for {timeout}");
                });
            }
        }

        /// <summary>
        /// Test various Gurobi thread counts.
        /// </summary>
        [TestMethod]
        public void TestGurobiThreads()
        {
            var threadCounts = new[] { 0, 1, 2, 4, 8 };

            foreach (var threads in threadCounts)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--gurobithreads", threads.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(threads, opts.NumGurobiThreads, $"NumGurobiThreads mismatch for {threads}");
                });
            }
        }

        /// <summary>
        /// Test seed values for reproducibility.
        /// </summary>
        [TestMethod]
        public void TestSeedValues()
        {
            var seeds = new[] { 0, 1, 42, 12345, 999999 };

            foreach (var seed in seeds)
            {
                var args = new string[]
                {
                    "--problemType", "TrafficEngineering",
                    "--seed", seed.ToString(),
                };

                var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

                result.WithParsed(opts =>
                {
                    Assert.AreEqual(seed, opts.Seed, $"Seed mismatch for {seed}");
                });
            }
        }

        #endregion

        #region Common Options - Verbose and Debug

        /// <summary>
        /// Test verbose flag.
        /// </summary>
        [TestMethod]
        public void TestVerboseFlag()
        {
            // Without verbose
            var args1 = new string[] { "--problemType", "BinPacking" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args1).WithParsed(opts =>
            {
                Assert.AreEqual(false, opts.Verbose);
            });

            // With verbose
            var args2 = new string[] { "--problemType", "BinPacking", "--verbose" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args2).WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.Verbose);
            });

            // With -v shorthand
            var args3 = new string[] { "--problemType", "BinPacking", "-v" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args3).WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.Verbose);
            });
        }

        /// <summary>
        /// Test debug flag.
        /// </summary>
        [TestMethod]
        public void TestDebugFlag()
        {
            // Without debug
            var args1 = new string[] { "--problemType", "BinPacking" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args1).WithParsed(opts =>
            {
                Assert.AreEqual(false, opts.Debug);
            });

            // With debug
            var args2 = new string[] { "--problemType", "BinPacking", "--debug" };
            CommandLine.Parser.Default.ParseArguments<CliOptions>(args2).WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.Debug);
            });
        }

        #endregion

        #region Complex Combination Tests

        /// <summary>
        /// Test TE with Pop heuristic and all common options.
        /// </summary>
        [TestMethod]
        public void TestTEPopFullConfiguration()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--topologyFile", "Swan.json",
                "--heuristic", "Pop",
                "--solver", "Gurobi",
                "--paths", "2",
                "--slices", "3",
                "--method", "Direct",
                "--innerencoding", "KKT",
                "--timeout", "300",
                "--gurobithreads", "1",
                "--seed", "42",
                "--verbose",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.TrafficEngineering, opts.ProblemType);
                Assert.AreEqual("Swan.json", opts.TopologyFile);
                Assert.AreEqual(Heuristic.Pop, opts.Heuristic);
                Assert.AreEqual(SolverChoice.Gurobi, opts.SolverChoice);
                Assert.AreEqual(2, opts.Paths);
                Assert.AreEqual(3, opts.PopSlices);
                Assert.AreEqual(MethodChoice.Direct, opts.Method);
                Assert.AreEqual(InnerRewriteMethodChoice.KKT, opts.InnerEncoding);
                Assert.AreEqual(300, opts.Timeout);
                Assert.AreEqual(1, opts.NumGurobiThreads);
                Assert.AreEqual(42, opts.Seed);
                Assert.AreEqual(true, opts.Verbose);
            });

            result.WithNotParsed(errors =>
            {
                Assert.Fail("Failed to parse TE Pop full configuration");
            });
        }

        /// <summary>
        /// Test TE with DemandPinning and PrimalDual.
        /// </summary>
        [TestMethod]
        public void TestTEDemandPinningPrimalDualConfig()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--heuristic", "DemandPinning",
                "--pinthreshold", "0.5",
                "--innerencoding", "PrimalDual",
                "--demandlist", "0,5,10,15,20",
                "--paths", "3",
                "--method", "Direct",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(Heuristic.DemandPinning, opts.Heuristic);
                Assert.AreEqual(0.5, opts.DemandPinningThreshold);
                Assert.AreEqual(InnerRewriteMethodChoice.PrimalDual, opts.InnerEncoding);
                Assert.AreEqual("0,5,10,15,20", opts.DemandList);
                Assert.AreEqual(3, opts.Paths);
            });
        }

        /// <summary>
        /// Test TE with clustering enabled.
        /// </summary>
        [TestMethod]
        public void TestTEClusteringFullConfig()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--enableclustering", "true",
                "--numclusters", "4",
                "--clusterversion", "3",
                "--interclustersamples", "10",
                "--nodespercluster", "5",
                "--numinterclusterquantization", "5",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(true, opts.EnableClustering);
                Assert.AreEqual(4, opts.NumClusters);
                Assert.AreEqual(3, opts.ClusterVersion);
                Assert.AreEqual(10, opts.NumInterClusterSamples);
                Assert.AreEqual(5, opts.NumNodesPerCluster);
                Assert.AreEqual(5, opts.NumInterClusterQuantizations);
            });
        }

        /// <summary>
        /// Test BinPacking with all options.
        /// </summary>
        [TestMethod]
        public void TestBPFullConfiguration()
        {
            var args = new string[]
            {
                "--problemType", "BinPacking",
                "--solver", "Gurobi",
                "--numBins", "8",
                "--numDemands", "12",
                "--numDimensions", "3",
                "--binCapacity", "1.5,1.5,1.5",
                "--optimalBins", "4",
                "--ffMethod", "FFDProd",
                "--breakSymmetry", "true",
                "--timeout", "120",
                "--verbose",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.BinPacking, opts.ProblemType);
                Assert.AreEqual(SolverChoice.Gurobi, opts.SolverChoice);
                Assert.AreEqual(8, opts.NumBins);
                Assert.AreEqual(12, opts.NumDemands);
                Assert.AreEqual(3, opts.NumDimensions);
                Assert.AreEqual("1.5,1.5,1.5", opts.BinCapacity);
                Assert.AreEqual(4, opts.OptimalBins);
                Assert.AreEqual(FFDMethodChoice.FFDProd, opts.FFMethod);
                Assert.AreEqual(true, opts.BreakSymmetry);
                Assert.AreEqual(120, opts.Timeout);
                Assert.AreEqual(true, opts.Verbose);
            });
        }

        /// <summary>
        /// Test PIFO with all options.
        /// </summary>
        [TestMethod]
        public void TestPIFOFullConfiguration()
        {
            var args = new string[]
            {
                "--problemType", "PIFO",
                "--solver", "Gurobi",
                "--numPackets", "24",
                "--maxRank", "10",
                "--numQueues", "6",
                "--maxQueueSize", "16",
                "--windowSize", "14",
                "--burstParam", "0.15",
                "--timeout", "180",
                "--verbose",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.PIFO, opts.ProblemType);
                Assert.AreEqual(24, opts.NumPackets);
                Assert.AreEqual(10, opts.MaxRank);
                Assert.AreEqual(6, opts.NumQueues);
                Assert.AreEqual(16, opts.MaxQueueSize);
                Assert.AreEqual(14, opts.WindowSize);
                Assert.AreEqual(0.15, opts.BurstParam);
                Assert.AreEqual(180, opts.Timeout);
            });
        }

        /// <summary>
        /// Test FailureAnalysis with all options.
        /// </summary>
        [TestMethod]
        public void TestFAFullConfiguration()
        {
            var args = new string[]
            {
                "--problemType", "FailureAnalysis",
                "--solver", "Gurobi",
                "--useDefaultTopology", "true",
                "--maxNumFailures", "2",
                "--numExtraPaths", "2",
                "--demandlist", "0,5,10,15",
                "--failureProbThreshold", "0.15",
                "--scenarioProbThreshold", "0.01",
                "--innerencoding", "PrimalDual",
                "--timeout", "240",
                "--verbose",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                Assert.AreEqual(ProblemType.FailureAnalysis, opts.ProblemType);
                Assert.AreEqual(true, opts.UseDefaultTopology);
                Assert.AreEqual(2, opts.MaxNumFailures);
                Assert.AreEqual(2, opts.NumExtraPaths);
                Assert.AreEqual("0,5,10,15", opts.DemandList);
                Assert.AreEqual(0.15, opts.FailureProbThreshold);
                Assert.AreEqual(0.01, opts.ScenarioProbThreshold);
                Assert.AreEqual(InnerRewriteMethodChoice.PrimalDual, opts.InnerEncoding);
                Assert.AreEqual(240, opts.Timeout);
            });
        }

        #endregion

        #region Smoke Tests - Actual Runner Initialization

        /// <summary>
        /// Smoke test: BinPacking runner can be initialized.
        /// </summary>
        [TestMethod]
        public void SmokeTestBPRunnerInit()
        {
            var args = new string[]
            {
                "--problemType", "BinPacking",
                "--solver", "Gurobi",
                "--numBins", "4",
                "--numDemands", "6",
                "--numDimensions", "2",
                "--optimalBins", "2",
                "--timeout", "30",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                // Parse capacities
                var binCapacities = opts.BinCapacity.Split(',').Select(double.Parse).ToList();
                while (binCapacities.Count < opts.NumDimensions)
                {
                    binCapacities.Add(1.00001);
                }

                // Create components
                var solver = new GurobiSOS(timeout: opts.Timeout, verbose: 0);
                var bins = new Bins(opts.NumBins, binCapacities);
                var optimalEncoder = new VBPOptimalEncoder<GRBVar, GRBModel>(
                    solver, opts.NumDemands, opts.NumDimensions, BreakSymmetry: opts.BreakSymmetry);
                var ffdEncoder = new FFDItemCentricEncoder<GRBVar, GRBModel>(
                    solver, opts.NumDemands, opts.NumDimensions);
                var adversarialGenerator = new VBPAdversarialInputGenerator<GRBVar, GRBModel>(
                    bins, opts.NumDemands, opts.NumDimensions);

                Assert.IsNotNull(solver);
                Assert.IsNotNull(bins);
                Assert.IsNotNull(optimalEncoder);
                Assert.IsNotNull(ffdEncoder);
                Assert.IsNotNull(adversarialGenerator);
            });
        }

       /// <summary>
        /// Smoke test: PIFO runner can be initialized.
        /// </summary>
        [TestMethod]
        public void SmokeTestPIFORunnerInit()
        {
            var args = new string[]
            {
                "--problemType", "PIFO",
                "--solver", "Gurobi",
                "--numPackets", "18",
                "--maxRank", "8",
                "--numQueues", "4",
                "--maxQueueSize", "12",
                "--timeout", "1000",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                var solver = new GurobiSOS(timeout: opts.Timeout, verbose: 0);

                var spPifoEncoder = new SPPIFOWithDropAvgDelayEncoder<GRBVar, GRBModel>(
                    solver, opts.NumPackets, opts.NumQueues, opts.MaxRank, opts.MaxQueueSize);

                var aifoEncoder = new AIFOAvgDelayEncoder<GRBVar, GRBModel>(
                    solver, opts.NumPackets, opts.MaxRank, opts.MaxQueueSize, opts.WindowSize, opts.BurstParam);

                var adversarialGenerator = new PIFOAdversarialInputGenerator<GRBVar, GRBModel>(
                    opts.NumPackets, opts.MaxRank);

                Assert.IsNotNull(solver);
                Assert.IsNotNull(spPifoEncoder);
                Assert.IsNotNull(aifoEncoder);
                Assert.IsNotNull(adversarialGenerator);
            });
        }

        /// <summary>
        /// Smoke test: TE components can be initialized.
        /// </summary>
        [TestMethod]
        public void SmokeTestTERunnerInit()
        {
            var args = new string[]
            {
                "--problemType", "TrafficEngineering",
                "--heuristic", "Pop",
                "--solver", "Gurobi",
                "--paths", "2",
                "--slices", "2",
                "--timeout", "30",
            };

            var result = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opts =>
            {
                CliOptions.Instance = opts;

                var solver = new GurobiSOS(timeout: opts.Timeout, verbose: 0);
                var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, opts.Paths);

                // Create simple test topology
                var topology = new Topology();
                topology.AddNode("a");
                topology.AddNode("b");
                topology.AddNode("c");
                topology.AddEdge("a", "b", capacity: 10);
                topology.AddEdge("b", "c", capacity: 10);
                topology.AddEdge("a", "c", capacity: 5);

                var partition = topology.RandomPartition(opts.PopSlices);
                var popEncoder = new PopEncoder<GRBVar, GRBModel>(
                    solver, maxNumPaths: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: partition);

                var adversarialGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(
                    topology, opts.Paths);

                Assert.IsNotNull(solver);
                Assert.IsNotNull(optimalEncoder);
                Assert.IsNotNull(popEncoder);
                Assert.IsNotNull(adversarialGenerator);
            });
        }

        #endregion
    }
}