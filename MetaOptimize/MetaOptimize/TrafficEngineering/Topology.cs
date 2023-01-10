// <copyright file="Topology.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using QuikGraph;
    using QuikGraph.Algorithms.ShortestPath;
    using ZenLib;

    /// <summary>
    /// A simple topology class that wraps a graph.
    /// </summary>
    public class Topology
    {
        /// <summary>
        /// A random number generator.
        /// </summary>
        private Random random;

        /// <summary>
        /// The underlying graph.
        /// </summary>
        public AdjacencyGraph<string, EquatableTaggedEdge<string, double>> Graph { get; set; }

        /// <summary>
        /// the paths between every pairs.
        /// </summary>
        public Dictionary<int, Dictionary<(string, string), string[][]>> paths { get; set; }

        /// <summary>
        /// the path to the file storing paths.
        /// </summary>
        private string pathFile;

        /// <summary>
        /// Creates a new instance of the <see cref="Topology"/> class.
        /// </summary>
        public Topology(string pathFile = null)
        {
            this.random = new Random(0);
            this.Graph = new AdjacencyGraph<string, EquatableTaggedEdge<string, double>>(allowParallelEdges: false);
            this.paths = new Dictionary<int, Dictionary<(string, string), string[][]>>();
            this.pathFile = pathFile;
            Utils.readPathsFromFile(this.pathFile, this.paths);
        }

        /// <summary>
        /// Add a node to the graph.
        /// </summary>
        /// <param name="node">The node name.</param>
        public void AddNode(string node)
        {
            this.Graph.AddVertex(node);
        }

        /// <summary>
        /// Add a directed edge to the graph.
        /// </summary>
        /// <param name="source">The source node.</param>
        /// <param name="target">The target node.</param>
        /// <param name="capacity">The capacity of the edge.</param>
        public void AddEdge(string source, string target, double capacity)
        {
            this.Graph.AddEdge(new EquatableTaggedEdge<string, double>(source, target, capacity));
        }

        /// <summary>
        /// whether the graph contains the specified edge.
        /// </summary>
        /// <param name="source">The source node.</param>
        /// <param name="target">The target node.</param>
        /// <param name="capacity">The capacity of the edge.</param>
        public bool ContaintsEdge(string source, string target, double capacity)
        {
            return this.Graph.ContainsEdge(new EquatableTaggedEdge<string, double>(source, target, capacity));
        }

        /// <summary>
        /// whether the graph contains the specified edge without knowing the capacity.
        /// </summary>
        public bool ContaintsEdge(string source, string target)
        {
            foreach (var edge in this.GetAllEdges()) {
                if (edge.Source == source || edge.Target == target) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get all the nodes in the topology.
        /// </summary>
        /// <returns>The nodes.</returns>
        public IEnumerable<string> GetAllNodes()
        {
            return this.Graph.Vertices;
        }

        /// <summary>
        /// Get all the edges in the topology.
        /// </summary>
        /// <returns>The edges.</returns>
        public IEnumerable<Edge> GetAllEdges()
        {
            foreach (var taggedEdge in this.Graph.Edges)
            {
                yield return new Edge { TaggedEdge = taggedEdge };
            }
        }

        /// <summary>
        /// Gets all the outgoing edges for a given node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>The outbound edge adjacencies.</returns>
        public IEnumerable<Edge> GetOutEdges(string node)
        {
            foreach (var taggedEdge in this.Graph.OutEdges(node))
            {
                yield return new Edge { TaggedEdge = taggedEdge };
            }
        }

        /// <summary>
        /// Enumerate all pairs of nodes.
        /// </summary>
        /// <returns>The node pairs.</returns>
        public IEnumerable<(string, string)> GetNodePairs()
        {
            foreach (var node1 in this.Graph.Vertices)
            {
                foreach (var node2 in this.Graph.Vertices)
                {
                    if (!node1.Equals(node2))
                    {
                        yield return (node1, node2);
                    }
                }
            }
        }

        /// <summary>
        /// Get the edge between a pair of nodes. Throws an exception if no such edge exists.
        /// </summary>
        /// <param name="source">The source node.</param>
        /// <param name="target">The target node.</param>
        /// <returns>The edge between the nodes.</returns>
        public Edge GetEdge(string source, string target)
        {
            if (!this.Graph.TryGetEdge(source, target, out var taggedEdge))
            {
                throw new Exception("No edge between source and target");
            }

            return new Edge { TaggedEdge = taggedEdge };
        }

        /// <summary>
        /// Enumerates all simple paths between two nodes.
        /// </summary>
        /// <param name="source">The source node.</param>
        /// <param name="target">The target node.</param>
        /// <returns>All simple paths between the nodes.</returns>
        public string[][] SimplePaths(string source, string target)
        {
            var paths = new List<string[]>();
            var stack = new Stack<ImmutableList<string>>();
            stack.Push(ImmutableList.Create<string>().Add(source));

            while (stack.Count > 0)
            {
                var path = stack.Pop();
                var current = path[path.Count - 1];

                if (current == target)
                {
                    paths.Add(path.ToArray());
                    continue;
                }

                foreach (var edge in this.GetOutEdges(current))
                {
                    if (!path.Contains(edge.Target))
                    {
                        stack.Push(path.Add(edge.Target));
                    }
                }
            }

            return paths.ToArray();
        }

        /// <summary>
        /// Compute the shortest k paths from a source to a destination.
        /// </summary>
        /// <param name="k">The maximum number of paths.</param>
        /// <param name="source">The source node.</param>
        /// <param name="dest">The destination node.</param>
        public string[][] ShortestKPaths(int k, string source, string dest)
        {
            if (this.paths.ContainsKey(k) && this.paths[k].ContainsKey((source, dest))) {
                return this.paths[k][(source, dest)];
            }

            if (!this.paths.ContainsKey(k)) {
                this.paths[k] = new Dictionary<(string, string), string[][]>();
            }

            var algorithm = new YenShortestPathsAlgorithm<string>(this.Graph, source, dest, k);

            try
            {
                var paths = algorithm.Execute().Select(p =>
                {
                    return Enumerable.Concat(Enumerable.Repeat(source, 1), p.Select(e => e.Target)).ToArray();
                });
                this.paths[k][(source, dest)] = paths.ToArray();
            }
            catch (QuikGraph.NoPathFoundException)
            {
                this.paths[k][(source, dest)] = new string[0][];
            }
            return this.paths[k][(source, dest)];
        }

        /// <summary>
        /// Compute the shortest k paths for a list of src-dst pairs.
        /// </summary>
        /// <param name="k">The maximum number of paths.</param>
        /// <param name="pairList">list of src-dst pairs.</param>
        /// <param name="output">to store output.</param>
        /// <param name="pid">processor id for logging.</param>
        /// <param name="verbose">enables detailed logging.</param>
        public void ShortestKPathsForListPairs(int k, IEnumerable<(string, string)> pairList,
                IDictionary<(string, string), string[][]> output, int pid = -1, bool verbose = false)
        {
            var path_dict = new Dictionary<(string, string), string[][]>();
            Utils.logger("processor with pid " + pid + " starting to compute paths...", verbose);
            foreach (var pair in pairList) {
                // Utils.logger("pid=" + pid + ": finding the paths between " + pair.Item1 + " and " + pair.Item2, verbose);
                var paths = this.ShortestKPaths(k, pair.Item1, pair.Item2);
                path_dict[pair] = paths;
            }
            path_dict.ToList().ForEach(pair => output[pair.Key] = pair.Value);
            Utils.logger("processor with pid " + pid + " done with paths...", verbose);
        }

        /// <summary>
        /// Compute the shortest k paths for all the pairs (multiprocessing).
        /// </summary>
        /// <param name="k">The maximum number of paths.</param>
        /// <param name="numProcesses">The number of processors to use.</param>
        /// <param name="verbose">To show detailed logs.</param>
        public Dictionary<(string, string), string[][]> AllPairsKShortestPathMultiProcessing(int k,
                int numProcesses = -1, bool verbose = false)
        {
            if (this.paths.ContainsKey(k)) {
                Utils.logger("found paths in " + this.pathFile, verbose);
                return this.paths[k];
            } else {
                Utils.logger("did not found paths in " + this.pathFile, verbose);
                this.paths[k] = new Dictionary<(string, string), string[][]>();
            }
            var output = new ConcurrentDictionary<(string, string), string[][]>();
            if (numProcesses < 1) {
                this.ShortestKPathsForListPairs(k, this.GetNodePairs(), output, verbose: verbose);
                this.paths[k] = output.ToDictionary(entry => entry.Key,
                                                entry => entry.Value);
                return output.ToDictionary(entry => entry.Key,
                                           entry => entry.Value);
            }
            // dividing pairs among processes
            Utils.logger("dividing pairs among processes", verbose);
            var processToPairList = new Dictionary<int, List<(string, string)>>();
            int pid = 0;
            foreach (var pair in this.GetNodePairs()) {
                if (!processToPairList.ContainsKey(pid)) {
                    processToPairList[pid] = new List<(string, string)>();
                }
                processToPairList[pid].Add(pair);
                pid = (pid + 1) % numProcesses;
            }
            // starting the processes;
            var threadlist = new List<Thread>();
            foreach (var pid1 in processToPairList.Keys) {
                Utils.logger(
                    string.Format("creating process {0} with {1} pairs", pid1, processToPairList[pid1].Count()),
                    verbose);
                threadlist.Add(new Thread(() => ShortestKPathsForListPairs(k, processToPairList[pid1], output, pid: pid1, verbose: verbose)));
                Utils.logger(
                    string.Format("starting process {0} with {1} pairs", pid1, processToPairList[pid1].Count()),
                    verbose);
                threadlist[pid1].Start();
                Thread.Sleep(1000);
            }
            foreach (var pid1 in processToPairList.Keys) {
                Utils.logger(
                    string.Format("waiting for process {0}", pid1),
                    verbose);
                threadlist[pid1].Join();
            }
            this.paths[k] = output.ToDictionary(entry => entry.Key,
                                                entry => entry.Value);
            if (!String.IsNullOrEmpty(this.pathFile)) {
                Utils.logger("storing the paths in the file " + this.pathFile, verbose);
                Utils.writePathsToFile(this.pathFile, this.paths);
            }
            return output.ToDictionary(entry => entry.Key,
                                       entry => entry.Value);
        }

        /// <summary>
        /// Computes the average shortest path length.
        /// </summary>
        public double avgShortestPathLength()
        {
            double sumLen = 0;
            double numPairs = 0;
            foreach (var pair in this.GetNodePairs()) {
                var path = this.ShortestKPaths(1, pair.Item1, pair.Item2);
                sumLen += path[0].Count();
                numPairs += 1;
            }
            return (sumLen / numPairs);
        }

        /// <summary>
        /// Computes diameter of the topology.
        /// </summary>
        public int diameter()
        {
            int diameter = 0;
            foreach (var pair in this.GetNodePairs()) {
                var path = this.ShortestKPaths(1, pair.Item1, pair.Item2);
                diameter = Math.Max(diameter, path[0].Count());
            }
            return diameter;
        }
        /// <summary>
        /// Randomly partition the pairs of nodes in the network.
        /// </summary>
        /// <param name="numPartitions">The number of partitions.</param>
        /// <returns>The partition mapping.</returns>
        public IDictionary<(string, string), int> RandomPartition(int numPartitions)
        {
            var mapping = new Dictionary<(string, string), int>();
            foreach (var pair in this.GetNodePairs())
            {
                mapping[pair] = this.random.Next(numPartitions);
            }

            return mapping;
        }

        /// <summary>
        /// The total capacity of all the edges in the topology.
        /// </summary>
        /// <returns>The total capacity.</returns>
        public double TotalCapacity()
        {
            double totalCapacity = 0;
            foreach (var edge in this.GetAllEdges())
            {
                totalCapacity = totalCapacity + edge.Capacity;
            }

            return totalCapacity;
        }

        /// <summary>
        /// the maximum link capacity among all the edges in the topology.
        /// </summary>
        /// <returns>The maximum link capacity.</returns>
        public double MaxCapacity()
        {
            double maxCapacity = 0;
            foreach (var edge in this.GetAllEdges()) {
                maxCapacity = Math.Max(maxCapacity, edge.Capacity);
            }
            return maxCapacity;
        }

        /// <summary>
        /// the minimum link capacity among all the edges in the topology.
        /// </summary>
        /// <returns>The maximum link capacity.</returns>
        public double MinCapacity()
        {
            double minCapacity = double.PositiveInfinity;
            foreach (var edge in this.GetAllEdges()) {
                minCapacity = Math.Min(minCapacity, edge.Capacity);
            }
            return minCapacity;
        }

        /// <summary>
        /// Split the capacity of each edge in the topology.
        /// </summary>
        /// <param name="k">The number of copies of the topology.</param>
        /// <returns>A new toplogy with 1/k capacity for each edge.</returns>
        public Topology SplitCapacity(int k)
        {
            var t = new Topology(this.pathFile);
            foreach (var node in this.GetAllNodes())
            {
                t.AddNode(node);
            }

            foreach (var edge in this.GetAllEdges())
            {
                t.AddEdge(edge.Source, edge.Target, edge.Capacity / k);
            }

            return t;
        }

        /// <summary>
        /// Scale the capacity of each edge in the topology.
        /// </summary>
        /// <param name="scaleFactor">the scale factor.</param>
        /// <returns>A new toplogy with scaled capacity for each edge.</returns>
        public Topology ScaleCapacity(double scaleFactor)
        {
            var t = new Topology(this.pathFile);
            foreach (var node in this.GetAllNodes())
            {
                t.AddNode(node);
            }

            foreach (var edge in this.GetAllEdges())
            {
                t.AddEdge(edge.Source, edge.Target, edge.Capacity * scaleFactor);
                Console.WriteLine(t.GetEdge(edge.Source, edge.Target).Capacity);
            }

            return t;
        }

        /// <summary>
        /// create random regular graph.
        /// </summary>
        public static Topology RandomRegularGraph(int numNodes, int radix, double capacity, int seed = 0)
        {
            // References
            // ----------
            // .. [1] A. Steger and N. Wormald,
            //    Generating random regular graphs quickly,
            //    Probability and Computing 8 (1999), 377-396, 1999.
            //    http://citeseer.ist.psu.edu/steger99generating.html.
            // .. [2] Jeong Han Kim and Van H. Vu,
            //    Generating random regular graphs,
            //    Proceedings of the thirty-fifth ACM symposium on Theory of computing,
            //    San Diego, CA, USA, pp 213--222, 2003.
            //    http://portal.acm.org/citation.cfm?id=780542.780576.
            if (radix >= numNodes) {
                throw new Exception("radix should be less than numNodes");
            }
            if (radix * numNodes % 2 != 0) {
                throw new Exception("radix * numNodes should be event.");
            }
            if (radix <= 0) {
                throw new Exception("radix should be positive.");
            }
            Console.WriteLine("Creating random graph.");
            var rng = new Random(seed);
            bool found = false;
            Topology t = null;
            do {
                t = new Topology();
                List<string> nodes = new List<string>();
                Dictionary<string, int> remRadix = new Dictionary<string, int>();
                for (int i = 0; i < numNodes; i++) {
                    t.AddNode(i.ToString());
                    for (int j = 0; j < radix; j++) {
                        nodes.Add(i.ToString());
                    }
                }
                bool stop = false;
                do {
                    for (int i = 0; i < numNodes; i++) {
                        remRadix[i.ToString()] = 0;
                    }
                    var shuffledNodes = nodes.OrderBy(a => rng.Next()).ToList();
                    for (int i = 0; i < shuffledNodes.Count(); i += 2) {
                        var node1 = shuffledNodes[i];
                        var node2 = shuffledNodes[i + 1];
                        if (node1.Equals(node2) | t.ContaintsEdge(node1, node2, capacity)) {
                            remRadix[node1] += 1;
                            remRadix[node2] += 1;
                        } else {
                            t.AddEdge(node1, node2, capacity);
                            t.AddEdge(node2, node1, capacity);
                        }
                    }
                    bool infeas = true;
                    bool allZero = true;
                    foreach (var (n1, r1) in remRadix) {
                        foreach (var (n2, r2) in remRadix) {
                            if (!n1.Equals(n2) & r1 > 0 & r2 > 0) {
                                allZero = false;
                                if (!t.ContaintsEdge(n1, n2, capacity)) {
                                    infeas = false;
                                }
                            }
                        }
                    }
                    nodes = new List<string>();
                    foreach (var (node, remR) in remRadix) {
                        for (int i = 0; i < remR; i++) {
                            nodes.Add(node.ToString());
                        }
                    }
                    if (allZero) {
                        stop = true;
                        found = true;
                    } else if (infeas) {
                        stop = true;
                        found = false;
                    } else {
                        stop = false;
                        found = false;
                    }
                } while (!stop);
            } while (!found);
            return t;
        }

        /// <summary>
        /// create small world graph.
        /// </summary>
        public static Topology SmallWordGraph(int numNodes, int k, double capacity) {
            if (k >= numNodes) {
                throw new Exception("k should be less than numNodes");
            }
            if (k < 0) {
                throw new Exception("k should be positive.");
            }
            if (numNodes < 0) {
                throw new Exception("numNodes should be positive.");
            }
            if (k % 2 != 0) {
                throw new Exception("k should be even.");
            }
            var topo = new Topology();
            for (int i = 0; i < numNodes; i++) {
                topo.AddNode(i.ToString());
            }

            for (int i = 0; i < numNodes; i++) {
                var node1 = i.ToString();
                var pos = k / 2;
                for (int j = 1; j <= pos; j++) {
                    var node2 = ((i + j) % numNodes).ToString();
                    if (!topo.ContaintsEdge(node1, node2, capacity)) {
                        topo.AddEdge(node1, node2, capacity);
                        topo.AddEdge(node2, node1, capacity);
                    }
                }
            }
            return topo;
        }
    }

    /// <summary>
    /// A simple edge class that wraps a TaggedEdge.
    /// </summary>
    public class Edge : IEquatable<Edge>
    {
        /// <summary>
        /// The underlying tagged edge.
        /// </summary>
        internal EquatableTaggedEdge<string, double> TaggedEdge { get; set; }

        /// <summary>
        /// The source of the edge.
        /// </summary>
        public string Source { get => this.TaggedEdge.Source; }

        /// <summary>
        /// The target of the edge.
        /// </summary>
        public string Target { get => this.TaggedEdge.Target; }

        /// <summary>
        /// The capacity of the edge.
        /// </summary>
        public double Capacity { get => this.TaggedEdge.Tag; }

        /// <summary>
        /// Equality for edges.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True or false.</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as Edge);
        }

        /// <summary>
        /// Equality for edges.
        /// </summary>
        /// <param name="other">The other edge.</param>
        /// <returns>True or false.</returns>
        public bool Equals(Edge other)
        {
            return other != null && TaggedEdge.Equals(other.TaggedEdge);
        }

        /// <summary>
        /// The hashcode for an edge.
        /// </summary>
        /// <returns>An integer.</returns>
        public override int GetHashCode()
        {
            return this.TaggedEdge.GetHashCode();
        }
    }
}
