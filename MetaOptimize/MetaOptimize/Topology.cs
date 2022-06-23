// <copyright file="Topology.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
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
        /// Creates a new instance of the <see cref="Topology"/> class.
        /// </summary>
        public Topology()
        {
            this.random = new Random(0);
            this.Graph = new AdjacencyGraph<string, EquatableTaggedEdge<string, double>>(allowParallelEdges: false);
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
            var algorithm = new YenShortestPathsAlgorithm<string>(this.Graph, source, dest, k);

            try
            {
                var paths = algorithm.Execute().Select(p =>
                {
                    return Enumerable.Concat(Enumerable.Repeat(source, 1), p.Select(e => e.Target)).ToArray();
                });

                return paths.ToArray();
            }
            catch (QuikGraph.NoPathFoundException)
            {
                return new string[0][];
            }
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
            var t = new Topology();
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
