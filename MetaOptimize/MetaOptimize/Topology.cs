// <copyright file="Topology.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using QuikGraph;

    /// <summary>
    /// A simple topology class that wraps a graph.
    /// </summary>
    public class Topology
    {
        /// <summary>
        /// The underlying graph.
        /// </summary>
        public AdjacencyGraph<string, TaggedEdge<string, Real>> Graph { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="Topology"/> class.
        /// </summary>
        public Topology()
        {
            this.Graph = new AdjacencyGraph<string, TaggedEdge<string, Real>>(allowParallelEdges: false);
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
        public void AddEdge(string source, string target, Real capacity)
        {
            this.Graph.AddEdge(new TaggedEdge<string, Real>(source, target, capacity));
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
        public IEnumerable<IList<string>> SimplePaths(string source, string target)
        {
            var stack = new Stack<ImmutableList<string>>();
            stack.Push(ImmutableList.Create<string>().Add(source));

            while (stack.Count > 0)
            {
                var path = stack.Pop();
                var current = path[path.Count - 1];

                if (current == target)
                {
                    yield return path;
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
        }

        /// <summary>
        /// The maximum capacity on any edge in the topology.
        /// </summary>
        /// <returns>The maximum capacity.</returns>
        public Real TotalCapacity()
        {
            var totalCapacity = new Real(0);
            foreach (var edge in this.GetAllEdges())
            {
                totalCapacity = totalCapacity + edge.Capacity;
            }

            return totalCapacity;
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
                t.AddEdge(edge.Source, edge.Target, edge.Capacity * new Real(1, k));
            }

            return t;
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
        internal TaggedEdge<string, Real> TaggedEdge { get; set; }

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
        public Real Capacity { get => this.TaggedEdge.Tag; }

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
