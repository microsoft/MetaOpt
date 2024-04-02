// <copyright file="Parser.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using MetaOptimize;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Parsing helper functions.
    /// </summary>
    /// TODO: it would be good to modify to add a debug mode instead of the console.writeline.
    public static class Parser
    {
        /// <summary>
        /// Reads a topology in JSON format.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <param name="pathPath">The path to the file that contains the paths in the topology.</param>
        /// <param name="scaleFactor">To scale topology capacities.</param>
        /// <returns>The topology from the file.</returns>
        public static Topology ReadTopologyJson(string filePath, string pathPath = null, double scaleFactor = 1)
        {
            var text = File.ReadAllText(filePath);
            var obj = (dynamic)JObject.Parse(text);
            var nodes = obj.nodes;
            var edges = obj.links;
            Console.WriteLine("======= " + pathPath);
            var topology = new Topology(pathPath);
            foreach (var node in nodes)
            {
                topology.AddNode(node.id.ToString());
            }

            foreach (var edge in edges)
            {
                // TODO: the number of decimal points should be part of a config file.
                double capacity = Math.Round((double)edge.capacity * scaleFactor, 4);
                // Console.WriteLine(capacity);
                topology.AddEdge(edge.source.ToString(), edge.target.ToString(), capacity);
            }
            return topology;
        }
    }
}
