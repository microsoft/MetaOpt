using System;
using System.Collections.Generic;
using QuickGraph;

namespace MaxConcurrentFlow
{
    public class Path
    {
        public int source;
        public int target;

        public double length;
        public List<Edge<int>> edgesList;

        public Path(int src, int dest)
        {
            source = src;
            target = dest;
            edgesList = new List<Edge<int>>();

            length = 0;
        }

        public double getPathLength(Dictionary<Tuple<int,int>,double> edgeLengths)
        {
            double len = 0;
            foreach(Edge<int> e in edgesList)
            {
                len += edgeLengths[new Tuple<int,int>(e.Source,e.Target)];
            }
            length = len;
            return len;
        }

    }
}
