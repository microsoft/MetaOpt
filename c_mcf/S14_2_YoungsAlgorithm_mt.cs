using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using QuickGraph;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MaxConcurrentFlow
{
    public class S14_2_Y_ThreadState
    {
        //
        // thread local variables; these are small enough; assuming we can restart threads
        //
        public Dictionary<int, double> edgeCapacities;
        public List<Tuple<int, int>> orig_network_edges;
        public int nodecount;
        public Dictionary<int, HashSet<int>> edgeUsedByReqPaths;

        // this is potentially quite large; but using it to avoid concurrent dictionary
        public Dictionary<int, int[][]> pathDictionary; // key: src<<x | target, value: src, hop1, ..., target
    }

    public class S14_2_WorkerThread
    {
        static int ReadTimeoutMS = 1000, WriteTimeoutMS = 3000;
        public static int HowManyIters = 10000;
        Stopwatch sw;

        S14_2_Y_ThreadState yts;
        S14_2_SharedState ss;
        public int threadId;
        public int success_iterations;

        int begin_t, T;  // this thread is responsible for times [begin_t, T]

        // which requests
        int[] req_ids;

        public Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>
            req2shortestPathLength2time_and_index;

        public bool madeForwardProgress;

        public void SetWork(int b, int e, int[] _req_ids)
        {
            begin_t = b;
            T = e;

            req_ids = _req_ids;
        }

        public S14_2_WorkerThread
            (S14_YoungsAlgorithm y, int _tid)
        {
            threadId = _tid;

            yts = new S14_2_Y_ThreadState();

            // copy individually
            yts.edgeCapacities = new Dictionary<int, double>();
            foreach (int e_key in y.edgeCapacities.Keys)
                yts.edgeCapacities.Add(e_key, y.edgeCapacities[e_key]);

            yts.orig_network_edges = new List<Tuple<int, int>>();
            foreach (Edge<int> e in y.orig_network.Edges)
                yts.orig_network_edges.Add(new Tuple<int, int>(e.Source, e.Target));

            yts.nodecount = y.orig_network.VertexCount;

            yts.pathDictionary = new Dictionary<int, int[][]>();
            foreach (int p_key in y.pathDictionary.Keys)
            {
                List<Path> l_paths = y.pathDictionary[p_key];
                int[][] paths = new int[l_paths.Count][];

                yts.pathDictionary.Add(p_key, paths);
                for (int p_ind = 0; p_ind < l_paths.Count; p_ind++)
                {
                    Path p = l_paths[p_ind];
                    List<int> p_as_list = new List<int>();

                    foreach (Edge<int> e in p.edgesList)
                        p_as_list.Add(e.Source);

                    p_as_list.Add(p_key & ((1 << YoungsAlgorithm.NumBitsForSource) - 1));

                    paths[p_ind] = p_as_list.ToArray();
                }
            }

            yts.edgeUsedByReqPaths = new Dictionary<int, HashSet<int>>();
            foreach (int e_key in y.edgeUsedByReqPaths.Keys)
            {
                HashSet<int> h = new HashSet<int>();
                foreach (int pind in y.edgeUsedByReqPaths[e_key])
                    h.Add(pind);
                yts.edgeUsedByReqPaths.Add(e_key, h);
            }

            sw = new Stopwatch();
        }

        public void CopyMinPaths(S14_2_SharedState ss, bool singleThread = false)
        {
            sw = new Stopwatch();
            sw.Start();
            this.ss = ss;

            if (singleThread)
                req2shortestPathLength2time_and_index =
                    ss.req2shortestPathLength2time_and_index;
            else
            {
                req2shortestPathLength2time_and_index =
                    new Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>();

                foreach (int r in req_ids)
                {
                    SortedDictionary<double, List<Tuple<int, int>>>
                        sd_d_ltii = new SortedDictionary<double, List<Tuple<int, int>>>();

                    Request r_r = ss.requests[r];

                    // pass 1: copy in
                    foreach (KeyValuePair<double, List<Tuple<int, int>>> kvp1 in
                            ss.req2shortestPathLength2time_and_index[r])
                    {

                        foreach (Tuple<int, int> tii in kvp1.Value)
                        {
                            if (tii.Item1 < Math.Max(r_r.arrival, begin_t) ||
                                tii.Item1 > Math.Min(r_r.deadline, T))
                                continue;

                            if (!sd_d_ltii.ContainsKey(kvp1.Key))
                                sd_d_ltii.Add(kvp1.Key, new List<Tuple<int, int>>());

                            sd_d_ltii[kvp1.Key].Add(new Tuple<int, int>(tii.Item1, tii.Item2));
                        }
                    }
                    req2shortestPathLength2time_and_index[r] = sd_d_ltii;

                    // pass 2: clean parent
                    List<Double> d_r = new List<double>();
                    foreach (double d in ss.req2shortestPathLength2time_and_index[r].Keys)
                    {
                        ss.req2shortestPathLength2time_and_index[r][d].RemoveAll
                            (tii =>
                                tii.Item1 >= Math.Max(r_r.arrival, begin_t) &&
                                tii.Item1 <= Math.Min(r_r.deadline, T));

                        if (ss.req2shortestPathLength2time_and_index[r][d].Count == 0)
                            d_r.Add(d);
                    }
                    foreach (double d in d_r)
                        ss.req2shortestPathLength2time_and_index[r].Remove(d);

                }
            }
            sw.Stop();
            Console.WriteLine("Thread {0} copyminpaths {1}ms", threadId, sw.ElapsedMilliseconds);
        }

        public void RunOnPartition()
        {
            Console.WriteLine("Thread {0} start #reqs {1} time {2}-{3}",
                threadId,
                // String.Join(",", req_ids), 
                req_ids.Length,
                begin_t,
                T);

            madeForwardProgress = false;
            sw = new Stopwatch();
            sw.Start();

            int iterations = 0;
            success_iterations = 0;
            int next_index_yetToSatisfy = 0;
            double average_requestsSearchedPerIteration = 0;

            List<int> yetToSatisfyReqs = new List<int>();
            yetToSatisfyReqs.AddRange(req_ids);
            int[] yetToSatisfyReqs_a = req_ids;

            while (iterations < HowManyIters)
            {
                /*
                if ( iterations % 100 == 99)
                    Console.WriteLine("T {0} iter {1}", threadId, iterations);
                 */
                iterations++;

                int istar = 0, jstar = 0;
                double old_sumy = 0, old_sumz = 0, new_sumY = 0, old_r=0, new_r;
                bool old_total_flow_active = true;
                int[] shortestPath = null;
                bool flag = false;
                int minT = 0;
                Request r_i = null;
                double pathLength = 0;

                // find a feasible {req, time} pair while iterating in round robin order
                //foreach (int i in yetToSatisfyReqs) //yetToSatisfyReqs.OrderBy(i=> totalFlow[i]/requests[i].demand))// allReqs.OrderBy(i=> yetToSatisfyReqs.Contains(i)? i: allReqs.Count+i))
                for (int j = 0; j < yetToSatisfyReqs.Count; j++)
                {
                    int i = yetToSatisfyReqs_a[(next_index_yetToSatisfy + j) % yetToSatisfyReqs.Count];

                    r_i = ss.requests[i];

                    KeyValuePair<double, List<Tuple<int, int>>> kvp_d_li =
                        req2shortestPathLength2time_and_index[i].First();
                    pathLength = kvp_d_li.Key;
                    Tuple<int, int> f = kvp_d_li.Value[0];
                    minT = f.Item1;
                    int minPathInd = f.Item2;

                    try
                    {
                        ss.rwl.AcquireReaderLock(S14_2_WorkerThread.ReadTimeoutMS);

                        old_sumy = ss.sumY;
                        old_sumz = ss.sumZ;
                        old_r = ss.covering_r;
                        old_total_flow_active = ss.total_flow_active;

                        ss.rwl.ReleaseReaderLock();
                    }
                    catch (ApplicationException)
                    {
                        Console.WriteLine("Thread {0} findFeasible can't get reader lock", threadId);
                        continue;
                    }

//                    if ((ss.alpha * r_i.demand * pathLength * old_sumz) <= (old_sumy * ss.beta * ss.z[i]))
                    // this is check in 4a
                    if ((pathLength * (old_sumz + (old_total_flow_active? old_r: 0))) <= 
                        (old_sumy * ss.beta * ((ss.z[i] / (ss.alpha * r_i.demand)) + (old_total_flow_active? (old_r/ (ss.delta * ss.totalDemand)):0) )))
                    {
                        average_requestsSearchedPerIteration += (j - average_requestsSearchedPerIteration) / iterations;

                        shortestPath = yts.pathDictionary[r_i.src << YoungsAlgorithm.NumBitsForSource | r_i.dest][minPathInd];

                        istar = i;
                        jstar = j;
                        flag = true;
                        break;
                    }
                }

                if (flag == false)
                {
                    Console.WriteLine("Thread {0} no forward progress", threadId);
                    break;
                }

                double gamma;
                // Gamma calculation as per 5
                {
                    double minCapInShortestPath = double.MaxValue;
                    for (int i = 1; i < shortestPath.Length; i++)
                    {
                        int s = shortestPath[i - 1],
                            t = shortestPath[i];

                        minCapInShortestPath = Math.Min(
                            minCapInShortestPath,
                            yts.edgeCapacities[s << YoungsAlgorithm.NumBitsForSource | t]
                            );
                    }

                    Debug.Assert(minCapInShortestPath != double.MaxValue);
                    gamma = ss.epsilon * Math.Min(ss.alpha * ss.requests[istar].demand, ss.beta * minCapInShortestPath);
                }

                // step 4b: allocate some flow to request istar
                double newFlow = gamma * ss.epsilon / Math.Log(ss.m);

                // prepare updates to y(e), flow, sumY, r
                Dictionary<int, double> newY =
                    new Dictionary<int, double>();

                new_sumY = old_sumy;
                for (int e_i = 1; e_i < shortestPath.Length; e_i++)
                {
                    int s = shortestPath[e_i - 1], t = shortestPath[e_i];
                    int e_k = s << YoungsAlgorithm.NumBitsForSource | t;
                    int key = minT << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

                    double old_y = ss.edgeLengths[key];
                    double new_y = ss.edgeLengths[key] *
                        Math.Pow(Math.E, gamma / (ss.beta * yts.edgeCapacities[e_k]));
                    if (new_y > .1 * double.MaxValue ||
                        new_sumY > .1 * double.MaxValue)
                    {
                        Console.WriteLine("WARN! edgeLength or sumY overflows {0} {1} {2}",
                            minT, ss.edgeLengths[key], new_sumY);
                    }
                    new_sumY = new_sumY + (new_y - old_y) * yts.edgeCapacities[e_k];

                    newY[key] = new_y;
                }

                // prepare updates to z, sumZ


                // check if request should be done
                bool request_istar_done =
                    ss.totalFlow[istar] + newFlow >= ss.alpha * ss.requests[istar].demand;

                newFlow = Math.Min(newFlow, ss.alpha * ss.requests[istar].demand - ss.totalFlow[istar]);

                // check that updates can be done
                bool go_on = false;
                try
                {
                    ss.rwl.AcquireWriterLock(S14_2_WorkerThread.WriteTimeoutMS);

                    // update the shared ones first
//                  if ((ss.alpha * r_i.demand * pathLength * ss.sumZ) <= (ss.sumY * ss.beta * ss.z[istar]))
                    if ((pathLength * (ss.sumZ + (ss.total_flow_active ? ss.covering_r : 0))) <=
    (ss.sumY * ss.beta * ((ss.z[istar] / (ss.alpha * r_i.demand)) + (ss.total_flow_active ? (ss.covering_r / (ss.delta * ss.totalDemand)) : 0))))
                    {
                        go_on = true;
                        ss.sumY = (ss.sumY - old_sumy) + new_sumY;

                        ss.totalDemand_satisfied += newFlow;
                        if (ss.total_flow_active && 
                            ss.totalDemand_satisfied >= ss.delta * ss.totalDemand)
                        {
                            ss.total_flow_active = false;
                            Console.WriteLine("| ---> delta constraint met");
                        }

                        if (ss.total_flow_active)
                        {
                            ss.covering_r *= Math.Pow(Math.E, -1 * gamma / (ss.delta * ss.totalDemand));
                            Debug.Assert(ss.covering_r > .001, String.Format("covering_r underflow {0}", ss.covering_r));
                        }


                        double old_z = ss.z[istar];
                        ss.z[istar] = old_z * Math.Pow(Math.E, (-1) * gamma / (ss.alpha * ss.requests[istar].demand));

                        if (ss.z[istar] < 10000)
                        {
                            int _x = 1000000;

                            double _curr_sumZ = 0;
                            for (int i = 0; i < ss.yetToSatisfyReqs.Length; i++)
                            {
                                if (!ss.yetToSatisfyReqs[i]) continue;

                                ss.z[i] *= _x;
                                _curr_sumZ += ss.z[i];
                            }
                            ss.sumZ = _curr_sumZ;

                            if (ss.total_flow_active)
                                ss.covering_r *= _x;
                        }
                        else
                            ss.sumZ = (ss.sumZ + ss.z[istar]) - old_z;

                        if (request_istar_done)
                        {
                            ss.yetToSatisfyReqs[istar] = false;
                            ss.sumZ -= ss.z[istar];
                        }
                    }

                    // release lock
                    ss.rwl.ReleaseWriterLock();
                }
                catch (ApplicationException)
                {
                    Console.WriteLine("Thread {0} findFeasible can't get writer lock", threadId);
                }


                if (go_on)
                {
                    success_iterations++;

                    madeForwardProgress = true;

                    // then, make "local" updates
                    ss.totalFlow[istar] += newFlow;
                    foreach (int key in newY.Keys)
                    {
                        ss.edgeFlows[key] += newFlow;
                        ss.edgeLengths[key] = newY[key];
                    }

                    //remove users whose demands are already fulfilled
                    if (request_istar_done)
                    {
                        yetToSatisfyReqs.Remove(istar);
                        yetToSatisfyReqs_a = yetToSatisfyReqs.ToArray<int>();

                        next_index_yetToSatisfy = jstar;
                    }
                    else
                    {
                        next_index_yetToSatisfy = (jstar + 1) % yetToSatisfyReqs.Count;
                    }
                    if (yetToSatisfyReqs.Count > 0)
                        next_index_yetToSatisfy %= yetToSatisfyReqs.Count;


                    // pick new shortest paths due to weight changes

                    SortedSet<int> shortestPathChangedReqs = new SortedSet<int>();

                    //find the requests whose shortest path is likely to change because of length changes done above
                    for (int e_i = 1; e_i < shortestPath.Length; e_i++)
                    {
                        int s = shortestPath[e_i - 1], t = shortestPath[e_i];

                        int e_k = s << YoungsAlgorithm.NumBitsForSource | t;
                        foreach (int r in yetToSatisfyReqs)
                        {
                            if (ss.requests[r].arrival > minT || ss.requests[r].deadline < minT) continue;


                            int minPathInd = ss.req2time2shortestPathLength_and_index[r][minT].Item2;
                            if (yts.edgeUsedByReqPaths[e_k].Contains(r << YoungsAlgorithm.NumBitsForPaths | minPathInd))
                                shortestPathChangedReqs.Add(r);
                        }
                    }
                    // Console.WriteLine("#reqs to re-eval path old {0} new {1}", old_shortestPathChangedReqs.Count, shortestPathChangedReqs.Count);


                    //Recompute shortest paths for the copy corresponding to minT, for the requests that may need change
                    foreach (int r in shortestPathChangedReqs)
                    {
                        if (r == istar && request_istar_done)
                        {
                            req2shortestPathLength2time_and_index.Remove(r);
                            // FIX FIX ss.req2time2shortestPathLength_and_index.Remove(r);
                            continue;
                        }
                        Request r_r = ss.requests[r];

                        double minLength = double.MaxValue;
                        int minPathInd = -1, pathInd = 0;
                        foreach (int[] p_array in yts.pathDictionary[r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest])
                        {
                            double p_length = 0;
                            for (int e_i = 1; e_i < p_array.Length; e_i++)
                            {
                                int s = p_array[e_i - 1], t = p_array[e_i];
                                p_length += ss.edgeLengths[
                                    minT << 2 * YoungsAlgorithm.NumBitsForSource |
                                    s << YoungsAlgorithm.NumBitsForSource |
                                    t];
                            }
                            if (p_length < minLength)
                            {
                                minLength = p_length;
                                minPathInd = pathInd;
                            }
                            pathInd++;
                        }

                        // update the memorized path length
                        double _x = ss.req2time2shortestPathLength_and_index[r][minT].Item1;

                        req2shortestPathLength2time_and_index[r][_x].RemoveAll(t => t.Item1 == minT);
                        if (req2shortestPathLength2time_and_index[r][_x].Count == 0)
                            req2shortestPathLength2time_and_index[r].Remove(_x);

                        if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                            req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());
                        req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(minT, minPathInd));

                        ss.req2time2shortestPathLength_and_index[r][minT] = new Tuple<double, int>(minLength, minPathInd);
                    }
                }
            }

            sw.Stop();
            Console.WriteLine("Thread {0} runOnPartition {1}ms {2}iter spec%{3}",
                threadId, sw.ElapsedMilliseconds, iterations, success_iterations * 1.0 / iterations);
        }
    }

    public class S14_2_SharedState
    {
        // read only
        public Request[] requests;
        public double epsilon, alpha, beta, delta;
        public int m, T, E;
        public double totalDemand;

        // read-write

        //
        // partitioned state
        //
        // by request
        public double[] totalFlow;

        // by time
        public ConcurrentDictionary<int, double> edgeFlows;
        public ConcurrentDictionary<int, double> edgeLengths;

        // r-w conflicts
        public double[] z; // updates are mostly per-req, except "scaling"
        public bool[] yetToSatisfyReqs; // removing satisfied requests

        public double sumY, sumZ, covering_r; // truly shared
        public bool total_flow_active; // is this covering constraint still active
        public double totalDemand_satisfied;


        // try to copy and copy back
        // memorize the shortest paths for each request in each time graph
        public Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>
            req2shortestPathLength2time_and_index;
        public ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>
                req2time2shortestPathLength_and_index;


        // locks
        public ReaderWriterLock rwl;

        public S14_2_SharedState(S14_YoungsAlgorithm y, double _alpha, double _beta, double _delta)
        {
            rwl = new ReaderWriterLock();

            beta = _beta;
            alpha = _alpha;
            delta = _delta;

            T = y.T;
            m = y.orig_network.EdgeCount;
            E = m * (T + 1);

            edgeFlows = new ConcurrentDictionary<int, double>();
            edgeLengths = new ConcurrentDictionary<int, double>();

            foreach (Edge<int> e in y.orig_network.Edges)
            {
                int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                for (int t = 0; t <= T; t++)
                {
                    int t_k = t << 2 * YoungsAlgorithm.NumBitsForSource | e_k;
                    edgeFlows[t_k] = 0;
                    edgeLengths[t_k] = 1 / y.edgeCapacities[e_k];
                }
            }


            requests = new Request[y.requests.Length];
            z = new double[requests.Length];
            totalFlow = new double[requests.Length];
            yetToSatisfyReqs = new bool[requests.Length];

            totalDemand_satisfied = 0;

            totalDemand = 0;

            for (int i = 0; i < requests.Length; i++)
            {
                requests[i] = y.requests[i];
                totalFlow[i] = 0;
                z[i] = 10000000000;
                // E += (requests[i].deadline - requests[i].arrival + 1) * 2;
                yetToSatisfyReqs[i] = true;

                totalDemand += requests[i].demand;
            }
            covering_r = 10000000000;
            total_flow_active = true;

            sumY = E;
            sumZ = z[0] * requests.Length;

            epsilon = y.epsilon;

            // seed this
            req2shortestPathLength2time_and_index =
                new Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>();
            req2time2shortestPathLength_and_index =
                new ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>();


            for (int r = 0; r < requests.Length; r++)
            {
                req2shortestPathLength2time_and_index[r] = new SortedDictionary<double, List<Tuple<int, int>>>();
                req2time2shortestPathLength_and_index[r] = new Dictionary<int, Tuple<double, int>>();

                Request r_r = requests[r];
                int r_k = r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest;

                double minLength = double.MaxValue;
                int minPathInd = -1, pathInd = 0;
                foreach (Path p in y.pathDictionary[r_k])
                {
                    double pathLength =
                        p.edgesList.Sum(e =>
                            edgeLengths[(e.Source << YoungsAlgorithm.NumBitsForSource) | (e.Target)]
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

                    req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(t, minPathInd));

                    req2time2shortestPathLength_and_index[r][t] = new Tuple<double, int>(minLength, minPathInd);
                }
            }
        }
    }
    public class S14_2_YoungsAlgorithm_mt
    {
        // all inputs are here
        S14_YoungsAlgorithm y;

        // thread local state
        S14_2_WorkerThread[] workers;
        int numThreads;

        S14_2_SharedState ss;

        public S14_2_YoungsAlgorithm_mt(S14_YoungsAlgorithm _y, int _numThreads)
        {
            y = _y;
            numThreads = _numThreads;
        }

        public double CheckFeasibility
            (double alpha, double beta, double delta, 
             out double achieved_alpha, out double achieved_delta)
        {
            // partition the work, issue the threads

            workers = new S14_2_WorkerThread[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                workers[i] = new S14_2_WorkerThread(y, i);
            }
            ss = new S14_2_SharedState(y, alpha, beta, delta);
            Thread[] threads = new Thread[numThreads];


            List<double>
                arrivals = new List<double>(),
                deadlines = new List<double>();
            for (int i = 0; i < y.requests.Length; i++)
            {
                arrivals.Add(y.requests[i].arrival);
                deadlines.Add(y.requests[i].deadline);
            }
            arrivals.Sort();
            deadlines.Sort();

            List<int> endTimes = new List<int>();
            int reqsSoFar = 0;
            int a_i = 0, d_i = 0;
            while (a_i < arrivals.Count || d_i < deadlines.Count)
            {
                double next_t;
                double
                    a = a_i < arrivals.Count ? arrivals[a_i] : double.MaxValue,
                    d = d_i < deadlines.Count ? deadlines[d_i] : double.MaxValue;

                if (a < d)
                {
                    next_t = arrivals[a_i];
                    a_i++;
                    reqsSoFar += 1;
                }
                else
                {
                    next_t = deadlines[d_i];
                    d_i++;
                    reqsSoFar += 2;
                }


                if (reqsSoFar > 3 * y.requests.Length / numThreads)
                {
                    endTimes.Add((int)Math.Ceiling(next_t));
                    reqsSoFar = 0;
                }
            }

            if (endTimes.Count != numThreads)
                endTimes.Add(y.T);

            //
            // figure out how to partition work across threads
            //
            List<Tuple<int, int>> timeRanges = new List<Tuple<int, int>>();
            int _time_partition = (int)Math.Floor(y.T * 1.0 / numThreads);
            for (int i = 0; i < numThreads; i++)
                /* equi-partition
                timeRanges.Add(
                    new Tuple<int, int>(
                    i * _time_partition,
                    i == numThreads - 1 ? y.T : ((i + 1) * _time_partition - 1)
                    ));
                 */
                // weighted partition
                timeRanges.Add(new Tuple<int, int>(
                    i==0? 0: endTimes[i-1], endTimes[i]-1
                    ));

            Dictionary<int, double[]> requestProbs = new Dictionary<int, double[]>();
            for (int j = 0; j < ss.requests.Length; j++)
            {
                Request r_j = ss.requests[j];
                double[] probabilities = new double[timeRanges.Count];
                for (int k = 0; k < timeRanges.Count; k++)
                {
                    Tuple<int, int> t_k = timeRanges[k];
                    if (t_k.Item1 > r_j.deadline ||
                         t_k.Item2 < r_j.arrival)
                        probabilities[k] = 0;
                    else
                        probabilities[k] =
                            (Math.Min(r_j.deadline, t_k.Item2) - Math.Max(r_j.arrival, t_k.Item1) + 1) * 1.0 /
                            (t_k.Item2 - t_k.Item1 + 1);
                }
                double sump = probabilities.Sum(i => i);
                double sumSoFar = 0;
                for (int k = 0; k < probabilities.Length; k++)
                {
                    sumSoFar += probabilities[k];
                    probabilities[k] = sumSoFar / sump;
                }
                probabilities[probabilities.Length - 1] = 1;

                requestProbs.Add(j, probabilities);
            }

            Stopwatch sw; 
            Random r = new Random(300);
            int scatter_iter = 0;
            int num_rem_req;
            bool forceSingleThread = false;
            do
            {
                sw = new Stopwatch();
                sw.Start();
                Console.WriteLine("Scatter {0} {1}", scatter_iter++, forceSingleThread?"!!ST!!":"");

                if (!forceSingleThread)
                {
                    // thread ids go from 0 to numThreads-1
                    // map requests to threads based on probabilities

                    int[] requestsToThreads = new int[ss.requests.Length];
                    Dictionary<int, List<int>> threadsToRequests = new Dictionary<int, List<int>>();
                    for (int i = 0; i < ss.requests.Length; i++)
                    {
                        if (ss.yetToSatisfyReqs[i] == false) continue;

                        double d = r.NextDouble();

                        int x = 0;
                        while (requestProbs[i][x] < d)
                            x++;

                        requestsToThreads[i] = x;
                        if (!threadsToRequests.ContainsKey(x))
                            threadsToRequests.Add(x, new List<int>());
                        threadsToRequests[x].Add(i);
                    }

                    // set them off to do some work
                    WorkerThread.HowManyIters = 10000;

                    for (int j = 0; j < numThreads; j++)
                    {
                        if (!threadsToRequests.ContainsKey(j) ||
                             threadsToRequests[j].Count == 0)
                            continue;


                        workers[j].SetWork(
                            timeRanges[j].Item1,
                            timeRanges[j].Item2,
                            threadsToRequests[j].ToArray<int>()
                            );
                        workers[j].CopyMinPaths(ss);
                        threads[j] = new Thread(new ThreadStart(workers[j].RunOnPartition));
                        threads[j].Start();
                    }


                    // wait for them to join; gather some of their local state
                    for (int j = 0; j < numThreads; j++)
                    {
                        if (!threadsToRequests.ContainsKey(j) ||
                            threadsToRequests[j].Count == 0)
                            continue;

                        threads[j].Join();

                        // reassemble the paths
                        foreach (int rid in workers[j].req2shortestPathLength2time_and_index.Keys)
                            foreach (double k_d in workers[j].req2shortestPathLength2time_and_index[rid].Keys)
                            {
                                if (!ss.req2shortestPathLength2time_and_index[rid].ContainsKey(k_d))
                                    ss.req2shortestPathLength2time_and_index[rid][k_d] =
                                       workers[j].req2shortestPathLength2time_and_index[rid][k_d];
                                else
                                    ss.req2shortestPathLength2time_and_index[rid][k_d].AddRange
                                        (workers[j].req2shortestPathLength2time_and_index[rid][k_d]);
                            }
                    }
                }
                else
                {
                    List<int> rem_req = new List<int>();
                    for(int i=0; i < ss.requests.Length; i++)
                        if ( ss.yetToSatisfyReqs[i] )
                            rem_req.Add( i );
                   
                    workers[0].SetWork(0, ss.T, rem_req.ToArray<int>());
                    workers[0].CopyMinPaths(ss, true);

                    threads[0] = new Thread(new ThreadStart(workers[0].RunOnPartition));
                    threads[0].Start();
                    threads[0].Join();
                }
                sw.Stop();

                int num_threads_no_progress = workers.Count(w => w.madeForwardProgress == false);
                num_rem_req = ss.yetToSatisfyReqs.Count(i => i == true);
                int num_success_iters = workers.Sum(w => w.success_iterations);

                Console.WriteLine("Scatter iter {0} = {1}ms {2}#req_remain progress {3}T {4}I %FlowMet {5:F4}",
                    scatter_iter - 1, 
                    sw.ElapsedMilliseconds, 
                    num_rem_req, 
                    numThreads - num_threads_no_progress, 
                    num_success_iters,
                    ss.totalDemand_satisfied*100.0/ ss.totalDemand);
                Console.WriteLine("------------------------");


                // what to do next?
                if (num_threads_no_progress == numThreads)
                    break;
                if (forceSingleThread &&
                    num_success_iters != WorkerThread.HowManyIters)
                    break;

                /* sk: should also be a function of T */
                if (num_rem_req < 20 ||
                    num_threads_no_progress > .5 * numThreads ||
                    num_success_iters < 1000)
                {
                    Console.WriteLine("\t <--- will force single thread");
                    forceSingleThread = true;
                }

                // reset worker state since the worker may not be invoked later...
                foreach (S14_2_WorkerThread w in workers)
                {
                    w.success_iterations = 0;
                    w.madeForwardProgress = false;
                }

            } while (num_rem_req > 0);

            double retval = -1;
            if (ss.yetToSatisfyReqs.Count(i=>i==true) == 0)
            {
                double worst_beta = 0;
                Console.WriteLine("|| Feasible solution for alpha {0} delta {1} beta {2}", alpha, delta, beta);
                foreach (Edge<int> e in y.orig_network.Edges)
                {
                    int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                    for (int t = 0; t <= y.T; t++)
                    {
                        int key = t << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

                        worst_beta = Math.Max(worst_beta, ss.edgeFlows[key] / y.edgeCapacities[e_k]);
                    }
                }
                retval = worst_beta;

                // compute achieved_alpha
                achieved_alpha = 1;
                for (int _r = 0; _r < ss.requests.Length; _r++ )
                {
                    achieved_alpha = Math.Min(achieved_alpha, ss.totalFlow[_r] / ss.requests[_r].demand);
                }

                achieved_delta = Math.Min(ss.totalDemand_satisfied / ss.totalDemand, 1);
                Console.WriteLine("|| Obtained alpha {0} delta {1} beta {2}", achieved_alpha, achieved_delta, worst_beta);
            }
            else
            {
                Console.WriteLine("|| XX No Feasible solution for alpha {0} delta {1} beta {2}", alpha, delta, beta);
                achieved_alpha = 0;
                achieved_delta = 0;
            }

            return retval;
        }
    }
}
