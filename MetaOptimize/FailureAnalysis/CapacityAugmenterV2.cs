namespace MetaOptimize.FailureAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using Gurobi;
    /// <summary>
    /// This class runs an E/M style algorithm to augment the capacity until
    /// it is resilient to failures with a given failure probability or to a specific
    /// number of maximum failures.
    /// </summary>
    public class CapacityAugmenterV2<TVar, TSolution>
    {
        private Dictionary<String, HashSet<string>> MetaNodeToActualNode = null;
        /// <summary>
        /// The initial topology that we are starting with.
        /// </summary>
        /// <value></value>
        public Topology Topology { get; set; }
        /// <summary>
        /// The solver we use.
        /// </summary>
        /// <value></value>
        public ISolver<TVar, TSolution> Solver { get; set; }
        /// <summary>
        /// The probability with which each link can fail.
        /// </summary>
        public Dictionary<(string, string, string), double> LinkFailureProbabilities { get; set; }
        /// <summary>
        /// The number of paths to use for TE.
        /// </summary>
        public int MaxNumPaths { get; set; }
        /// <summary>
        /// The class initializer.
        /// The failure probability and metanode map are optional but have implications if they are not provided.
        /// </summary>
        /// <returns></returns>
        public CapacityAugmenterV2(Topology topo, int maxNumPaths, Dictionary<(string, string, string), double> failureProbs = null, Dictionary<string, HashSet<string>> MetaNodeToActualNode = null)
        {
            this.Topology = topo;
            this.LinkFailureProbabilities = failureProbs;
            this.MetaNodeToActualNode = MetaNodeToActualNode;
            this.MaxNumPaths = maxNumPaths;
        }
        private Topology getDownedTopo(Dictionary<(string, string, string), int> downedLinks, Dictionary<(string, string), string[][]> paths)
        {
            Debug.Assert(downedLinks.Count > 0);
            var t = new Topology();
            var downedEdges = downedLinks.GroupBy(kvp => (kvp.Key.Item1, kvp.Key.Item1))
                                         .ToDictionary(g => g.Key,
                                                       g => g.Sum(kvp => kvp.Value));
            foreach (var node in this.Topology.GetAllNodes())
            {
                t.AddNode(node);
            }
            var linksBefore = this.Topology.edgeLinks.SelectMany(x => x.Value.Keys).Sum();
            var lagsBefore = this.Topology.GetAllEdges().Count();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                if (downedEdges.ContainsKey((edge.Source, edge.Target)))
                {
                    var numberOfLinksInOriginal = this.Topology.edgeLinks[(edge.Source, edge.Target)].Count;
                    Debug.Assert(downedEdges[(edge.Source, edge.Target)] <= numberOfLinksInOriginal);
                    if (downedEdges[(edge.Source, edge.Target)] == numberOfLinksInOriginal)
                    {
                        continue;
                    }
                    var capacity = edge.Capacity * (numberOfLinksInOriginal - downedEdges[(edge.Source, edge.Target)]) / numberOfLinksInOriginal;
                    t.AddEdge(edge.Source, edge.Target, capacity);
                    foreach (var link in this.Topology.edgeLinks[(edge.Source, edge.Target)].Keys)
                    {
                        if (downedLinks.ContainsKey((edge.Source, edge.Target, this.Topology.edgeLinks[(edge.Source, edge.Target)][link].Item1)))
                        {
                            continue;
                        }
                        t.AddLinkToEdge(edge.Source, edge.Target, this.Topology.edgeLinks[(edge.Source, edge.Target)][link].Item1, this.Topology.edgeLinks[(edge.Source, edge.Target)][link].Item2);
                    }
                }
                else
                {
                    var capacity = edge.Capacity;
                    if (this.MetaNodeToActualNode.ContainsKey(edge.Source))
                    {
                        var pathSet = paths.Where(demands => demands.Key.Item1 == edge.Source)
                                           .SelectMany(path => path.Value).Where(path => path[1] == edge.Target).ToList();
                        capacity = pathSet.Select(path => path.Zip(path.Skip(1), (from, to) => this.Topology.GetEdge(from, to).Capacity).ToList().Min()).Sum();
                    }
                    if (this.MetaNodeToActualNode.ContainsKey(edge.Target))
                    {
                        var pathSet = paths.Where(demands => demands.Key.Item2 == edge.Target)
                                           .SelectMany(path => path.Value).Where(path => path[path.Count() - 2] == edge.Source).ToList();
                        capacity = pathSet.Select(path => path.Zip(path.Skip(1), (from, to) => this.Topology.GetEdge(from, to).Capacity).ToList().Min()).Sum();
                    }
                    t.AddEdge(edge.Source, edge.Target, capacity);
                    t.edgeLinks[(edge.Source, edge.Target)] = this.Topology.edgeLinks[(edge.Source, edge.Target)];
                }
            }
            var linksAfter = t.edgeLinks.SelectMany(x => x.Value.Keys).Sum();
            var lagsAfter = t.GetAllEdges().Count();
            Debug.Assert((linksAfter < linksBefore) || (lagsAfter < lagsBefore), $"Links after are {linksAfter} and linksBefore are {linksBefore}");
            return t;
        }
        private Topology getDownedTopo(Dictionary<(string, string, string), int> downedLinks, bool addFailed = false)
        {
            Debug.Assert(downedLinks.Count > 0);
            var t = new Topology();
            var downedEdges = downedLinks.GroupBy(kvp => (kvp.Key.Item1, kvp.Key.Item2))
                                         .ToDictionary(g => g.Key,
                                                       g => g.Sum(kvp => kvp.Value));
            foreach (var node in this.Topology.GetAllNodes())
            {
                t.AddNode(node);
            }
            var linksBefore = this.Topology.edgeLinks.SelectMany(x => x.Value.Keys).Sum();
            var lagsBefore = this.Topology.GetAllEdges().Count();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                if (downedEdges.ContainsKey((edge.Source, edge.Target)))
                {
                    var numberOfLinksInOriginal = this.Topology.edgeLinks[(edge.Source, edge.Target)].Count;
                    Debug.Assert(downedEdges[(edge.Source, edge.Target)] <= numberOfLinksInOriginal);
                    if (downedEdges[(edge.Source, edge.Target)] == numberOfLinksInOriginal)
                    {
                        if (addFailed)
                        {
                            t.AddEdge(edge.Source, edge.Target, 0);
                        }
                        continue;
                    }
                    var capacity = edge.Capacity * (numberOfLinksInOriginal - downedEdges[(edge.Source, edge.Target)]) / numberOfLinksInOriginal;
                    t.AddEdge(edge.Source, edge.Target, capacity);
                    foreach (var link in this.Topology.edgeLinks[(edge.Source, edge.Target)].Keys)
                    {
                        if (downedLinks.ContainsKey((edge.Source, edge.Target, this.Topology.edgeLinks[(edge.Source, edge.Target)][link].Item1)))
                        {
                            continue;
                        }
                        t.AddLinkToEdge(edge.Source, edge.Target, this.Topology.edgeLinks[(edge.Source, edge.Target)][link].Item1, this.Topology.edgeLinks[(edge.Source, edge.Target)][link].Item2);
                    }
                }
                else
                {
                    t.AddEdge(edge.Source, edge.Target, edge.Capacity);
                    t.edgeLinks[(edge.Source, edge.Target)] = this.Topology.edgeLinks[(edge.Source, edge.Target)];
                }
            }
            var linksAfter = t.edgeLinks.SelectMany(x => x.Value.Keys).Sum();
            var lagsAfter = t.GetAllEdges().Count();
            if (!addFailed)
            {
                Debug.Assert((linksAfter < linksBefore) || (lagsAfter < lagsBefore), $"Links after are {linksAfter} and linksBefore are {linksBefore}");
            }
            return t;
        }
        // Todo : currently assumes links that are augmented don't fail. Can modify later to make this more general with other probability estimates.
        private void augmentTopo(Dictionary<(string, string), double> augmentedLags, bool useLagCapacity = false, int iteration = 0)
        {
            var avgCapacity = this.Topology.GetAllEdges().Where(x => x.Capacity > 0).Select(x => x.Capacity).Average();
            var initialLags = this.Topology.GetAllEdges().Count();
            foreach (var (source, dest) in augmentedLags.Keys)
            {
                if (this.Topology.Graph.TryGetEdge(source, dest, out var taggedEdgeVar))
                {
                    double capacity = taggedEdgeVar.Tag;
                    this.Topology.RemoveEdge(taggedEdgeVar);
                    this.Topology.AddEdge(source, dest, capacity + avgCapacity);
                    var linkFailureProb = this.LinkFailureProbabilities.Where(x => x.Key.Item1 == source && x.Key.Item2 == dest).Select(x => x.Value).Average();
                    this.Topology.AddLinkToEdge(source, dest, $"augmented-{iteration}", linkFailureProb);
                    this.LinkFailureProbabilities[(source, dest, $"augmented-{iteration}")] = linkFailureProb;
                    continue;
                }
                if (useLagCapacity)
                {
                    avgCapacity = augmentedLags[(source, dest)];
                }
                this.Topology.AddEdge(source, dest, avgCapacity);
                this.Topology.AddLinkToEdge(source, dest, "augmented", 0);
                this.LinkFailureProbabilities[(source, dest, "agumented")] = 0;
            }
            var finalLags = this.Topology.GetAllEdges().Count();
            Debug.Assert(finalLags > initialLags, $"Number of final lags {finalLags} and initial lags {initialLags}");
        }
        /// <summary>
        /// Todo: currently assumes links that are augmented don't fail.
        /// </summary>
        /// <param name="augmentedLags"></param>
        /// <param name="iteration"></param>
        private void augmentExistingTopo(Dictionary<(string, string), double> augmentedLags, int iteration = 0)
        {
            foreach (var (source, dest) in augmentedLags.Keys)
            {
                if (this.Topology.Graph.TryGetEdge(source, dest, out var taggedEdgeVar))
                {
                    double capacity = taggedEdgeVar.Tag;
                    this.Topology.RemoveEdge(taggedEdgeVar);
                    this.Topology.AddEdge(source, dest, capacity + augmentedLags[(source, dest)]);
                    var linkFailureProb = this.LinkFailureProbabilities.Where(x => x.Key.Item1 == source && x.Key.Item2 == dest).Select(x => x.Value).Average();
                    this.Topology.AddLinkToEdge(source, dest, $"augmented-{iteration}", linkFailureProb);
                    this.LinkFailureProbabilities[(source, dest, $"augmented-{iteration}")] = linkFailureProb;
                }
            }
        }
        private void updateClusters(List<Topology> clusters, Dictionary<(string, string), double> augmentedLags)
        {
            var minCapacity = this.Topology.GetAllEdges().Where(x => x.Capacity > 0).Select(x => x.Capacity).Min();
            foreach (var (source, dest) in augmentedLags.Keys)
            {
                foreach (var cluster in clusters)
                {
                    if (!cluster.GetAllNodes().Contains(source))
                    {
                        continue;
                    }
                    if (!cluster.GetAllNodes().Contains(dest))
                    {
                        continue;
                    }
                    cluster.AddEdge(source, dest, minCapacity);
                    break;
                }
            }
        }
        private void updateExistingClusters(List<Topology> clusters, Dictionary<(string, string), double> augmentedLags)
        {
            var minCapacity = this.Topology.GetAllEdges().Where(x => x.Capacity > 0).Select(x => x.Capacity).Min();
            foreach (var (source, dest) in augmentedLags.Keys)
            {
                foreach (var cluster in clusters)
                {
                    if (!cluster.GetAllNodes().Contains(source))
                    {
                        continue;
                    }
                    if (!cluster.GetAllNodes().Contains(dest))
                    {
                        continue;
                    }
                    if (cluster.Graph.TryGetEdge(source, dest, out var edge))
                    {
                        var capacity = edge.Tag;
                        cluster.RemoveEdge(edge);
                        cluster.AddEdge(source, dest, capacity + augmentedLags[(source, dest)]);
                    }
                    break;
                }
            }
        }
        /// <summary>
        /// This function fixes the topology so that it is resilient to failures that are more than
        /// X% likely or to a particular number of failures.
        /// We allow the function to be run with constrained or unconstrained demands.
        /// </summary>
        public Topology AugMentCapacity(ISolver<TVar, TSolution> solver,
                                        List<Topology> clusters = null,
                                        int maxNumFailures = -1,
                                        double failureProbThreshold = -1,
                                        double dmeandUB = -1,
                                        GenericList demandList = null,
                                        IDictionary<(string, string), double> constrainedDemands = null,
                                        IDictionary<(string, string), double> preDemandUB = null,
                                        int numExtraPaths = 0,
                                        bool ensureConnectedGraph = false,
                                        List<string> relayFilter = null,
                                        bool optimalIsConstant = false,
                                        int maxNumIterations = 100,
                                        bool storeProgress = true,
                                        string storeLocation = "..//",
                                        int minAugmentation = 1)
        {
            this.Solver = solver;
            if (maxNumFailures < 0 && failureProbThreshold < 0)
            {
                throw new Exception("This function is not meant to be used this way.");
            }
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, maxNumPaths: this.MaxNumPaths);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, this.MaxNumPaths);
            var capacityAugmenter = new CapacityAugmentEncoder<TVar, TSolution>(solver, this.MetaNodeToActualNode);
            var augmentedLags = new Dictionary<(string, string), double>();
            double oldTarget = 0;
            double increase = 1.1;
            for (var i = 0; i < maxNumIterations; i++)
            {
                // TODO: for each link that I add remember to augment the failure probability.
                // TODO: can add to the implementation so that the user specifies how to calculate.
                if (i != 0)
                {
                    augmentTopo(augmentedLags, iteration: i);
                    updateClusters(clusters, augmentedLags);
                }
                if (MetaNodeToActualNode == null)
                {
                    throw new Exception("Need to implement for that scenario and i haven't yet");
                }
                TEOptimizationSolution optimalSol = null;
                FailureAnalysisOptimizationSolution failureSol = null;
                var adversarialGenerator = new FailureAnalysisWithMetaNodeAdversarialGenerator<TVar, TSolution>(this.Topology, this.MaxNumPaths, metaNodeToActualNodes: this.MetaNodeToActualNode);
                if (constrainedDemands != null || clusters == null)
                {
                    this.Solver.CleanAll();
                    (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder,
                                                                                          optimalCutEncoder,
                                                                                          demandList: demandList,
                                                                                          perDemandUB: preDemandUB,
                                                                                          constrainedDemands: constrainedDemands,
                                                                                          numExtraPaths: numExtraPaths,
                                                                                          maxNumFailures: maxNumFailures,
                                                                                          failureProbThreshold: failureProbThreshold,
                                                                                          useLinkFailures: true,
                                                                                          innerEncoding: InnerRewriteMethodChoice.PrimalDual,
                                                                                          ensureConnectedGraph: ensureConnectedGraph,
                                                                                          linkFailureProbabilities: this.LinkFailureProbabilities,
                                                                                          relayFilter: relayFilter,
                                                                                          optimalIsConstant: optimalIsConstant);
                }
                else
                {
                    (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGapWithClustering(clusters,
                                                                                                        optimalEncoder,
                                                                                                        optimalCutEncoder,
                                                                                                        demandList: demandList,
                                                                                                        perDemandUB: preDemandUB,
                                                                                                        numExtraPaths: numExtraPaths,
                                                                                                        maxNumFailures: maxNumFailures,
                                                                                                        failureProbThreshold: failureProbThreshold,
                                                                                                        useLinkFailures: true,
                                                                                                        innerEncoding: InnerRewriteMethodChoice.PrimalDual,
                                                                                                        ensureConnectedGraph: ensureConnectedGraph,
                                                                                                        linkFailureProbabilities: this.LinkFailureProbabilities,
                                                                                                        relayFilter: relayFilter);
                }
                var paths = adversarialGenerator.ReturnPaths();
                var downedLinks = new Dictionary<(string, string, string), int>();
                foreach (var (name, link) in adversarialGenerator.LinkUpVariables)
                {
                    var lag_val = this.Solver.GetVariable(solver.GetModel(), link);
                    if (lag_val >= 0.9999 && lag_val <= 1.0001)
                    {
                        downedLinks[name] = 1;
                    }
                }
                if (storeProgress)
                {
                    if (!Directory.Exists(storeLocation))
                    {
                        Directory.CreateDirectory(storeLocation);
                    }
                    string outputFile = Path.Combine(storeLocation, "storeProgress.csv");
                    using (StreamWriter writer = new StreamWriter(outputFile, true))
                    {
                        writer.WriteLine($"{i}, {optimalSol.MaxObjective - failureSol.MaxObjective}");
                    }
                }
                if (optimalSol.MaxObjective - failureSol.MaxObjective <= 1e-3)
                {
                    break;
                }
                var demands = new Dictionary<(string, string), double>(optimalSol.Demands);
                var flows = new Dictionary<(string, string), double>(((TEMaxFlowOptimizationSolution)optimalSol).Flows);
                var downedTopo = getDownedTopo(downedLinks, paths);
                var downedEdges = downedLinks.GroupBy(kvp => (kvp.Key.Item1, kvp.Key.Item2))
                                             .ToDictionary(g => g.Key,
                                                           g => g.Sum(kvp => kvp.Value));
                this.Solver.CleanAll();
                var target = optimalSol.MaxObjective;
                if (Math.Round(oldTarget, 2) <= Math.Round(target, 2))
                {
                    oldTarget = target;
                    target = target * increase;
                    demands = demands.ToDictionary(entry => entry.Key,
                                                   entry => entry.Value * increase);
                    increase += 1;
                }
                else
                {
                    increase = 1.1;
                    oldTarget = target;
                }
                var augmenterEncoding = capacityAugmenter.Encoding(downedTopo,
                                                                   demands,
                                                                   flows,
                                                                   target,
                                                                   minAugmentation,
                                                                   downedEdges);
                var solution = this.Solver.Maximize(augmenterEncoding.MaximizationObjective);
                var augmentationSolution = capacityAugmenter.GetSolution(solution);
                augmentedLags.Clear();
                foreach (var lag in ((CapacityAugmentSolution)augmentationSolution).LagStatus.Keys)
                {
                    if (((CapacityAugmentSolution)augmentationSolution).LagStatus[lag] >= 0.9999 && ((CapacityAugmentSolution)augmentationSolution).LagStatus[lag] <= 1.0001)
                    {
                        augmentedLags[lag] = 1;
                    }
                }
                if (storeProgress)
                {
                    string outputFile = Path.Combine(storeLocation, "storedProgressLags.csv");
                    using (StreamWriter writer = new StreamWriter(outputFile, true))
                    {
                        writer.WriteLine($"{i}, {augmentedLags.Count}, {augmentedLags.Select(x => x.Value).Sum()}");
                    }
                }
            }
            return this.Topology;
        }
        /// <summary>
        /// Main difference with the function above is that it only uses existing lags instead of new ones.
        /// </summary>
        public Topology AugmentCapacityExisting(ISolver<TVar, TSolution> solver,
                                                List<Topology> clusters = null,
                                                int maxNumFailures = -1,
                                                double failureProbThreshold = -1,
                                                double demandUB = -1,
                                                GenericList demandList = null,
                                                IDictionary<(string, string), double> constrainedDemands = null,
                                                IDictionary<(string, string), double> perDemandUB = null,
                                                int numExtraPaths = 0,
                                                bool ensureConnectedGraph = false,
                                                List<string> relayFilter = null,
                                                bool optimalIsConstant = false,
                                                int maxNumIterations = 50,
                                                bool storedProgress = true,
                                                string storeLocation = "..//",
                                                int minAugmentation = 1)
        {
            this.Solver = solver;
            if (maxNumFailures < 0 && failureProbThreshold < 0)
            {
                throw new Exception("This function is not meant to be used this way");
            }
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, maxNumPaths: this.MaxNumPaths);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, this.MaxNumPaths);
            var capacityAugmenter = new CapacityAugmentsOnExisting<TVar, TSolution>(solver, this.MetaNodeToActualNode, this.MaxNumPaths);
            var augmentedLags = new Dictionary<(string, string), double>();
            for (var i = 0; i < maxNumIterations; i++)
            {
                if (i != 0)
                {
                    augmentExistingTopo(augmentedLags, iteration: i);
                    updateExistingClusters(clusters, augmentedLags);
                }
                if (MetaNodeToActualNode == null)
                {
                    throw new Exception("Need to implement for that scenario and I haven't yet");
                }
                TEOptimizationSolution optimalSol = null;
                FailureAnalysisOptimizationSolution failureSol = null;
                var adversarialGenerator = new FailureAnalysisWithMetaNodeAdversarialGenerator<TVar, TSolution>(this.Topology, this.MaxNumPaths, metaNodeToActualNodes: this.MetaNodeToActualNode);
                if (constrainedDemands != null || clusters == null)
                {
                    this.Solver.CleanAll();
                    (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder,
                                                                                          optimalCutEncoder,
                                                                                          demandList: demandList,
                                                                                          perDemandUB: perDemandUB,
                                                                                          constrainedDemands: constrainedDemands,
                                                                                          numExtraPaths: numExtraPaths,
                                                                                          maxNumFailures: maxNumFailures,
                                                                                          failureProbThreshold: failureProbThreshold,
                                                                                          useLinkFailures: true,
                                                                                          innerEncoding: InnerRewriteMethodChoice.PrimalDual,
                                                                                          ensureConnectedGraph: ensureConnectedGraph,
                                                                                          linkFailureProbabilities: LinkFailureProbabilities,
                                                                                          relayFilter: relayFilter,
                                                                                          optimalIsConstant: optimalIsConstant);
                }
                else
                {
                    (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGapWithClustering(clusters,
                                                                                                        optimalEncoder,
                                                                                                        optimalCutEncoder,
                                                                                                        demandList: demandList,
                                                                                                        perDemandUB: perDemandUB,
                                                                                                        numExtraPaths: numExtraPaths,
                                                                                                        maxNumFailures: maxNumFailures,
                                                                                                        failureProbThreshold: failureProbThreshold,
                                                                                                        useLinkFailures: true,
                                                                                                        innerEncoding: InnerRewriteMethodChoice.PrimalDual,
                                                                                                        ensureConnectedGraph: ensureConnectedGraph,
                                                                                                        linkFailureProbabilities: this.LinkFailureProbabilities,
                                                                                                        relayFilter: relayFilter);
                }
                var paths = adversarialGenerator.ReturnPaths();
                var downedLinks = new Dictionary<(string, string, string), int>();
                foreach (var (name, lag) in adversarialGenerator.LinkUpVariables)
                {
                    var lag_val = this.Solver.GetVariable(solver.GetModel(), lag);
                    if (lag_val >= 0.9999 && lag_val <= 1.0001)
                    {
                        downedLinks[name] = 1;
                    }
                }
                if (storedProgress)
                {
                    if (!Directory.Exists(storeLocation))
                    {
                        Directory.CreateDirectory(storeLocation);
                    }
                    string outputFile = Path.Combine(storeLocation, "storeProgress.csv");
                    using (StreamWriter writer = new StreamWriter(outputFile, true))
                    {
                        writer.WriteLine($"{i}, {optimalSol.MaxObjective - failureSol.MaxObjective}");
                    }
                }
                if (optimalSol.MaxObjective - failureSol.MaxObjective <= 1e-3)
                {
                    break;
                }
                var demands = new Dictionary<(string, string), double>(optimalSol.Demands);
                var flows = new Dictionary<(string, string), double>(((TEMaxFlowOptimizationSolution)optimalSol).Flows);
                var downedTopo = getDownedTopo(downedLinks, true);
                var downedEdges = downedLinks.GroupBy(kvp => (kvp.Key.Item1, kvp.Key.Item2))
                                             .ToDictionary(g => g.Key,
                                                           g => g.Sum(kvp => kvp.Value));
                this.Solver.CleanAll();
                var target = optimalSol.MaxObjective;
                var augmenterEncoding = capacityAugmenter.Encoding(downedTopo,
                                                                   demands,
                                                                   flows,
                                                                   target);
                var solution = this.Solver.Maximize(augmenterEncoding.MaximizationObjective);
                var augmenterObjective = this.Solver.GetVariable(solver.GetModel(), capacityAugmenter.TotalDemandMetVariable);
                Debug.Assert(augmenterObjective >= target, "WE did not meet target demand");
                var augmentationSolution = capacityAugmenter.GetSolution(solution);
                augmentedLags.Clear();
                var count = 0;
                double cap = 0;
                foreach (var lag in ((CapacityAugmentSolution)augmentationSolution).LagStatus.Keys)
                {
                    augmentedLags[lag] = ((CapacityAugmentSolution)augmentationSolution).LagStatus[lag];
                    if (augmentedLags[lag] > 0)
                    {
                        var edge = this.Topology.Graph.TryGetEdge(lag.Item1, lag.Item2, out var taggedEdge);
                        Console.WriteLine($"This is the lag that we augmented to {lag} and by {augmentedLags[lag]} much old capacity was: {taggedEdge.Tag}");
                        count += 1;
                        cap += augmentedLags[lag];
                    }
                }
                if (storedProgress)
                {
                    string outputFile = Path.Combine(storeLocation, "StoredProgressLinks.csv");
                    using (StreamWriter writer = new StreamWriter(outputFile, true))
                    {
                        writer.WriteLine($"{i}, {count}, {cap}");
                    }
                }
            }
            return this.Topology;
        }
    }
}