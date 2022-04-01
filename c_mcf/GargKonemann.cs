using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;


namespace MaxConcurrentFlow
{
    public class GargKonemann
    {
        private Dictionary<Tuple<int,int>,double> edgeLengths;
        private Dictionary<Tuple<int,int>,double> edgeCapacities;
        private List<Request> requests;
        private Dictionary<Tuple<int, int>, double> edgeFlows = new Dictionary<Tuple<int, int>, double>();
        private double epsilon;

        public GargKonemann(Dictionary<Tuple<int, int>, double> edgecap, Dictionary<Tuple<int, int>,double> edgelen, Dictionary<Tuple<int, int>, double> edgeflows, List<Request> reqs, double eps)
        {
            edgeLengths = edgelen;
            edgeCapacities = edgecap;
            requests = reqs;
            edgeFlows = edgeflows;
            epsilon = eps;

        }
        public void Run(BidirectionalGraph<int,Edge<int>> network)
        {
            int totalEdges = network.EdgeCount;
            int reqCount = requests.Count;

            double delta = Math.Pow((totalEdges / (1 - epsilon)), -1 / epsilon);

            double initLength;
            int phases = 0;
            double D = delta * totalEdges;
            double tempDemand;
            Path shortestPath;
            double minCap;
            Edge<int> minCapEdge;
            double tempu;
            double changeInD;
            int minT;
            double minDist;
            double tempLength;
            SortedDictionary<double, List<int>> shortestPathIndies;
            Dictionary<int, Path> pathsAtTimeT;
            Dijkstra dijkstra = new Dijkstra();
            double[] flow = new double[reqCount];
            int tSource, tDest;

            //initialize lengths
            foreach (Edge<int> e in network.Edges)
            {
                initLength = delta / edgeCapacities[new Tuple<int, int>(e.Source, e.Target)];
                edgeLengths[new Tuple<int, int>(e.Source, e.Target)] = initLength;
            }

            //initialize total flow of each user to 0
            for (int f = 0; f < reqCount; f++)
            {
                flow[f] = 0;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();


            //Garg-Konemann's algorithm
            while (D < 1)
            {
                phases++;
                Console.WriteLine("Phase: {0}", phases);

                //iterate over requests
                for (int j = 0; j < reqCount; j++)
                {
                    tSource = requests.ElementAt(j).src;
                    tDest = requests.ElementAt(j).dest;

                    shortestPathIndies = new SortedDictionary<double, List<int>>();
                    pathsAtTimeT = new Dictionary<int, Path>();

                    for (int t = requests.ElementAt(j).arrival; t <= requests.ElementAt(j).deadline; t++)
                    {
                        shortestPath = dijkstra.Solve(network, (t << 10) ^ tSource, (t << 10) ^ tDest, edgeLengths);
                        tempLength = shortestPath.length;
                        if (!shortestPathIndies.ContainsKey(tempLength))
                        {
                            shortestPathIndies[tempLength] = new List<int>();
                        }
                        shortestPathIndies[tempLength].Add(t);
                        pathsAtTimeT[t] = shortestPath;
                    }

                    tempDemand = requests.ElementAt(j).demand;
                    while (D < 1 && tempDemand > 0)
                    {
                        //find the shortest path from source to destination
                        minDist = shortestPathIndies.Keys.Min();
                        minT = shortestPathIndies[minDist].First();
                        shortestPathIndies[minDist].Remove(minT);

                        //if no more paths have this distance, then remove the key from dictionary
                        if (shortestPathIndies[minDist].Count == 0)
                        {
                            shortestPathIndies.Remove(minDist);
                        }

                        //find the shortest path from source to target of this request
                        //shortestPath = dijkstra.Solve(network, (minT << 10) ^ tSource, (minT << 10) ^ tDest, edgeLengths);
                        shortestPath = pathsAtTimeT[minT];

                        //find the bottleneck edge in the shortest path
                        minCap = double.MaxValue;
                        foreach (Edge<int> e in shortestPath.edgesList)
                        {
                            if (edgeCapacities[new Tuple<int, int>(e.Source, e.Target)] <= minCap)
                            {
                                minCap = edgeCapacities[new Tuple<int, int>(e.Source, e.Target)];
                                minCapEdge = e;
                            }
                        }

                        //find the minimum of left over demand and the bottleneck capacity
                        tempu = Math.Min(tempDemand, minCap);
                        tempDemand -= tempu;


                        //Send the required amount of flow
                        //capacity constraints are not taken care of now
                        //later we take care by scaling all the flows
                        foreach (Edge<int> e in shortestPath.edgesList)
                        {
                            if (edgeFlows.ContainsKey(new Tuple<int, int>(e.Source, e.Target)))
                            {
                                edgeFlows[new Tuple<int, int>(e.Source, e.Target)] += tempu;
                            }
                            else
                            {
                                edgeFlows[new Tuple<int, int>(e.Source, e.Target)] = tempu;
                            }
                        }


                        //increment the transferred bits of user j
                        flow[j] += tempu;

                        //update lengths of edges in the shortest path
                        //update D
                        changeInD = 0;
                        foreach (Edge<int> e in shortestPath.edgesList)
                        {
                            Tuple<int, int> t = new Tuple<int, int>(e.Source, e.Target);
                            double oldLength = edgeLengths[t];
                            edgeLengths[t] *= (1 + (epsilon * tempu) / (edgeCapacities[t]));
                            changeInD += edgeCapacities[t] * (edgeLengths[t] - oldLength);

                        }

                        D += changeInD;
                        //Console.WriteLine("The value of D is {0}", D);

                        //Now recompute the shortest path in the graph correspoinding to minT
                        shortestPath = dijkstra.Solve(network, (minT << 10) ^ tSource, (minT << 10) ^ tDest, edgeLengths);
                        if (!shortestPathIndies.ContainsKey(shortestPath.length))
                        {
                            shortestPathIndies[shortestPath.length] = new List<int>();
                        }
                        shortestPathIndies[shortestPath.length].Add(minT);


                    }
                }
            }
            timer.Stop();
            Console.WriteLine("Time elapsed is {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("Alpha: {0}", (phases - 1) / (Math.Log(1 / delta) / Math.Log(1 + epsilon)));

            //Calculate alpha manually and check
            double alpha = double.MaxValue;
            double fractionSent;
            double scaleFactor = Math.Log(1 / delta) / Math.Log(1 + epsilon);
            for (int f = 0; f < reqCount; f++)
            {
                flow[f] = flow[f] / scaleFactor;
                fractionSent = flow[f] / requests[f].demand;

                if (fractionSent <= alpha)
                {
                    alpha = fractionSent;
                }
            }

            Console.WriteLine("The value of alpha calculated is {0}", alpha);

            //Just a check to ensure whether capacity constraints are not violated after scaling the flows
            foreach (Edge<int> e in network.Edges)
            {
                if (!edgeFlows.ContainsKey(new Tuple<int, int>(e.Source, e.Target))) continue;
                if (edgeFlows[new Tuple<int, int>(e.Source, e.Target)] / scaleFactor > edgeCapacities[new Tuple<int, int>(e.Source, e.Target)])
                {
                    Console.WriteLine("Danger!");
                    Console.ReadKey();
                }
            }
            Console.WriteLine("No capacity constraints have been violated");
        }
        
    }
}
