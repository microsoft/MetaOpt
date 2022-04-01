using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;


using System.Diagnostics;
namespace MaxConcurrentFlow
{
    public class S14_YoungsAlgorithm
    {
        public static Int32 NumBitsForSource = 10, NumBitsForPaths = 6;

        // input variables
        public Dictionary<int, double> edgeCapacities;
        public Request[] requests;
        public BidirectionalGraph<int, Edge<int>> orig_network;
        public Dictionary<int, List<Path>> pathDictionary;

        public int T;
        public double epsilon;


        // persistent state across runs; mostly initial values for CheckFeasiblity
        // private BidirectionalGraph<int, Edge<int>> reduced_network;
        // private Dictionary<int, BitArray> edgeUsedByReq;
        public Dictionary<int, HashSet<int>> edgeUsedByReqPaths;

        // constructor
        public S14_YoungsAlgorithm(
            BidirectionalGraph<int, Edge<int>> network,
            Dictionary<int, double> edgeCaps,
            List<Request> reqs,
            int _T,
            Dictionary<int, List<Path>> _pathDictionary,
            double _epsilon
            )
        {
            // all inputs are on the original network
            edgeCapacities = edgeCaps;
            orig_network = network;
            requests = reqs.ToArray<Request>();
            T = _T;
            epsilon = _epsilon;
            pathDictionary = _pathDictionary;



            // compute reduced network, not used at all
            // reduced_network = (new NetworkGenerator()).getReducedNetwork(orig_network, T);


            // per edge, remember which requests _can_ use it, i.e., have a path through the ege
            /*
            edgeUsedByReq = new Dictionary<int, BitArray>();

            foreach (Edge<int> e in orig_network.Edges)
            {
                int key = (e.Source << YoungsAlgorithm.NumBitsForSource) | (e.Target);
                edgeUsedByReq[key] = new BitArray(requests.Length);
                //make all the bits false initially
                for (int b = 0; b < requests.Length; b++)
                {
                    edgeUsedByReq[key][b] = false;
                }
            }
            

            for (int r = 0; r < requests.Length; r++)
            {
                Request r_r = requests[r];

                foreach (Path p in pathDictionary[r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest])
                    foreach (Edge<int> e in p.edgesList)
                    {
                        edgeUsedByReq[e.Source << YoungsAlgorithm.NumBitsForSource | (e.Target)][r] = true;
                    }
            }
            */

            // per edge, remember the {req, pathindex} that uses it
            // reading the hashset is okay from multiple threads
            edgeUsedByReqPaths = new Dictionary<int, HashSet<int>>();
            foreach (Edge<int> e in orig_network.Edges)
            {
                int key = (e.Source << YoungsAlgorithm.NumBitsForSource) | (e.Target);
                edgeUsedByReqPaths[key] = new HashSet<int>();
            }

            for (int r = 0; r < requests.Length; r++)
            {
                Request r_r = requests[r];

                if (r == 9)
                {
                    int i = 0; i++;
                }

                int p_ind = 0;
                foreach(Path p in pathDictionary[r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest])
                {
                    foreach (Edge<int> e in p.edgesList)
                        edgeUsedByReqPaths[e.Source << YoungsAlgorithm.NumBitsForSource | (e.Target)].Add(r << YoungsAlgorithm.NumBitsForPaths | p_ind);
                    p_ind ++;
                }
            }
            // ...
        }

        public double CheckFeasibility(double alpha, double beta)
        {
            Console.WriteLine("Started running Young's algorithm");

            var timer = System.Diagnostics.Stopwatch.StartNew();
            // output of this computation
            /*
            ConcurrentDictionary<int, double> edgeFlows =
                new ConcurrentDictionary<int, double>();
             */
            Dictionary<int, double> edgeFlows = new Dictionary<int, double>();

            foreach (Edge<int> e in orig_network.Edges)
            {
                int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                for (int t = 0; t <= T; t++)
                    edgeFlows[t << 2 * YoungsAlgorithm.NumBitsForSource | e_k] = 0;
            }

            // edgeweigths set as 1/c_e
            // ConcurrentDictionary<int, double> edgeLengths = new ConcurrentDictionary<int, double>();
            Dictionary<int, double> edgeLengths = new Dictionary<int, double>();
            foreach (Edge<int> e in orig_network.Edges)
            {
                int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                double l = 1 / edgeCapacities[e_k];
                for (int t = 0; t <= T; t++)
                    edgeLengths[t << 2 * YoungsAlgorithm.NumBitsForSource | e_k] = l;
            }
            

            int m = orig_network.EdgeCount;

            //E is the number of edges in the reduced graph
            int E = m * (T + 1);

            double[] totalFlow = new double[requests.Length];
            double[] z = new double[requests.Length];

            //set of requests whose demand is not fulfilled yet
            // has to be explicitly syncrhonized 1
            int[] yetToSatisfyReqs_a;
            List<int> yetToSatisfyReqs = new List<int>();  


            //initialization and updating E
            for (int i = 0; i < requests.Length; i++)
            {
                totalFlow[i] = 0;
                yetToSatisfyReqs.Add(i);
                z[i] = 1000000000000;
                E += (requests[i].deadline - requests[i].arrival + 1) * 2;
            }
            yetToSatisfyReqs_a = yetToSatisfyReqs.ToArray<int>();

            // has to be explicitly syncrhonized 2, 3
            double sumY = E;
            double sumZ = z[0]*requests.Length;



            // we may have to partition this 4
            // memorize the shortest paths for each request in each time graph
            Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>
                req2shortestPathLength2time_and_index = 
                new Dictionary<int,SortedDictionary<double,List<Tuple<int, int>>>>();
            Dictionary<int, Dictionary<int, Tuple<double, int>>> 
                req2time2shortestPathLength_and_index = 
                new Dictionary<int, Dictionary<int, Tuple<double, int>>>();

 
            for (int r = 0; r < requests.Length; r++)
            {
                req2shortestPathLength2time_and_index[r] = new SortedDictionary<double, List<Tuple<int, int>>>();
                req2time2shortestPathLength_and_index[r] = new Dictionary<int, Tuple<double, int>>();

                Request r_r = requests[r];
                int r_k = r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest;

                double minLength = double.MaxValue;
                int minPathInd=-1, pathInd = 0;
                foreach (Path p in pathDictionary[r_k])
                {
                    double pathLength = 
                        p.edgesList.Sum( e => 
                            edgeLengths[ (e.Source << YoungsAlgorithm.NumBitsForSource) | (e.Target)]
                            );

                    if (pathLength <= minLength)
                    {
                        minLength = pathLength;
                        minPathInd = pathInd;
                    }
                    pathInd++;
                }
                for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                {
                    if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                        req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());

                    req2shortestPathLength2time_and_index[r][minLength].Add( new Tuple<int, int>(t, minPathInd) );

                    req2time2shortestPathLength_and_index[r][t] = new Tuple<double, int>(minLength, minPathInd);
                }
            }


            int iterations = 0;
            int next_index_yetToSatisfy = 0;

            double average_requestsSearchedPerIteration = 0;

            while (yetToSatisfyReqs.Count != 0)
            {
                iterations++;

                int istar = 0, jstar = 0;
                double oldy, oldz;
                Path shortestPath = null;
                bool flag = false;
                int minT = 0;

                // find a feasible {req, time} pair while iterating in round robin order
                //foreach (int i in yetToSatisfyReqs) //yetToSatisfyReqs.OrderBy(i=> totalFlow[i]/requests[i].demand))// allReqs.OrderBy(i=> yetToSatisfyReqs.Contains(i)? i: allReqs.Count+i))
                for (int j = 0; j < yetToSatisfyReqs.Count; j++)
                {
                    int i = yetToSatisfyReqs_a[(next_index_yetToSatisfy + j) % yetToSatisfyReqs.Count];
                    Request r_i = requests[i];

                    KeyValuePair<double, List<Tuple<int, int>>> kvp_d_li = req2shortestPathLength2time_and_index[i].First();
                    double pathLength = kvp_d_li.Key;
                    Tuple<int, int> f = kvp_d_li.Value[0];
                    minT = f.Item1;
                    int minPathInd = f.Item2;

                    // this is check in 4a
                    if ((alpha * r_i.demand * pathLength * sumZ) <= (sumY * beta * z[i]))
                    {
                        average_requestsSearchedPerIteration += (j - average_requestsSearchedPerIteration)/ iterations;

                        shortestPath = pathDictionary[r_i.src << YoungsAlgorithm.NumBitsForSource | r_i.dest][minPathInd];

                        istar = i;
                        jstar = j;
                        flag = true;
                        break;
                    }
                }

                if (flag == false)
                {
                    Console.WriteLine("Problem infeasible");
                    break;
                }

                double gamma;
                // Gamma calculation as per 5
                {
                    double minCapInShortestPath =
                        shortestPath.edgesList.Min(e =>
                            edgeCapacities[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target]);

                    Debug.Assert(minCapInShortestPath != double.MaxValue);
                    gamma = epsilon * Math.Min(alpha * requests[istar].demand, beta * minCapInShortestPath);
                }

                // step 4b: allocate some flow to request istar
                double newFlow = gamma * epsilon / Math.Log(m);  // SK: fix?
                totalFlow[istar] += newFlow;


                // update y(e)
                foreach (Edge<int> e in shortestPath.edgesList)
                {
                    int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                    int key = minT << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

                    oldy = edgeLengths[key];
                    edgeLengths[key] = edgeLengths[key] * Math.Pow(Math.E, gamma / (beta * edgeCapacities[e_k]));
                    if (edgeLengths[key] > .1 * double.MaxValue ||
                         sumY > .1 * double.MaxValue)
                    {
                        Console.WriteLine("WARN! edgeLength or sumY overflows {0} {1} {2}", minT, edgeLengths[key], sumY);
                    }
                    sumY = sumY + (edgeLengths[key] - oldy) * edgeCapacities[e_k];
                    edgeFlows[key] += newFlow;
                }

                SortedSet<int> shortestPathChangedReqs = new SortedSet<int>();
                //find the requests whose shortest path is likely to change because of length changes done above
                foreach (Edge<int> e in shortestPath.edgesList)
                {
                    int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                    foreach (int r in yetToSatisfyReqs)
                    {
                        if (requests[r].arrival > minT || requests[r].deadline < minT) continue;

                        int minPathInd = req2time2shortestPathLength_and_index[r][minT].Item2;
                        if (edgeUsedByReqPaths[e_k].Contains(r << YoungsAlgorithm.NumBitsForPaths | minPathInd))
                            shortestPathChangedReqs.Add(r);
                    }
                }
                // Console.WriteLine("#reqs to re-eval path old {0} new {1}", old_shortestPathChangedReqs.Count, shortestPathChangedReqs.Count);

                // z(i)
                // to prevent underflow, upscale all z if z[istar] is low
                if (z[istar] < .1)
                {
                    int _x = 1000000;
                    sumZ *= _x;

                    double _curr_sumZ = 0;
                    foreach (int i in yetToSatisfyReqs)
                    {
                        z[i] *= _x;
                        _curr_sumZ += z[i];
                    }

                    Debug.Assert(Math.Abs(_curr_sumZ - sumZ) < .001 * sumZ, "sumZ before scaling is diff");
                    sumZ = _curr_sumZ;
                }


                // update z[istar]
                oldz = z[istar];
                z[istar] = z[istar] * Math.Pow(Math.E, (-1) * gamma / (alpha * requests[istar].demand));

                Debug.Assert(z[istar] > 0,
                    "WARN! z[istar] falling below zero?");

                double old_sumZ = sumZ;
                sumZ = sumZ + z[istar] - oldz;
                Debug.Assert(sumZ > 0, "WARN! sumZ falling below zero?");

                bool request_istar_done = false;

                //remove users whose demands are already fulfilled
                if (totalFlow[istar] >= alpha * requests[istar].demand)
                {
                    yetToSatisfyReqs.Remove(istar);
                    yetToSatisfyReqs_a = yetToSatisfyReqs.ToArray<int>();
                    sumZ -= z[istar];
                    Debug.Assert(sumZ > 0 || yetToSatisfyReqs.Count == 0,
                        "WARN! sumZ falls below zero upon satisifying req");

                    next_index_yetToSatisfy = jstar;
                    request_istar_done = true;
                }
                else
                {
                    next_index_yetToSatisfy = (jstar + 1) % yetToSatisfyReqs.Count;
                }
                if (yetToSatisfyReqs.Count > 0)
                    next_index_yetToSatisfy %= yetToSatisfyReqs.Count;


                //Recompute shortest paths for the copy corresponding to minT, for the requests that may need change
                foreach (int r in shortestPathChangedReqs)
                {
                    if (r == istar)
                        if(request_istar_done)
                    {
                        req2shortestPathLength2time_and_index.Remove(r);
                        req2time2shortestPathLength_and_index.Remove(r);
                        continue;
                    }

                    Request r_r = requests[r];

                    double minLength = double.MaxValue;
                    int minPathInd=-1, pathInd = 0;
                    foreach(Path p in pathDictionary[r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest])
                    {
                        double p_length = 0;
                        foreach(Edge<int> e in p.edgesList)
                            p_length += edgeLengths[minT << 2 * YoungsAlgorithm.NumBitsForSource | e.Source << YoungsAlgorithm.NumBitsForSource | e.Target];

                        if (p_length < minLength)
                        {
                            minLength = p_length;
                            minPathInd = pathInd;
                        }
                        pathInd++;
                    }

                    // update the memorized path length
                    double _x = req2time2shortestPathLength_and_index[r][minT].Item1;

                    req2shortestPathLength2time_and_index[r][_x].RemoveAll(t => t.Item1 == minT);
                    if (req2shortestPathLength2time_and_index[r][_x].Count == 0)
                        req2shortestPathLength2time_and_index[r].Remove(_x);

                    if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                        req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());
                    req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(minT, minPathInd));

                    req2time2shortestPathLength_and_index[r][minT] = new Tuple<double,int>(minLength, minPathInd);
                }
            }

            double retval = -1;
            if (yetToSatisfyReqs.Count == 0)
            {
                double worst_beta = 0;
                Console.WriteLine("Feasible solution for beta = {0}", beta);
                foreach (Edge<int> e in orig_network.Edges)
                {
                    int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                    for (int t = 0; t <= T; t++)
                    {
                        int key = t << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

                        worst_beta = Math.Max(worst_beta, edgeFlows[key] / edgeCapacities[e_k]);
                    }
                }
                Console.WriteLine("Obtained beta:{0} when desired beta:{1}", worst_beta, beta);    
                retval = worst_beta;
            }
            else
            {
                Console.WriteLine("No feasible solution for beta = {0}", beta);
            }
        

            timer.Stop();
            Console.WriteLine("Elapsed time: {0} {1} iter {2} reqs/iter", timer.ElapsedMilliseconds, iterations, average_requestsSearchedPerIteration);
            return retval;
        }

    }
}
