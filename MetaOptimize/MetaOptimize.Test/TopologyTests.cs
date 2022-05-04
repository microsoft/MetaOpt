// <copyright file="TopologyTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;

    /// <summary>
    /// Some basic topology tests.
    /// </summary>
    [TestClass]
    public class TopologyTests
    {
        /// <summary>
        /// Test that maximization works via the kkt conditions.
        /// </summary>
        [TestMethod]
        public void TestPathEnumeration()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddEdge("a", "b", capacity: 10);
            topology.AddEdge("a", "c", capacity: 10);
            topology.AddEdge("b", "d", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);

            var paths = topology.SimplePaths("a", "d").ToArray();

            Assert.AreEqual(2, paths.Length);

            Assert.AreEqual("a", paths[0][0]);
            Assert.AreEqual("c", paths[0][1]);
            Assert.AreEqual("d", paths[0][2]);

            Assert.AreEqual("a", paths[1][0]);
            Assert.AreEqual("b", paths[1][1]);
            Assert.AreEqual("d", paths[1][2]);
        }

        /// <summary>
        /// Test that computing k shortest paths works.
        /// </summary>
        [TestMethod]
        public void TestKShortestPaths1()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddEdge("a", "b", capacity: 10);
            topology.AddEdge("a", "c", capacity: 10);
            topology.AddEdge("b", "d", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);

            var paths = topology.ShortestKPaths(1, "a", "d").ToArray();

            Assert.AreEqual(1, paths.Length);

            Assert.AreEqual("a", paths[0][0]);
            Assert.AreEqual("b", paths[0][1]);
            Assert.AreEqual("d", paths[0][2]);

            paths = topology.ShortestKPaths(2, "a", "d").ToArray();

            Assert.AreEqual(2, paths.Length);

            Assert.AreEqual("a", paths[0][0]);
            Assert.AreEqual("b", paths[0][1]);
            Assert.AreEqual("d", paths[0][2]);

            Assert.AreEqual("a", paths[1][0]);
            Assert.AreEqual("c", paths[1][1]);
            Assert.AreEqual("d", paths[1][2]);
        }

        /// <summary>
        /// Test that computing k shortest paths works.
        /// </summary>
        [TestMethod]
        public void TestKShortestPaths2()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddEdge("a", "b", capacity: 10);
            topology.AddEdge("a", "c", capacity: 8);
            topology.AddEdge("a", "d", capacity: 10);
            topology.AddEdge("b", "d", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);

            var paths = topology.ShortestKPaths(4, "a", "d").ToArray();

            Assert.AreEqual(3, paths.Length);

            Assert.AreEqual("a", paths[0][0]);
            Assert.AreEqual("d", paths[0][1]);

            Assert.AreEqual("a", paths[1][0]);
            Assert.AreEqual("c", paths[1][1]);
            Assert.AreEqual("d", paths[1][2]);

            Assert.AreEqual("a", paths[2][0]);
            Assert.AreEqual("b", paths[2][1]);
            Assert.AreEqual("d", paths[2][2]);
        }
    }
}