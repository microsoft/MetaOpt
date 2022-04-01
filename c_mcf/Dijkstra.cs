using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;

namespace MaxConcurrentFlow
{
    public class Dijkstra
    {
        public Dictionary<int, int> Solve(
            BidirectionalGraph<int, Edge<int>> network, int source, int dest , 
            Dictionary<int,double> edgeLengths, bool fullSolve)
        {
            //Console.WriteLine("Entering Dijkstra...");
            Dictionary<int, double> dist = new Dictionary<int, double>();
            VertexComparer vcomp = new VertexComparer(dist);
            SortedSet<int> UnsettledNodes = new SortedSet<int>(vcomp);
            HashSet<int> SettledNodes = new HashSet<int>();
            Dictionary<int, int> prev = new Dictionary<int, int>();

            //initialization
            dist[source] = 0;
            UnsettledNodes.Add(source);

            double alt;
            double smalldist = double.MaxValue;
            int smallvertex = source;

            //find the vertex with smallest value of dist in Unsettled nodes

            while (UnsettledNodes.Count != 0)
            {

                smallvertex = UnsettledNodes.Min;
                smalldist = dist[smallvertex];
                
                bool x = UnsettledNodes.Remove(smallvertex);
                SettledNodes.Add(smallvertex);
                
                if (x == false)
                {
                    Console.WriteLine("Call to Remove failed");
                    Console.WriteLine("Whether it has this key? {0}", UnsettledNodes.Contains(smallvertex));
                    foreach (int m in UnsettledNodes)
                    {
                        Console.WriteLine("{0}      {1}",m,dist[m]);
                    }
                    Console.ReadKey();
                }
                // break if shortest path to target has been comptued
                // do source to all destinations!
                if (!fullSolve && smallvertex == dest) break;

                //case when graph is not connected
                if (dist[smallvertex] == double.MaxValue) break;

                IEnumerable<Edge<int>> outedges;
                network.TryGetOutEdges(smallvertex, out outedges);

                //for each neighbour update the dist values
                foreach (Edge<int> e in outedges)
                {
                    int v = e.Target;
                    if (SettledNodes.Contains(v)) continue;

                    alt = dist[smallvertex] + edgeLengths[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target];

                    if (dist.ContainsKey(v))
                    {
                        if (alt >= dist[v]) continue;
                        else
                            UnsettledNodes.Remove(v);
                    }

                    dist[v] = alt;
                    prev[v] = smallvertex;
                    UnsettledNodes.Add(v);
                }
            }
            return prev;
        }
    }
}
