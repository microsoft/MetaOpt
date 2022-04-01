using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;

namespace MaxConcurrentFlow
{
    class YoungsAlgorithmNaive
    {
        private Dictionary<Tuple<int, int>, double> edgeLengths;
        private Dictionary<Tuple<int, int>, double> edgeCapacities;
        private List<Request> requests;
        private Dictionary<Tuple<int, int>, double> edgeFlows = new Dictionary<Tuple<int, int>, double>();

        public YoungsAlgorithmNaive(Dictionary<Tuple<int, int>, double> edgeLens, Dictionary<Tuple<int, int>, double> edgeCaps, List<Request> reqs)
        {
            edgeLengths = edgeLens;
            edgeCapacities = edgeCaps;
            requests = reqs;
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

            Dijkstra dijkstra = new Dijkstra();
            NetworkGenerator netGen = new NetworkGenerator(edgeLengths, edgeCapacities, requests);
         
            //initialization and updating E
            for (int i = 0; i < reqCount; i++)
            {
                totalFlow[i] = 0;
                allReqs.Add(i);
                z[i] = 1;
                E += (requests.ElementAt(i).deadline - requests.ElementAt(i).arrival + 1) * 2;
            }

            //Maintain only the sum of all y
            //No need of individual y values
            double sumY = E;
            double sumZ = reqCount;
            BidirectionalGraph<int, Edge<int>> network = netGen.getReducedNetwork(originalNetwork, T);

            //edgeLength = y(e)/c(e)
            foreach (Edge<int> e in network.Edges)
            {
                edgeLengths[new Tuple<int, int>(e.Source, e.Target)] = 1 / edgeCapacities[new Tuple<int, int>(e.Source, e.Target)];
            }

            Path shortestPath = dijkstra.Solve(originalNetwork, 0, 1, edgeLengths);        //dummy
            double gamma = 0;

            int istar = 0;              // unassigned local variable error
            double oldy;
            double oldz;
            bool flag = true;

            var timer = System.Diagnostics.Stopwatch.StartNew();

            System.IO.StreamWriter writer = new System.IO.StreamWriter("Naive");

            SortedSet<int> yetToSatisfy = new SortedSet<int>();
            for (int r = 0; r < reqCount; r++)
            {
                yetToSatisfy.Add(r);
            }
            double len;
            while (yetToSatisfy.Count != 0)
            {
                iterations++;
     
                flag = false;
                int minimumT = Int32.MaxValue;
                double minLength = double.MaxValue;
                foreach (int i in allReqs)
                {
                    
                    //This method picks global shortest paths first
                    minimumT = Int32.MaxValue;
                    minLength = double.MaxValue;

                    for (int t = requests.ElementAt(i).arrival; t <= requests.ElementAt(i).deadline; t++)
                    {

                        shortestPath = dijkstra.Solve(network, t << 10 ^ (requests.ElementAt(i).src), t << 10 ^ (requests.ElementAt(i).dest), edgeLengths);
                        len = shortestPath.length;
                        if (shortestPath.length < minLength)
                        {
                            minimumT = t;
                            minLength = shortestPath.length;
                        }
                        
                    }

                    shortestPath = dijkstra.Solve(network, minimumT << 10 ^ requests.ElementAt(i).src, minimumT << 10 ^ requests.ElementAt(i).dest, edgeLengths);
                    len = shortestPath.length;

                    if ((alpha * requests.ElementAt(i).demand * len) * sumZ <= (beta * z[i]) * sumY )
                    {
                        writer.WriteLine("Iteration: {0}", iterations);
                        writer.WriteLine("{0} {1} {2}", i, minimumT,shortestPath.length);
                        
                        istar = i;
                        flag = true;
                        break;
                    }

                    //Console.WriteLine("Request: {0} LHS:{1}",i, alpha * requests.ElementAt(i).demand * len * sumZ);
                    //Console.WriteLine("Request: {0} RHS:{1}", i,beta * z[i] * sumY);
                    if (flag == true)
                        break;
                    
                     
                    /*           
                    //This method picks the first path that satisfies the condition
                    
                    for (int t = requests.ElementAt(i).arrival; t <= requests.ElementAt(i).deadline; t++)
                    {
                        shortestPath = dijkstra.Solve(network, t << 10 ^ requests.ElementAt(i).src, t << 10 ^ requests.ElementAt(i).dest, edgeLengths);
                        if ((alpha * requests.ElementAt(i).demand * shortestPath.length) / (beta * z[i]) <= sumY / sumZ)
                        {
                            writer.WriteLine("Iteration: {0}", iterations);
                            writer.WriteLine("{0} {1} {2}", i, t, shortestPath.length);

                            istar = i;
                            flag = true;
                            break;
                    
                        }
                    }
                    if (flag == true)
                        break;
                    */
                }

                if (flag == false)
                {
                    Console.WriteLine("No feasible solution");
                    break;          //No feasible solution
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

                //totalFlow[istar] += Math.Min(requests.ElementAt(istar).demand - totalFlow[istar], gamma * epsilon / Math.Log(m));
                totalFlow[istar] += gamma * epsilon / Math.Log(m);


                //update y(e)
                foreach (Edge<int> e in shortestPath.edgesList)
                {
                    Tuple<int, int> t = new Tuple<int, int>(e.Source, e.Target);
                    oldy = edgeLengths[t];
                    edgeLengths[t] = edgeLengths[t] * Math.Pow(Math.E, gamma / (beta * edgeCapacities[t]));
                    sumY = sumY + (edgeLengths[t] - oldy) * edgeCapacities[t];
                }

                //update z(i)
                oldz = z[istar];
                z[istar] = z[istar] * Math.Pow(Math.E, (-1) * gamma / (alpha * requests.ElementAt(istar).demand));
                sumZ = sumZ + z[istar] - oldz;


                if (totalFlow[istar] >= alpha * requests.ElementAt(istar).demand)
                {
                    yetToSatisfy.Remove(istar);
                }
                /*
                //remove users whose demands are already fulfilled
                if (totalFlow[istar] >= alpha * requests.ElementAt(istar).demand)
                {
                    allReqs.Remove(istar);
                    sumZ = sumZ - z[istar];
                }
                */
                
            }
            writer.Close();
            timer.Stop();
            Console.WriteLine("Elapsed time: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("Total iterations: {0}", iterations);
            Console.WriteLine("SumY:{0} Sumz:{1}", sumY, sumZ);

            foreach (int r in allReqs)
            {
                Console.WriteLine("r:{0} sent:{1}", r, totalFlow[r]);
                Console.WriteLine("z: {0}", z[r]);
            }
            if (yetToSatisfy.Count == 0)
            {
                Console.WriteLine("Feasible solution for beta = {0}", beta);
                Console.WriteLine("End of Young's algorithm");
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
