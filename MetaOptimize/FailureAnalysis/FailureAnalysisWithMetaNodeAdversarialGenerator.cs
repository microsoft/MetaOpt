using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gurobi;
using MetaOptimize;
using NLog;
using NLog.LayoutRenderers;
using QuikGraph.Algorithms.ShortestPath;

namespace MetaOptimize.FailureAnalysis
{
    /// <summary>
    /// A class that generates adversarial scenarios for network failure analysis by finding the worst-case failure combinations,
    /// with support for meta-nodes that represent groups of actual nodes. This allows for more efficient analysis of large networks
    /// by treating groups of nodes as single entities.
    /// Key features:
    /// 1. Meta-node support: Groups of nodes can be treated as single entities for path computation and failure analysis
    /// 2. Path computation: Automatically computes paths between nodes, respecting meta-node constraints
    /// 3. Failure modeling: Supports both link and LAG failures, with constraints on failure probabilities and counts
    /// 4. Path constraints: Ensures proper handling of path failures and backup path activation
    /// The class extends the base FailureAnalysisAdversarialGenerator by adding meta-node functionality while maintaining
    /// all the core failure analysis capabilities.
    /// </summary>
    /// <typeparam name="TVar"></typeparam>
    /// <typeparam name="TSolution"></typeparam>
    public class FailureAnalysisWithMetaNodeAdversarialGenerator<TVar, TSolution> : FailureAnalysisAdversarialGenerator<TVar, TSolution>
    {
        /// <summary>
        /// Has the mapping from the metanodes to the actual nodes they represent.
        /// </summary>
        protected Dictionary<string, HashSet<string>> MetaNodeToActualNode = null;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes a new instance of the FailureAnalysisWithMetaNodeAdversarialGenerator class.
        /// </summary>
        /// <param name="topology">The network topology to analyze.</param>
        /// <param name="maxNumPaths">Maximum number of paths to consider between any node pair.</param>
        /// <param name="numProcesses">Number of parallel processes to use for computation.</param>
        /// <param name="metaNodeToActualNodes">Mapping from meta-nodes to their constituent actual nodes.</param>
        public FailureAnalysisWithMetaNodeAdversarialGenerator(Topology topology, int maxNumPaths, int numProcesses = -1, Dictionary<string, HashSet<string>> metaNodeToActualNodes = null)
            : base(topology, maxNumPaths, numProcesses)
        {
            this.MetaNodeToActualNode = metaNodeToActualNodes;
            this.AuxiliaryVariables = new List<TVar>();
            this.CapacityUpperBound = -1;
            foreach (var edge in topology.GetAllEdges())
            {
                if (edge.Capacity > this.CapacityUpperBound)
                {
                    this.CapacityUpperBound = edge.Capacity;
                }
            }
            this.LinkUpVariables = null;
        }
        private bool CheckIfPresent(string[] path, List<string> relayFilter)
        {
            bool found = false;
            if (relayFilter == null)
            {
                return found;
            }
            foreach (var filter in relayFilter)
            {
                found |= path.Skip(1).Take(path.Length - 2).Any(lag => lag.Contains(filter));
                if (found)
                {
                    return true;
                }
            }
            return found;
        }
        /// <summary>
        /// Filters out those routers which should not be tranist nodes.
        /// </summary>
        /// <param name="t">A copy of the topology.</param>
        /// <param name="source">the source router for the paths.</param>
        /// <param name="dest">the destination router for the paths.</param>
        /// <param name="relayFilter">the set of routers which should not be transit.</param>
        /// <param name="numPathNeeded">the number of paths we want to keep.</param>
        /// <param name="numPathsToTry">number of paths we want to try to get as an initial set to try to filter from.</param>
        /// <param name="numTries">number of retries.</param>
        private string[][] reComputePaths(Topology t, string source, string dest, List<string> relayFilter, int numPathNeeded, int numPathsToTry, int numTries)
        {
            var output = new Dictionary<(string, string), string[][]>();
            t.ShortestKPathsForPairList(numPathsToTry, Enumerable.Repeat((source, dest), 1), output);
            string[][] paths = output[(source, dest)];
            paths = paths.Where(p => !this.CheckIfPresent(p, relayFilter)).ToArray();
            if (paths.Length >= numPathNeeded)
            {
                return paths.Take(numPathNeeded).ToArray();
            }
            if (numTries > 5)
            {
                return paths;
            }
            return this.reComputePaths(t, source, dest, relayFilter, numPathNeeded, numPathsToTry + (numPathsToTry - paths.Length), numTries + 1);
        }
        /// <summary>
        /// Creates the topology absent the MetaNodes to get the paths in the original topology.
        /// </summary>
        /// <returns></returns>
        private Topology FilteredTopology()
        {
            var t = new Topology();
            foreach (var node in this.Topology.GetAllNodes())
            {
                if (!this.MetaNodeToActualNode.ContainsKey(node))
                {
                    t.AddNode(node);
                }
            }
            foreach (var edge in this.Topology.GetAllEdges())
            {
                if (!this.MetaNodeToActualNode.ContainsKey(edge.Source) && !this.MetaNodeToActualNode.ContainsKey(edge.Target))
                {
                    t.AddEdge(edge.Source, edge.Target, edge.Capacity);
                }
            }
            return t;
        }
        /// <summary>
        /// Sets the path for the failure analysis use-case when we want to have MetaNodes in the topology.
        /// This is different from the function of the same name from the vanila failureanalysisadversarial generator.
        /// Here we only define path proper between the metanodes because the assumption is that the only non-negative demand is between metanodes.
        /// </summary>
        /// <param name="numExtraPaths">Number of extra paths.</param>
        /// <param name="pathType">The path computation type.</param>
        /// <param name="selectedPaths">The input paths the user provided.</param>
        /// <param name="relayFilter">The set of nodes we don't want to allow to be tranist nodes.</param>
        public override void SetPath(int numExtraPaths, PathType pathType = PathType.KSP, Dictionary<(string, string), string[][]> selectedPaths = null, List<string> relayFilter = null)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            if (selectedPaths == null)
            {
                Topology t = this.FilteredTopology();
                this.Paths = this.Topology.ComputePaths(pathType, selectedPaths, this.maxNumPath + numExtraPaths, this.NumProcesses, false);
                var filteredPaths = t.ComputePaths(pathType, selectedPaths, this.maxNumPath + numExtraPaths, this.NumProcesses, false, relayFilter);
                foreach (var pair in this.Paths.Keys)
                {
                    var count = 0;
                    if (this.MetaNodeToActualNode.ContainsKey(pair.Item1) && this.MetaNodeToActualNode.ContainsKey(pair.Item2))
                    {
                        this.Paths[pair] = new string[0][];
                        foreach (var sourceEdge in MetaNodeToActualNode[pair.Item1])
                        {
                            foreach (var destinationEdge in MetaNodeToActualNode[pair.Item2])
                            {
                                int beforeCount = filteredPaths[(sourceEdge, destinationEdge)].Length;
                                count += filteredPaths[(sourceEdge, destinationEdge)].Length;
                                this.Paths[pair] = this.Paths[pair].Concat(filteredPaths[(sourceEdge, destinationEdge)].Select(r => r.Prepend(pair.Item1).Append(pair.Item2).ToArray())).ToArray();
                            }
                        }
                        Debug.Assert(this.Paths[pair].Length == count, "The path calculation probably has a bug.");
                    }
                }
                this.Paths = this.Paths.Where(p => this.MetaNodeToActualNode.ContainsKey(p.Key.Item1) && this.MetaNodeToActualNode.ContainsKey(p.Key.Item2)).ToDictionary(p => p.Key, p => p.Value);
            }
            else
            {
                this.Paths = selectedPaths;
            }
            stopwatch.Stop();
            Console.WriteLine($"Execution Time: {stopwatch.ElapsedMilliseconds} ms");
        }
        /// <summary>
        /// Adds the path constraints that bring the path down and switch to backup paths if a lag along that path fails.
        /// For the backup path to be activeated the lag has to come down entirely, it is not enoufh for a link to fail.
        /// This function is different from the one in the capplan adversarial generator because we need to account for the meta nodes and ensure
        /// that the right set of failovers happen.Note when ensuring a connected graph we only ensure that there is at least one path between the metanodes is up.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="ensureAtLeastOneUp"></param>
        protected override void SetPathConstraintsForFailures(ISolver<TVar, TSolution> solver, bool ensureAtLeastOneUp = false)
        {
            foreach (var (pair, paths) in this.Paths)
            {
                Logger.Info("Computing the sum of all paths + setting up link constraints.");
                var atLeastOneUpConstraint = new Polynomial<TVar>();
                foreach (var actualSource in this.MetaNodeToActualNode[pair.Item1])
                {
                    foreach (var actualDest in this.MetaNodeToActualNode[pair.Item2])
                    {
                        var sumOfPathsUpVariables = new Polynomial<TVar>();
                        var candidateList = paths.Select(p => p[1] == actualSource && p[p.Length - 2] == actualDest ? p : null).Where(p => p != null).ToArray();
                        for (var i = 0; i < candidateList.Length; i++)
                        {
                            if (i > 1)
                            {
                                Debug.Assert(candidateList[i - 1].Length <= candidateList[i].Length, "Checking the k shortest path length failed");
                            }
                            var path = candidateList[i];
                            if (ensureAtLeastOneUp)
                            {
                                atLeastOneUpConstraint.Add(new Term<TVar>(1, this.PathUpVariables[path]));
                            }
                            var firstPathUpConstraint = new Polynomial<TVar>(new Term<TVar>(1, this.PathUpVariables[path]));
                            for (int k = 0; k < path.Length - 1; k++)
                            {
                                var source = path[k];
                                var dest = path[k + 1];
                                firstPathUpConstraint.Add(new Term<TVar>(-1, this.LagUpVariables[(source, dest)]));
                                var secondPathUpConstraint = new Polynomial<TVar>(new Term<TVar>(1, this.LagUpVariables[(source, dest)]));
                                secondPathUpConstraint.Add(new Term<TVar>(-1, this.PathUpVariables[path]));
                                solver.AddLeqZeroConstraint(secondPathUpConstraint);
                            }
                            solver.AddLeqZeroConstraint(firstPathUpConstraint);
                            sumOfPathsUpVariables.Add(new Term<TVar>(1, this.PathUpVariables[path]));
                            var sumDisabledPathSoFar = sumOfPathsUpVariables.Copy();
                            sumDisabledPathSoFar.Add(new Term<TVar>(this.maxNumPath - i));
                            var aux = EncodingUtils<TVar, TSolution>.IsLeq(solver, new Polynomial<TVar>(new Term<TVar>(0)), sumDisabledPathSoFar, this.maxNumPath * 10, 0.1);
                            this.AuxiliaryVariables.Add(aux);
                            Debug.Assert(this.PathExtensionCapacityEnforcers.ContainsKey(path), "The path extension capacity enforcers should have been created.");
                            var lowerBound = this.PathExtensionCapacityEnforcers[path].Copy().Negate();
                            lowerBound.Add(new Term<TVar>(this.CapacityUpperBound, aux));
                            solver.AddEqZeroConstraint(lowerBound);
                        }
                    }
                }
                if (ensureAtLeastOneUp)
                {
                    if (paths.Length < 1)
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(-1 * (paths.Length)));
                    }
                    else
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(-1 * (paths.Length - 1)));
                    }
                    solver.AddLeqZeroConstraint(atLeastOneUpConstraint);
                }
            }
        }
        /// <summary>
        /// Creates the indicator variables for links of a given lag being up.
        /// 0 is lag is up and 1 is that it is down.
        /// Again we ensure the links between the lags that have MetaNodes can't go down.
        /// </summary>
        protected override Dictionary<(string, string, string), TVar> CreateLinkUpVariables(ISolver<TVar, TSolution> solver, Topology topology)
        {
            var linkUpVariables = new Dictionary<(string, string, string), TVar>();
            foreach (var lag in topology.GetAllEdges())
            {
                var source = lag.Source;
                var dest = lag.Target;
                foreach (var eachLink in topology.edgeLinks[(source, dest)].Keys)
                {
                    linkUpVariables[(source, dest, topology.edgeLinks[(source, dest)][eachLink].Item1)] = solver.CreateVariable("link_up_" + source + "_" + dest + "_" + eachLink, type: GRB.BINARY);
                    if (this.MetaNodeToActualNode.ContainsKey(source) || this.MetaNodeToActualNode.ContainsKey(dest))
                    {
                        var linkUpConstraint = new Polynomial<TVar>(new Term<TVar>(0));
                        linkUpConstraint.Add(new Term<TVar>(1, linkUpVariables[(source, dest, topology.edgeLinks[(source, dest)][eachLink].Item1)]));
                        solver.AddEqZeroConstraint(linkUpConstraint);
                    }
                }
            }
            return linkUpVariables;
        }
        /// <summary>
        /// Creates the indicator variables for lags being up.
        /// 0 is the lag up and 1 is that it is down.
        /// This function ensures that the lags that originate at MetaNodes are always up.
        /// </summary>
        protected override Dictionary<(string, string), TVar> CreateLagUpVariables(ISolver<TVar, TSolution> solver, Topology topology)
        {
            var lagUpVariables = new Dictionary<(string, string), TVar>();
            foreach (var lag in topology.GetAllEdges())
            {
                var source = lag.Source;
                var dest = lag.Target;
                lagUpVariables[(source, dest)] = solver.CreateVariable("lag_up_" + source + "_" + dest, type: GRB.BINARY);
                if (this.MetaNodeToActualNode.ContainsKey(source) || this.MetaNodeToActualNode.ContainsKey(dest))
                {
                    var lagUpConstraint = new Polynomial<TVar>(new Term<TVar>(0));
                    lagUpConstraint.Add(new Term<TVar>(1, lagUpVariables[(source, dest)]));
                    solver.AddEqZeroConstraint(lagUpConstraint);
                }
            }
            return lagUpVariables;
        }
        /// <summary>
        /// Creates demand variables. Only creates demand variables for MetaNodes and ignores all other nodes.
        /// </summary>
        protected override (Dictionary<(string, string), Polynomial<TVar>>, Dictionary<(string, string), double>) CreateDemandVariables(ISolver<TVar, TSolution> solver,
                                                                                                                                        InnerRewriteMethodChoice innerEncoding,
                                                                                                                                        IList quantizationThresholdsForDemands,
                                                                                                                                        IDictionary<(string, string), double> demandInits = null,
                                                                                                                                        double largeDemandLB = -1,
                                                                                                                                        int largeMaxDistance = -1,
                                                                                                                                        int smallMaxDistance = -1)
        {
            if (largeMaxDistance != -1)
            {
                Debug.Assert(largeMaxDistance >= 1);
                Debug.Assert(largeDemandLB > 0);
                Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
            }
            else
            {
                largeMaxDistance = int.MaxValue;
            }
            if (smallMaxDistance != -1)
            {
                Debug.Assert(smallMaxDistance >= 1);
                Debug.Assert(largeDemandLB > 0);
                Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
            }
            else
            {
                smallMaxDistance = int.MaxValue;
            }
            var demandEnforcers = new Dictionary<(string, string), Polynomial<TVar>>();
            var LocalityConstrainedDemands = new Dictionary<(string, string), double>();
            Logger.Debug("In total " + this.Topology.GetNodePairs().Count() + " pairs");
            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (this.MetaNodeToActualNode.ContainsKey(pair.Item1) == false || this.MetaNodeToActualNode.ContainsKey(pair.Item2) == false)
                {
                    Logger.Info("skipping pair " + pair.Item1 + " " + pair.Item2);
                    continue;
                }
                switch (innerEncoding)
                {
                    case InnerRewriteMethodChoice.KKT:
                        demandEnforcers[pair] = new Polynomial<TVar>(new Term<TVar>(1, solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2)));
                        break;
                    case InnerRewriteMethodChoice.PrimalDual:
                        var demands = quantizationThresholdsForDemands.GetValueForPair(pair.Item1, pair.Item2);
                        demands.Remove(0);
                        var path = this.Topology.ShortestKPaths(1, pair.Item1, pair.Item2);
                        var distance = path.Length > 0 ? path[0].Length - 1 : 1e10;
                        var auxVariableConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                        var demandLvlEnforcer = new Polynomial<TVar>();
                        bool found = false;
                        bool atLeastOneValidLvl = false;
                        foreach (double demandLvl in demands)
                        {
                            // Skip if the distance between the pair is larger than what we allow
                            // or if the demand level is larger than the large demand lower bound.
                            if (distance > largeMaxDistance && demandLvl >= largeDemandLB)
                            {
                                continue;
                            }
                            if (distance > smallMaxDistance && demandLvl < largeDemandLB)
                            {
                                continue;
                            }
                            atLeastOneValidLvl = true;
                            var demandbinaryAuxVar = solver.CreateVariable("aux_demand_" + pair.Item1 + "_" + pair.Item2, type: GRB.BINARY);
                            demandLvlEnforcer.Add(new Term<TVar>(demandLvl, demandbinaryAuxVar));
                            auxVariableConstraint.Add(new Term<TVar>(1, demandbinaryAuxVar));
                            if (demandInits != null)
                            {
                                if (Math.Abs(demandInits[pair] - demandLvl) < 0.0001)
                                {
                                    solver.InitializeVariables(demandbinaryAuxVar, 1);
                                    found = true;
                                }
                                else
                                {
                                    solver.InitializeVariables(demandbinaryAuxVar, 0);
                                }
                            }
                        }
                        if (demandInits != null)
                        {
                            Debug.Assert(found == true || Math.Abs(demandInits[pair]) <= 0.0001);
                        }
                        if (atLeastOneValidLvl)
                        {
                            solver.AddLeqZeroConstraint(auxVariableConstraint);
                        }
                        else
                        {
                            LocalityConstrainedDemands[pair] = 0.0;
                        }
                        demandEnforcers[pair] = demandLvlEnforcer;
                        break;
                    default:
                        throw new Exception("wrong method for inner problem encoder!");
                }
            }
            return (demandEnforcers, LocalityConstrainedDemands);
        }
        /// <summary>
        /// Makes sure that we have enough paths in the path selection and also returns the primary
        /// paths so that we can use them for the optimal formulation.
        /// Notice that the implementation here is different from that in the failureAnalysisAdversarialGenerator.
        /// Specifically, we need to be careful that each actual pair has the correct number of paths.
        /// </summary>
        protected override Dictionary<(string, string), string[][]> VerifyPaths(Dictionary<(string, string), string[][]> paths, int numExtraPaths)
        {
            var pathSubset = new Dictionary<(string, string), string[][]>();
            foreach (var pair in paths.Keys)
            {
                if (!(this.MetaNodeToActualNode.ContainsKey(pair.Item1) && this.MetaNodeToActualNode.ContainsKey(pair.Item2)))
                {
                    throw new Exception("The path computation should have gotten rid of non-meta nodes.");
                }
                pathSubset[pair] = new string[0][];
                double totalNumPaths = 0;
                foreach (var actualSource in this.MetaNodeToActualNode[pair.Item1])
                {
                    foreach (var actualDest in this.MetaNodeToActualNode[pair.Item2])
                    {
                        var candidateList = paths[pair].Select(p => p[1] == actualSource && p[p.Length - 2] == actualDest ? p : null).Where(p => p != null).ToArray();
                        pathSubset[pair] = pathSubset[pair].Concat(candidateList.Take(Math.Min(this.maxNumPath, candidateList.Length))).ToArray();
                        totalNumPaths += Math.Min(this.maxNumPath, candidateList.Length);
                    }
                }
                Debug.Assert(totalNumPaths <= (this.maxNumPath * this.MetaNodeToActualNode[pair.Item1].Count * this.MetaNodeToActualNode[pair.Item2].Count), "The path computation probably has a bug.");
            }
            Debug.Assert(pathSubset != null);
            return pathSubset;
        }
    }
}