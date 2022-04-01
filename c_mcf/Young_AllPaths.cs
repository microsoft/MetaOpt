using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using QuickGraph;

namespace MaxConcurrentFlow
{
    class Young_AllPaths
    {
        private Dictionary<Tuple<int, int>, double> edgeLengths;
        private Dictionary<Tuple<int, int>, double> edgeCapacities;
        private List<Request> requests;
        private Dictionary<Tuple<int, int>, double> edgeFlows;

        public Young_AllPaths(Dictionary<Tuple<int, int>, double> edgeLens, Dictionary<Tuple<int, int>, double> edgeCaps, List<Request> reqs)
        {
            edgeLengths = edgeLens;
            edgeCapacities = edgeCaps;
            requests = reqs;
            edgeFlows = new Dictionary<Tuple<int, int>, double>();
        }

        public bool Run(BidirectionalGraph<int, Edge<int>> originalNetwork, double alpha, double beta, int T, double epsilon)
        {
            Console.WriteLine("Started running Young's algorithm");
            int reqCount = requests.Count;
            int m = originalNetwork.EdgeCount;
            int iterations = 0;

            //E is the number of edges in the reduced graph
            int E = m * (T + 1);

            double[] totalFlow = new double[reqCount];
            double[] z = new double[reqCount];

            //set of requests whose demand is not fulfilled yet
            SortedSet<int> allReqs = new SortedSet<int>();
            SortedSet<int> yetToSatisfyReqs = new SortedSet<int>();

            Dijkstra dijkstra = new Dijkstra();
            NetworkGenerator netGen = new NetworkGenerator(edgeLengths, edgeCapacities, requests);
            double len;

            //initialization and updating E
            for (int i = 0; i < reqCount; i++)
            {
                totalFlow[i] = 0;
                allReqs.Add(i);
                yetToSatisfyReqs.Add(i);
                z[i] = 1;
                E += (requests.ElementAt(i).deadline - requests.ElementAt(i).arrival + 1) * 2;
            }

            //Maintain only the sum of all y
            //No need of individual y values
            double sumY = E;
            double sumZ = reqCount;
            BidirectionalGraph<int, Edge<int>> network = netGen.getReducedNetwork(originalNetwork, T);

            
            //For every edge, we need a bit array to store which flows have their shortest path containing this edge
            Dictionary<Tuple<int,int>, BitArray> bitDictionary = new Dictionary<Tuple<int,int>, BitArray>();
            foreach (Edge<int> e in network.Edges)
            {
                bitDictionary[new Tuple<int, int>(e.Source, e.Target)] = new BitArray(requests.Count);
                //make all the bits false initially
                for (int b = 0; b < bitDictionary[new Tuple<int, int>(e.Source, e.Target)].Count; b++)
                {
                    bitDictionary[new Tuple<int, int>(e.Source, e.Target)][b] = false;
                }
            }
            

            //edgeLength = y(e)/c(e)
            foreach (Edge<int> e in network.Edges)
            {
                edgeFlows[new Tuple<int, int>(e.Source, e.Target)] = 0;
                edgeLengths[new Tuple<int, int>(e.Source, e.Target)] = 1 / edgeCapacities[new Tuple<int, int>(e.Source, e.Target)];
            }

            //src-dest mapping with requests 
            Dictionary<Tuple<int, int>, List<int>> srcDestReqMap = new Dictionary<Tuple<int, int>, List<int>>();
            for (int r = 0; r < requests.Count; r++)
            {
                int src = requests.ElementAt(r).src;
                int dest = requests.ElementAt(r).dest;
                Tuple<int, int> t = new Tuple<int, int>(src, dest);
                if (srcDestReqMap.ContainsKey(t))
                {
                    srcDestReqMap[t].Add(r);
                }
                else
                {
                    srcDestReqMap[t] = new List<int>();
                    srcDestReqMap[t].Add(r);
                }
            }

            Path shortestPath = dijkstra.Solve(originalNetwork, 0, 1, edgeLengths);        //dummy

            double gamma = 0;

            int istar = 0;              // unassigned local variable error
            double oldy;
            double oldz;
            bool flag = true;

            Dictionary<int, SortedSet<Tuple<int, double>>> storedPaths = new Dictionary<int, SortedSet<Tuple<int, double>>>();

            //calculate shortest path for each user in each time graph
            foreach (Tuple<int, int> srcDestTuple in srcDestReqMap.Keys)
            {
                shortestPath = dijkstra.Solve(network, srcDestTuple.Item1, srcDestTuple.Item2, edgeLengths);
                foreach (int r in srcDestReqMap[new Tuple<int, int>(srcDestTuple.Item1, srcDestTuple.Item2)])
                {
                    storedPaths[r] = new SortedSet<Tuple<int, double>>(new TPComparer());
                    for (int t = requests.ElementAt(r).arrival; t <= requests.ElementAt(r).deadline; t++)
                    {
                        storedPaths[r].Add(new Tuple<int, double>(t, shortestPath.length));
                        foreach (Edge<int> e in shortestPath.edgesList)
                        {
                            int edgeSource = t << 10 ^ e.Source;
                            int edgeTarget = t << 10 ^ e.Target;
                            bitDictionary[new Tuple<int, int>(edgeSource, edgeTarget)][r] = true;
                        }
                    }
                }
            }

            SortedSet<int> shortestPathChangedReqs = new SortedSet<int>();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (yetToSatisfyReqs.Count != 0)
            {
                iterations++;
                shortestPathChangedReqs = new SortedSet<int>();
                flag = false;
                int minT = 0;       //unassigned local variable problem
                foreach (int i in allReqs)
                {
                    minT = storedPaths[i].Min.Item1;
                    
                    if ((alpha * requests.ElementAt(i).demand * storedPaths[i].Min.Item2) / (beta * z[i]) <= sumY / sumZ)
                    {
                        shortestPath = dijkstra.Solve(network, minT << 10 ^ requests.ElementAt(i).src, minT << 10 ^ requests.ElementAt(i).dest, edgeLengths);
                        istar = i;
                        flag = true;
                        break;
                    }
                    
                }

                //Gamma calculation
                if (flag == true)
                {
                    double minCapInShortestPath = double.MaxValue;
                    foreach (Edge<int> e in shortestPath.edgesList)
                    {
                        if (edgeCapacities[new Tuple<int, int>(e.Source, e.Target)] <= minCapInShortestPath)
                        {
                            minCapInShortestPath = edgeCapacities[new Tuple<int, int>(e.Source, e.Target)];
                        }
                    }
                    gamma = epsilon * Math.Min(alpha * requests[istar].demand, beta * minCapInShortestPath);
                }

                if (flag == false)
                {
                    Console.WriteLine("Problem infeasible");
                    break;
                }

                double newFlow = Math.Min(requests.ElementAt(istar).demand - totalFlow[istar], gamma * epsilon / Math.Log(m));
                totalFlow[istar] += newFlow;
                //totalFlow[istar] += gamma * epsilon / Math.Log(m);


                //update y(e)
                foreach (Edge<int> e in shortestPath.edgesList)
                {
                    Tuple<int, int> t = new Tuple<int, int>(e.Source, e.Target);
                    oldy = edgeLengths[t];
                    edgeLengths[t] = edgeLengths[t] * Math.Pow(Math.E, gamma / (beta * edgeCapacities[t]));
                    sumY = sumY + (edgeLengths[t] - oldy) * edgeCapacities[t];
                    edgeFlows[t] += newFlow;
                }

                //find the requests whose shortest path is likely to change because of length changes done above
                foreach (Edge<int> e in shortestPath.edgesList)
                {
                    Tuple<int, int> t = new Tuple<int, int>(e.Source, e.Target);
                    foreach (int r in allReqs)
                    {
                        if (bitDictionary[t][r] == true)
                        {
                            shortestPathChangedReqs.Add(r);
                        }
                    }
                }

                //now make the bits false for these requests
                foreach (Edge<int> e in originalNetwork.Edges)
                {
                    foreach (int r in shortestPathChangedReqs)
                    {
                        bitDictionary[new Tuple<int, int>(minT << 10 ^ e.Source, minT << 10 ^ e.Target)][r] = false;
                    }
                }

                //update z(i)
                oldz = z[istar];
                z[istar] = z[istar] * Math.Pow(Math.E, (-1) * gamma / (alpha * requests.ElementAt(istar).demand));
                sumZ = sumZ + z[istar] - oldz;

                //remove users whose demands are already fulfilled
                if (totalFlow[istar] >= alpha * requests.ElementAt(istar).demand)
                {
                    yetToSatisfyReqs.Remove(istar);
                }

                //Recompute shortest paths for the original copy corresponding to minT
                foreach (int r in shortestPathChangedReqs)
                {
                    shortestPath = dijkstra.Solve(network, minT << 10 ^ requests[r].src, minT << 10 ^ requests[r].dest, edgeLengths);
                    storedPaths[r].RemoveWhere(t => t.Item1 == minT);
                    storedPaths[r].Add(new Tuple<int, double>(minT, shortestPath.length));
                    foreach (Edge<int> e in shortestPath.edgesList)
                    {
                        bitDictionary[new Tuple<int, int>(e.Source, e.Target)][r] = true;
                    }
                }

            }
            timer.Stop();
            //Console.WriteLine("Elapsed time: {0}", timer.ElapsedMilliseconds);
            //Console.WriteLine("Total iterations: {0}", iterations);

            if (yetToSatisfyReqs.Count == 0)
            {
                double worst_beta = 0;
                Console.WriteLine("Feasible solution for beta = {0}", beta);
                System.IO.StreamWriter writer = new System.IO.StreamWriter("YoungScatter_alpha09_eps03_dem.xls");
                foreach (Edge<int> e in network.Edges)
                {
                    writer.WriteLine("{0}\t{1}\t{2}", e.Source, e.Target, edgeFlows[new Tuple<int, int>(e.Source, e.Target)] /
                        edgeCapacities[new Tuple<int, int>(e.Source, e.Target)]);
                    if (edgeFlows[new Tuple<int, int>(e.Source, e.Target)] /
                        edgeCapacities[new Tuple<int, int>(e.Source, e.Target)] >= worst_beta)
                        worst_beta = edgeFlows[new Tuple<int, int>(e.Source, e.Target)] /
                        edgeCapacities[new Tuple<int, int>(e.Source, e.Target)];

                }
                Console.WriteLine("Worst beta:{0} when actual beta:{1}", worst_beta,beta);    
                writer.Close();
                return true;
            }
            else
            {
                Console.WriteLine("No feasible solution for beta = {0}", beta);
                Console.WriteLine("End of Young's algorithm");
                return false;
            }
        }

    }
}
