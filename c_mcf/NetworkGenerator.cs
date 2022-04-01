using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;
using System.IO;
using System.Xml.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MaxConcurrentFlow
{
    /// <summary>
    ///  this is just a worker bee of a class; no internal state
    /// </summary>
    class NetworkGenerator
    {
        Random rand;
        public NetworkGenerator()
        {
            rand = new Random(50);
        }

        Dictionary<string, int> nodeNameToIndex;
        Randomness randomness = new Randomness(10);

        public Dictionary<int, double>[] spreadOutTrafficMatrices
            (int T, int scaleFactor, Dictionary<int, double>[] trafficMatrices)
        {
            if (T != trafficMatrices.Length)
            {
                // here we implement spreading out the actual traffic matrices to the T that we need                trafficMatrices;
            }

            if ( scaleFactor != 1)
            for (int a = 0; a < trafficMatrices.Length; a++)
            {
                Dictionary<int, double> t = new Dictionary<int, double>();
                foreach (int k in trafficMatrices[a].Keys)
                    t[k] = trafficMatrices[a][k] * scaleFactor;

                trafficMatrices[a] = t;
            }

            return trafficMatrices;
        }

        public Dictionary<int, double>[] readTrafficMatrices(string dirName)
        {
            DirectoryInfo di = new DirectoryInfo(dirName);
            FileInfo[] files = di.GetFiles();

            Regex whitespaces = new Regex(@"\s+");

            Dictionary<int, double>[] trafficMatrices =
                new Dictionary<int, double>[files.Length];

            foreach (FileInfo fi in files)
            {
                if (!fi.Name.StartsWith("tm")) continue;

                int tmId = int.Parse(fi.Name.Substring(2, fi.Name.IndexOf('.') - 2));

                trafficMatrices[tmId] = new Dictionary<int, double>();

                StreamReader sr = new StreamReader(fi.FullName);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    string[] vals = whitespaces.Split(line);
                    Debug.Assert(vals.Length == 4);

                    int from = nodeNameToIndex[vals[0]],
                        to = nodeNameToIndex[vals[1]];

                    int trafficClass;
                    switch(vals[2])
                    {
                        case "High":
                            trafficClass = 0;
                            break;
                        case "Mid":
                        case "Low":
                            trafficClass = 1;
                            break;
                        default:
                            throw new Exception("unknown traffic class " + vals[2]);
                    }

                    int key = 
                        from << (YoungsAlgorithm.NumBitsForSource+1) |
                        to << 1 |
                        trafficClass;

                    if ( ! trafficMatrices[tmId].ContainsKey(key) )
                        trafficMatrices[tmId].Add(key, 0);

                    trafficMatrices[tmId][key] += double.Parse(vals[3]);
                }
            }

            return trafficMatrices;
        }

        public BidirectionalGraph<int, Edge<int>> readNetworkFromFile(string filename, out Dictionary<int, double> edgeCapacities)
        {
            BidirectionalGraph<int, Edge<int>> network = new BidirectionalGraph<int, Edge<int>>();
            edgeCapacities = new Dictionary<int, double>();

            nodeNameToIndex = new Dictionary<string, int>();
            
            StreamReader sr = new StreamReader(filename);
            string line;
            Regex whitespaces = new Regex(@"\s+");
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;

                string[] vals = whitespaces.Split(line);
                Debug.Assert(vals.Length == 3);

                int from, to;
                foreach(string s in new string[] { vals[0], vals[1] } )
                    if (!nodeNameToIndex.ContainsKey(s))
                    {
                        nodeNameToIndex.Add(s, nodeNameToIndex.Count);
                        network.AddVertex(nodeNameToIndex.Count - 1);
                    }

                from = nodeNameToIndex[vals[0]];
                to = nodeNameToIndex[vals[1]];

                Edge<int> e = new Edge<int>(from, to);
                network.AddEdge(e);

                edgeCapacities[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target] = int.Parse(vals[2]); 
            }

            return network;
        }

        public BidirectionalGraph<int,Edge<int>>  generateOriginalNetwork(int nodeCount, int edgeCount)
        {
            Debug.Assert(nodeCount > 2);
            Debug.Assert(edgeCount > 4 * nodeCount, "directional edge count, give edgecount > 4*nodecount");

            BidirectionalGraph<int, Edge<int>> network = new BidirectionalGraph<int, Edge<int>>();
            for (int i = 0; i < nodeCount; i++)
            {
                network.AddVertex(i);
            }

            //Naive way to make sure graph is connected
            for (int i = 0; i < nodeCount - 1; i++)
            {
                network.AddEdge(new Edge<int>(i, i + 1));
                network.AddEdge(new Edge<int>(i + 1, i));
            }

            int rem_desiredEdgePairs = (edgeCount - 2 * (nodeCount - 1))/2;
            int num_candidatePairs = nodeCount * (nodeCount - 2) / 2;

            int randNum;
            for (int i = 0; i < nodeCount; i++)
            {
                for (int j = i + 2; j < nodeCount; j++)
                {
                    randNum = rand.Next(num_candidatePairs);
                    if( randNum <= rem_desiredEdgePairs)
                    {                        
                        network.AddEdge(new Edge<int>(i, j));
                        network.AddEdge(new Edge<int>(j, i));
                    }
                }
            }

            return network;
        }

        public Dictionary<int, double> scaleCapacities
            (int scaleFactor, Dictionary<int, double> ec)
        {
            Dictionary<int, double> ec2 = new Dictionary<int, double>();
            foreach (int k in ec.Keys)
                ec2[k] = ec[k] * scaleFactor;
            return ec2;
        }

        //This will take the skeleton of network and populate edge capacities
        public Dictionary<int, double> generateCapacities(BidirectionalGraph<int,Edge<int>> network, int sampleCap)
        {
            Dictionary<int, double> edgeCapacities = new Dictionary<int, double>();

            Random newr = new Random(200);
            foreach (Edge<int> e in network.Edges)
            {
                int a = newr.Next()%10;
                double cap;
                if(a<=6)cap = sampleCap;
                else cap = sampleCap/4;
                //edgeCapacities[new Tuple<int, int>(e.Source, e.Target)] = sampleCap + newr.NextDouble() * 30;
                edgeCapacities[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target] = cap; 
            }
            return edgeCapacities;
        }

        public void writeReqsToFile(string filename, List<Request> requests)
        {
            StreamWriter sw = new StreamWriter(filename);
            foreach (Request r in requests)
                sw.WriteLine("{0}", r);
            sw.Close();
        }
        public List<Request> useReqsFromFile(string filename)
        {
            List<Request> requests = new List<Request>();

            StreamReader sr = new StreamReader(filename);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                Request r = new Request(line);
                requests.Add(r);
            }
            return requests;
        }

        public List<Request> SetAwareTimes(List<Request> requests)
        {
            foreach (Request r in requests)
            {
                r.awareTime = Math.Max(0, r.arrival - randomness.pickRandomInt(20));
            }
            return requests;
        }

        public Request GetAndSetDemand(Request req, Dictionary<int, double>[] trafficMatrices, double x, bool pushFlag)
        {
            double dem = randomness.GetParetoSample(2.00001, Math.Max(x/20, 10));

            double pendingDemand = 0;
            int key =
                req.src << (YoungsAlgorithm.NumBitsForSource + 1) |
                req.dest << 1 |
                1;
            for (int t = req.arrival; t <= req.deadline; t++)
                pendingDemand += trafficMatrices[t].ContainsKey(key)? trafficMatrices[t][key]: 0;

            if (pendingDemand < dem)
            {
                if (!pushFlag || pendingDemand < 50)
                    return null;
                else
                    dem = pendingDemand;
            }

            // now store and deduct
            int numTry = 0;
            req.demand = dem;
            do
            {
                numTry++;
                if (numTry > 10)
                {
                    Console.WriteLine("Req {0} numTry {1} remains {2} avail {3}", req, numTry, dem, pendingDemand);
                }
                for (int t = req.arrival; t < req.deadline; t++)
                {
                    double y;

                    if (Math.Abs(req.demand- pendingDemand) < .001 * pendingDemand)
                        y = Math.Min(trafficMatrices[t][key], dem);
                    else
                    y = 
                        Math.Min(
                        Math.Min(
                        trafficMatrices[t][key],
                        rand.NextDouble() * 2 * dem / (numTry >1 ? 1: req.deadline - t)
                        ),
                        dem
                        );

                    trafficMatrices[t][key] -= y;
                    if (trafficMatrices[t][key] < 0)
                        trafficMatrices[t][key] = 0;

                    dem -= y;
                    if (dem < 0)
                        dem = 0;

                    if (dem == 0)
                        break;
                }
                if (trafficMatrices[req.deadline][key] >= dem ||
                    Math.Abs(dem - trafficMatrices[req.deadline][key]) < .00001)
                {
                    trafficMatrices[req.deadline][key] -= dem;
                    if (trafficMatrices[req.deadline][key] < 0)
                        trafficMatrices[req.deadline][key] = 0;
                    dem = 0;
                }
            } while (dem > 0.00001);

            return req;
        }

        //generate requests to match TM
        public List<Request> generateReqsToMatchTM(
            BidirectionalGraph<int, Edge<int>> network,
            Dictionary<int, double>[] trafficMatrices,
            int reqCount,
            int averageDuration)
        {

            int T = trafficMatrices.Length;
            List<Request> requests = new List<Request>();

            // find pairs that have non-zero lowpri demand in TMs
            Dictionary<int, double> nodePairsWithDemand = new Dictionary<int, double>();
            for (int t = 0; t < trafficMatrices.Length; t++)
                foreach (int x in trafficMatrices[t].Keys)
                    if (x % 2 == 0) continue;
                    else
                    {
                        if (!nodePairsWithDemand.ContainsKey(x))
                            nodePairsWithDemand.Add(x, 0);

                        nodePairsWithDemand[x] += trafficMatrices[t][x];
                    }


            int nodePairCount = nodePairsWithDemand.Count;

            int count_failed_req_samples = 0, limit = 100;
            for (int r = 0; r < reqCount; )
            {
                if (count_failed_req_samples > 150 * reqCount)
                    break;

                while (count_failed_req_samples > limit)
                {
                    limit += 1000;
                    Console.WriteLine("#reqs generated {0} failures {1}; index {2}", requests.Count, count_failed_req_samples, r);
                }

                int np = nodePairsWithDemand.Keys.ElementAt(rand.Next(nodePairCount));
                int sink = (np >> 1) & ((1 << YoungsAlgorithm.NumBitsForSource)-1),
                    src = (np >> (1 + YoungsAlgorithm.NumBitsForSource)) & ((1 << YoungsAlgorithm.NumBitsForSource) - 1);


                // 5 requests with the same arrival time
                int arrival = rand.Next(T), deadline;
                for (int x = 0; x < 5 && r < reqCount; x++)
                {
                    deadline = arrival + rand.Next(2 * averageDuration);
                    if (deadline >= T)
                    {
                        count_failed_req_samples++;
                        continue;
                    }

                    Request req = new Request(src, sink, arrival, deadline, 0);
                    req = GetAndSetDemand(req, trafficMatrices, nodePairsWithDemand[np], count_failed_req_samples > 100*reqCount);

                    if (req == null)
                    {
                        count_failed_req_samples++;
                        continue;
                    }

                    requests.Add(req);
                    r++;
                }

                // 5 requests with the same deadline
                deadline = rand.Next(T);
                for (int x = 0; x < 5 && r < reqCount; x++)
                {
                    arrival = deadline - rand.Next(2 * averageDuration);
                    if (arrival < 0)
                    {
                        count_failed_req_samples++;
                        continue;
                    }

                    Request req = new Request(src, sink, arrival, deadline, 0);
                    req = GetAndSetDemand(req, trafficMatrices, nodePairsWithDemand[np], count_failed_req_samples>100*reqCount);

                    if (req == null)
                    {
                        count_failed_req_samples++;
                        continue;
                    }
                    requests.Add(req);
                    r++;
                }
            }

            // choose aware time
            requests = SetAwareTimes(requests);

            return requests;
        }

        public double HowMuchDemandsRemain
            (Dictionary<int, double>[] trafficMatrices)
        {
            double d = 0;
            for (int t = 0; t < trafficMatrices.Length; t++)
                foreach (int k in trafficMatrices[t].Keys)
                    if (k % 2 == 1)
                        d += trafficMatrices[t][k];
            return d;
        }

        //generate requests
        public List<Request> generateRequests(
            BidirectionalGraph<int, Edge<int>> network, 
            int T, 
            int reqCount,
            int averageDuration)
        {
            List<Request> requests = new List<Request>();
            int nodeCount = network.VertexCount;

            //generate requests
            int src, sink, arrival, deadline = int.MaxValue;
            Request req;

            int meta_num_req_falling_out_of_T = 0;

            //deadline = rand.Next(T);
            for (int r = 0; r < reqCount; r+=10)
            {
                if (meta_num_req_falling_out_of_T > reqCount)
                    break;

                arrival = rand.Next(T);
            
                src = rand.Next(nodeCount - 1);
                do
                {
                    sink = rand.Next(nodeCount - 1);
                } while (src == sink);

                int tempr=r;
                for (tempr = r; tempr < r + 10 && tempr < reqCount; tempr++)
                {
                    // was 
                    // deadline = arrival + rand.Next(T - arrival);

                    // duration ~ U[0, 2*avg]
                    deadline = arrival + rand.Next(2*averageDuration);

                    if (deadline > T)
                    {
                        meta_num_req_falling_out_of_T++;
                        break;
                    }

                    //update the requests data structure
                    req = new Request(src, sink, arrival, deadline, 0);
                    requests.Add(req);
                }
                // we failed to find a good one
                if (deadline > T)                
                    r = tempr-10;
            }
            Console.WriteLine("# generateRequests got {0} failed {1}",
                requests.Count, meta_num_req_falling_out_of_T);
            return requests;
        }


        //generate demands for each request
        public void generateDemands(List<Request> requests)
        {
            //------------------------------------------------------------TAKE CARE
            Random newrand = new Random(20);

            double sum = 0;
            foreach (Request r in requests)
            {
                //double dem = sampleDemand + newrand.NextDouble() * 230;
                double dem = randomness.GetParetoSample(2.00001, 70);
                r.demand = dem;
                sum += dem;
            }

            Console.WriteLine("Average demand is {0}", sum / requests.Count);
        }


        //Get the reduction graph (the bigger one)
        public BidirectionalGraph<int, Edge<int>> getReducedNetwork(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            int T)
        {
            BidirectionalGraph<int, Edge<int>> reducedNetwork = new BidirectionalGraph<int, Edge<int>>();
            int tSource;
            int tDest;

            //the basic network has been generated
            //now generate the network where this one is copied over time instances
            for (int t = 0; t <= T; t++)
            {
                for (int i = 0; i < originalNetwork.VertexCount; i++)
                {
                    reducedNetwork.AddVertex((t << YoungsAlgorithm.NumBitsForSource) ^ i);
                }
            }

            //generate the edges of reduced network
            foreach (Edge<int> e in originalNetwork.Edges)
            {
                tSource = e.Source;
                tDest = e.Target;

                for (int t = 0; t <= T; t++)
                {
                    Edge<int> newE = new Edge<int>((t << YoungsAlgorithm.NumBitsForSource) ^ tSource, (t << YoungsAlgorithm.NumBitsForSource) ^ tDest);
                    reducedNetwork.AddEdge(newE);
                }
            }
            return reducedNetwork;
        }

        public void dumpGAMS(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha
            )
        {
            StreamWriter writer = new StreamWriter("network.gms");
            writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");
            //-------------------------------------------------------------
            writer.WriteLine("Parameters arrival(r)  arrival of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].arrival);
            }
            writer.WriteLine("/;");
            //--------------------------------------------------------------
            writer.WriteLine("Parameters deadline(r)  deadline of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].deadline);
            }
            writer.WriteLine("/;");
            //---------------------------------------------------------------
            writer.WriteLine("Parameters source(r)  source of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].src);
            }
            writer.WriteLine("/;");
            //---------------------------------------------------------------
            writer.WriteLine("Parameters destination(r)  destination of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].dest);
            }
            writer.WriteLine("/;");
            //---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0} \t {1}", e,
                    edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                e++;
            }
            writer.WriteLine("/;");
            //---------------------------------------------------------------
            writer.WriteLine("Parameters src(e)  source vertex of edge e");
            writer.WriteLine("/");
            e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0} \t {1}", e, edge.Source);
                e++;
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Parameters target(e)  target vertex of edge e");
            writer.WriteLine("/");
            e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0} \t {1}", e, edge.Target);
                e++;
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Parameters dup(i) dupi is same as i");
            writer.WriteLine("/");
            for (int i = 0; i < originalNetwork.VertexCount; i++)
            {
                writer.WriteLine("{0} \t {1}", i, i);
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Parameters dupt(t) dupt is same as t");
            writer.WriteLine("/");
            for (int t = 0; t <= T; t++)
            {
                writer.WriteLine("{0} \t {1}", t, t);
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(e,t,r)");
            writer.WriteLine("beta");
            writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            writer.WriteLine("FlowConservationConstraint(i,t,r)");
            writer.WriteLine("ZeroConstraint(e,t,r)");
            writer.WriteLine("DemandConstraint(r);");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((r),f(e,t,r)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((e,t)$(src(e) eq source(r)) , f(e,t,r)) - Sum((e,t)$(target(e) eq source(r)) , f(e,t,r)) =g= alpha * demand(r);");
            writer.WriteLine("FlowConservationConstraint(i,t,r)$((source(r) ne dup(i)) and (destination(r) ne dup(i))) .. Sum((e)$(target(e) eq dup(i)) , f(e,t,r)) =e= Sum((e)$(src(e) eq dup(i)) , f(e,t,r));");
            writer.WriteLine("ZeroConstraint(e,t,r) .. f(e,t,r)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp minimizing beta;");

            writer.Close();
        }


        public void dumpGAMSAllPaths(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha
            )
        {

            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();


            StreamWriter writer = new StreamWriter("networkAllPaths.gms");
            writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);


            writer.WriteLine("Set active(r,t)  whether rth request is active at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {

                writer.WriteLine("{0}.{1}*{2}", r, requests[r].arrival, requests[r].deadline);
    
                //for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                    //    writer.WriteLine("{0}\t.{1}", r, t);
            }
            writer.WriteLine("/;");


            //---------------------------------------------------------------
            writer.WriteLine("Set src(e,i)  whether edge e has node i as source");
            writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0}\t.{1}", e, edge.Source);
                e++;
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Set tgt(e,i)  whether edge e has node i as target");
            writer.WriteLine("/");
            e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0}\t.{1}", e, edge.Target);
                e++;
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------


            //------------------------------------------------------------



            //------------------------------------------------------------

            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");
            e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0} \t {1}", e,edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);
                e++;
            }
            writer.WriteLine("/;");

            ////------------------------------------------------------------

            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");
            ////-------------------------------------------------------------
            //writer.WriteLine("Parameters arrival(r)  arrival of request r");
            //writer.WriteLine("/");
            //for (int r = 0; r < requests.Count; r++)
            //{
            //    writer.WriteLine("{0} \t {1}", r, requests[r].arrival);
            //}
            //writer.WriteLine("/;");
            ////--------------------------------------------------------------
            //writer.WriteLine("Parameters deadline(r)  deadline of request r");
            //writer.WriteLine("/");
            //for (int r = 0; r < requests.Count; r++)
            //{
            //    writer.WriteLine("{0} \t {1}", r, requests[r].deadline);
            //}
            //writer.WriteLine("/;");
            ////---------------------------------------------------------------
            writer.WriteLine("Parameters source(r)  source of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].src);
            }
            writer.WriteLine("/;");
            //---------------------------------------------------------------
            writer.WriteLine("Parameters destination(r)  destination of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].dest);
            }
            writer.WriteLine("/;");
            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            //int e = 0;
            //foreach (Edge<int> edge in originalNetwork.Edges)
            //{
            //    writer.WriteLine("{0} \t {1}", e,
            //        edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            //    e++;
            //}
            //writer.WriteLine("/;");
            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters src(e)  source vertex of edge e");
            //writer.WriteLine("/");
            //e = 0;
            //foreach (Edge<int> edge in originalNetwork.Edges)
            //{
            //    writer.WriteLine("{0} \t {1}", e, edge.Source);
            //    e++;
            //}
            //writer.WriteLine("/;");
            ////----------------------------------------------------------------
            //writer.WriteLine("Parameters target(e)  target vertex of edge e");
            //writer.WriteLine("/");
            //e = 0;
            //foreach (Edge<int> edge in originalNetwork.Edges)
            //{
            //    writer.WriteLine("{0} \t {1}", e, edge.Target);
            //    e++;
            //}
            //writer.WriteLine("/;");
            ////----------------------------------------------------------------
            writer.WriteLine("Parameters dup(i) dupi is same as i");
            writer.WriteLine("/");
            for (int i = 0; i < originalNetwork.VertexCount; i++)
            {
                writer.WriteLine("{0} \t {1}", i, i);
            }
            writer.WriteLine("/;");
            ////----------------------------------------------------------------
            //writer.WriteLine("Parameters dupt(t) dupt is same as t");
            //writer.WriteLine("/");
            //for (int t = 0; t <= T; t++)
            //{
            //    writer.WriteLine("{0} \t {1}", t, t);
            //}
            //writer.WriteLine("/;");
            ////----------------------------------------------------------------







            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(e,t,r)");
            writer.WriteLine("beta");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
           writer.WriteLine("CapacityConstraints(e,t)");
            writer.WriteLine("FlowConservationConstraint(i,t,r)");
           // writer.WriteLine("ZeroConstraint(e,t,r)");
           writer.WriteLine("DemandConstraint(r,i);");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum(active(r,t),f(e,t,r)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r,i)$(source(r) = dup(i)) .. Sum((src(e,i),active(r,t)) , f(e,t,r)) - Sum((tgt(e,i),active(r,t)) , f(e,t,r)) =g= alpha * demand(r);");
            writer.WriteLine("FlowConservationConstraint(i,t,r)$((active(r,t)) and (source(r) ne dup(i)) and (destination(r) ne dup(i))) .. Sum(tgt(e,i) , f(e,t,r)) =e= Sum(src(e,i) , f(e,t,r));");
           // writer.WriteLine("ZeroConstraint(e,t,r) .. f(e,t,r)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp minimizing beta;");

            writer.Close();
        }

        public void dumpGAMSPaths(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,
            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("network.gms");
        //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count- 1);
            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");
            //-------------------------------------------------------------
            writer.WriteLine("Parameters arrival(r)  arrival of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].arrival);
            }
            writer.WriteLine("/;");
            //--------------------------------------------------------------
            writer.WriteLine("Parameters deadline(r)  deadline of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].deadline);
            }
            writer.WriteLine("/;");
            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters source(r)  source of request r");
            //writer.WriteLine("/");
            //for (int r = 0; r < requests.Count; r++)
            //{
            //    writer.WriteLine("{0} \t {1}", r, requests[r].src);
            //}
            //writer.WriteLine("/;");
            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters destination(r)  destination of request r");
            //writer.WriteLine("/");
            //for (int r = 0; r < requests.Count; r++)
            //{
            //    writer.WriteLine("{0} \t {1}", r, requests[r].dest);
            //}
            //writer.WriteLine("/;");
            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                writer.WriteLine("{0} \t {1}", e,
                    edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);
               
                e++;
            }
            writer.WriteLine("/;");
            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Parameters hasPath(r,p)  whether rth request has path p");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++ )
                    writer.WriteLine("{0}\t.{1}\t=\t1", r, pathEnum[stKey][j]);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------
            writer.WriteLine("Parameters path(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}\t=\t1", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");
            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters src(e)  source vertex of edge e");
            //writer.WriteLine("/");
            //e = 0;
            //foreach (Edge<int> edge in originalNetwork.Edges)
            //{
            //    writer.WriteLine("{0} \t {1}", e, edge.Source);
            //    e++;
            //}
            //writer.WriteLine("/;");
            ////----------------------------------------------------------------
            //writer.WriteLine("Parameters target(e)  target vertex of edge e");
            //writer.WriteLine("/");
            //e = 0;
            //foreach (Edge<int> edge in originalNetwork.Edges)
            //{
            //    writer.WriteLine("{0} \t {1}", e, edge.Target);
            //    e++;
            //}
            //writer.WriteLine("/;");
            ////----------------------------------------------------------------
            //writer.WriteLine("Parameters dup(i) dupi is same as i");
            //writer.WriteLine("/");
            //for (int i = 0; i < originalNetwork.VertexCount; i++)
            //{
            //    writer.WriteLine("{0} \t {1}", i, i);
            //}
            //writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Parameters dupt(t) dupt is same as t");
            writer.WriteLine("/");
            for (int t = 0; t <= T; t++)
            {
                writer.WriteLine("{0} \t {1}", t, t);
            }
            writer.WriteLine("/;");
            //----------------------------------------------------------------
            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(p,t,r)");
            writer.WriteLine("beta");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            writer.WriteLine("ZeroConstraint(p,t,r)");
            writer.WriteLine("DemandConstraint(r);");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((r,p)$((path(p,e) eq 1) and (hasPath(r,p) eq 1)), f(p,t,r)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((p,t)$(hasPath(r,p) eq 1) , f(p,t,r)) =g= alpha * demand(r);");
            writer.WriteLine("ZeroConstraint(p,t,r) .. f(p,t,r)$((hasPath(r,p) eq 0) or (dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp minimizing beta;");

            writer.Close();
        }

        public void dumpGAMSPathsE(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,
            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("network2.gms");
            //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count - 1);

            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Set hasPath(r,p,t)  whether rth request has path p at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++)
                    for (int t = requests[r].arrival ; t<= requests[r].deadline ; t++)
                        writer.WriteLine("{0}\t.{1}\t.{2}", r, pathEnum[stKey][j],t);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------


            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
              //  writer.WriteLine("{0} \t {1}", e,
               //     edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);

                e++;
            }
            //writer.WriteLine("/;");

            ////------------------------------------------------------------
            writer.WriteLine("Set edgeInPath(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");


            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");
         
            foreach (Edge<int> edge in originalNetwork.Edges)
                writer.WriteLine("{0} \t {1}", edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target],
                edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            writer.WriteLine("/;");


            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");
            
            //----------------------------------------------------------------
            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(r,p,t)");
            writer.WriteLine("beta");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            //writer.WriteLine("ZeroConstraint(r,p,t)");
            writer.WriteLine("DemandConstraint(r);");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum(haspath(r,p,t)$edgeInPath(p,e), f(r,p,t)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum(haspath(r,p,t), f(r,p,t)) =g= alpha * demand(r);");
            //writer.WriteLine("ZeroConstraint(r,p,t) .. f(r,p,t)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp minimizing beta;");

            writer.Close();
        }



        public void dumpGAMSPathsEb(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,
            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("network2b.gms");
            //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count - 1);

            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Set hasPath(r,p)  whether rth request has path p");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++)
               //     for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                        writer.WriteLine("{0}\t.{1}", r, pathEnum[stKey][j]);
            }
            writer.WriteLine("/;");

            //--------------------------------------------------------------------------

            writer.WriteLine("Set active(r,t)  whether rth request is active at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0}.{1}*{2}", r, requests[r].arrival, requests[r].deadline);
                //for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                //    writer.WriteLine("{0}\t.{1}", r, t);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------


            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                //  writer.WriteLine("{0} \t {1}", e,
                //     edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);

                e++;
            }
            //writer.WriteLine("/;");

            ////------------------------------------------------------------
            writer.WriteLine("Set edgeInPath(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");


            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");

            foreach (Edge<int> edge in originalNetwork.Edges)
                writer.WriteLine("{0} \t {1}", edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target],
                edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            writer.WriteLine("/;");


            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");

            //----------------------------------------------------------------
            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(r,p,t)");
            writer.WriteLine("beta");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            //writer.WriteLine("ZeroConstraint(r,p,t)");
            writer.WriteLine("DemandConstraint(r);");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((haspath(r,p),active(r,t))$edgeInPath(p,e), f(r,p,t)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =g= alpha * demand(r);");
            //writer.WriteLine("ZeroConstraint(r,p,t) .. f(r,p,t)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp minimizing beta;");

            writer.Close();
        }


        public void dumpGAMSPathsMaxAlpha(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,
            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("networkMaxAlpha.gms");
            //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count - 1);

            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Set hasPath(r,p)  whether rth request has path p");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++)
                    //     for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                    writer.WriteLine("{0}\t.{1}", r, pathEnum[stKey][j]);
            }
            writer.WriteLine("/;");

            //--------------------------------------------------------------------------

            writer.WriteLine("Set active(r,t)  whether rth request is active at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0}.{1}*{2}", r, requests[r].arrival, requests[r].deadline);
                //for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                //    writer.WriteLine("{0}\t.{1}", r, t);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------


            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                //  writer.WriteLine("{0} \t {1}", e,
                //     edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);

                e++;
            }
            //writer.WriteLine("/;");

            ////------------------------------------------------------------
            writer.WriteLine("Set edgeInPath(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");


            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");

            foreach (Edge<int> edge in originalNetwork.Edges)
                writer.WriteLine("{0} \t {1}", edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target],
                edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            writer.WriteLine("/;");


            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");

            //----------------------------------------------------------------
     //       writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(r,p,t)");
            writer.WriteLine("alpha");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            //writer.WriteLine("ZeroConstraint(r,p,t)");
            writer.WriteLine("DemandConstraint(r)");
            writer.WriteLine("UpperDemandConstraint(r);");
            //----------------------------------------------------------------
            //writer.WriteLine("CapacityConstraints(e,t) .. Sum((haspath(r,p),active(r,t))$edgeInPath(p,e), f(r,p,t)) =l= beta * capacity(e);");
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((haspath(r,p),active(r,t))$edgeInPath(p,e), f(r,p,t)) =l= capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =g= alpha * demand(r);");
            writer.WriteLine("UpperDemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =l= demand(r);");
            //writer.WriteLine("ZeroConstraint(r,p,t) .. f(r,p,t)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp maximizing alpha;");

            writer.Close();
        }


        public void dumpGAMSPathsMaxWeightedFlow(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,

            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("networkWeightedFlow.gms");
            //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count - 1);

            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Set hasPath(r,p)  whether rth request has path p");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++)
                    //     for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                    writer.WriteLine("{0}\t.{1}", r, pathEnum[stKey][j]);
            }
            writer.WriteLine("/;");

            //--------------------------------------------------------------------------

            writer.WriteLine("Set active(r,t)  whether rth request is active at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0}.{1}*{2}", r, requests[r].arrival, requests[r].deadline);
                //for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                //    writer.WriteLine("{0}\t.{1}", r, t);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------


            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                //  writer.WriteLine("{0} \t {1}", e,
                //     edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);

                e++;
            }
            //writer.WriteLine("/;");

            ////------------------------------------------------------------
            writer.WriteLine("Set edgeInPath(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");


            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");

            foreach (Edge<int> edge in originalNetwork.Edges)
                writer.WriteLine("{0} \t {1}", edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target],
                edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            writer.WriteLine("/;");


            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");

            //----------------------------------------------------------------
            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(r,p,t)");
            writer.WriteLine("delta");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            //writer.WriteLine("ZeroConstraint(r,p,t)");
            writer.WriteLine("DemandConstraint(r)");
            writer.WriteLine("UpperDemandConstraint(r)");
            writer.WriteLine("TotalFlowConstraint;");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((haspath(r,p),active(r,t))$edgeInPath(p,e), f(r,p,t)) =l= capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =g= alpha * demand(r);");
            writer.WriteLine("UpperDemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =l= demand(r);");
            writer.WriteLine("TotalFlowConstraint .. Sum((haspath(r,p),active(r,t)), f(r,p,t)/demand(r)) =e= delta;");
            //writer.WriteLine("ZeroConstraint(r,p,t) .. f(r,p,t)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp maximizing delta;");

            writer.Close();
        }

        public void dumpGAMSPathsMinBeta(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,
            double delta,
            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("networkBeta.gms");
            //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count - 1);

            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Set hasPath(r,p)  whether rth request has path p");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++)
                    //     for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                    writer.WriteLine("{0}\t.{1}", r, pathEnum[stKey][j]);
            }
            writer.WriteLine("/;");

            //--------------------------------------------------------------------------

            writer.WriteLine("Set active(r,t)  whether rth request is active at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0}.{1}*{2}", r, requests[r].arrival, requests[r].deadline);
                //for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                //    writer.WriteLine("{0}\t.{1}", r, t);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------


            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                //  writer.WriteLine("{0} \t {1}", e,
                //     edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);

                e++;
            }
            //writer.WriteLine("/;");

            ////------------------------------------------------------------
            writer.WriteLine("Set edgeInPath(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");


            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");

            foreach (Edge<int> edge in originalNetwork.Edges)
                writer.WriteLine("{0} \t {1}", edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target],
                edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            writer.WriteLine("/;");


            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");

            //----------------------------------------------------------------
            writer.WriteLine("Scalar alpha /{0}/;", alpha);
            writer.WriteLine("Scalar delta /{0}/;", delta);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(r,p,t)");
            writer.WriteLine("beta");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            //writer.WriteLine("ZeroConstraint(r,p,t)");
            writer.WriteLine("DemandConstraint(r)");
            writer.WriteLine("UpperDemandConstraint(r)");
            writer.WriteLine("TotalFlowConstraint;");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((haspath(r,p),active(r,t))$edgeInPath(p,e), f(r,p,t)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =g= alpha * demand(r);");
            writer.WriteLine("UpperDemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =l= demand(r);");
            writer.WriteLine("TotalFlowConstraint .. Sum((haspath(r,p),active(r,t)), f(r,p,t)/demand(r)) =g= delta;");
            //writer.WriteLine("ZeroConstraint(r,p,t) .. f(r,p,t)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp minimizing beta;");

            writer.Close();
        }

        public void dumpGAMSPathsMaxSingleObjective(
            BidirectionalGraph<int, Edge<int>> originalNetwork,
            List<Request> requests,
            int T,
            Dictionary<int, double> edgeCapacities,
            double alpha,
            double delta,
            Dictionary<int, List<Path>> pathDictionary
            )
        {

            int cnt = 0;
            Dictionary<int, List<int>> pathEnum = new Dictionary<int, List<int>>();
            Dictionary<int, int> edgeEnum = new Dictionary<int, int>();

            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                List<int> plistInInt = new List<int>();

                for (int p = 0; p < plist.Count; p++)
                    plistInInt.Add(cnt + p);

                pathEnum.Add(p_key, plistInInt);
                cnt += plist.Count;
            }

            int path_count = pathDictionary.Sum(x => x.Value.Count());



            StreamWriter writer = new StreamWriter("networkSingleObjective.gms");
            //    writer.WriteLine("Set i nodes of graph /{0} * {1}/;", 0, originalNetwork.VertexCount - 1);
            writer.WriteLine("Set e edges /{0} * {1}/;", 0, originalNetwork.EdgeCount - 1);
            writer.WriteLine("Set r requests /{0} * {1}/;", 0, requests.Count - 1);
            writer.WriteLine("Set t time /{0} * {1}/;", 0, T);
            writer.WriteLine("Set p paths /{0} * {1}/;", 0, path_count - 1);

            ////---------------------------------------------------------------
            //------------------------------------------------------------
            writer.WriteLine("Set hasPath(r,p)  whether rth request has path p");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                int stKey = (requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest);
                for (int j = 0; j < pathEnum[stKey].Count; j++)
                    //     for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                    writer.WriteLine("{0}\t.{1}", r, pathEnum[stKey][j]);
            }
            writer.WriteLine("/;");

            //--------------------------------------------------------------------------

            writer.WriteLine("Set active(r,t)  whether rth request is active at time t");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0}.{1}*{2}", r, requests[r].arrival, requests[r].deadline);
                //for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                //    writer.WriteLine("{0}\t.{1}", r, t);
            }
            writer.WriteLine("/;");

            //------------------------------------------------------------


            ////---------------------------------------------------------------
            //writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            //writer.WriteLine("/");
            int e = 0;
            foreach (Edge<int> edge in originalNetwork.Edges)
            {
                //  writer.WriteLine("{0} \t {1}", e,
                //     edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                edgeEnum.Add(edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target, e);

                e++;
            }
            //writer.WriteLine("/;");

            ////------------------------------------------------------------
            writer.WriteLine("Set edgeInPath(p,e)  whether path p has edge e in it");
            writer.WriteLine("/");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];

                for (int k = 0; k < plist.Count; k++)
                {
                    int pInt = pathEnum[p_key][k];
                    foreach (Edge<int> edge in plist[k].edgesList)
                        writer.WriteLine("{0}\t.{1}", pInt, edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
                }
            }
            writer.WriteLine("/;");


            ////---------------------------------------------------------------
            writer.WriteLine("Parameters capacity(e)  capacity of edge e");
            writer.WriteLine("/");

            foreach (Edge<int> edge in originalNetwork.Edges)
                writer.WriteLine("{0} \t {1}", edgeEnum[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target],
                edgeCapacities[edge.Source << YoungsAlgorithm.NumBitsForSource | edge.Target]);
            writer.WriteLine("/;");


            //------------------------------------------------------------
            writer.WriteLine("Parameters demand(r)  demand of request r");
            writer.WriteLine("/");
            for (int r = 0; r < requests.Count; r++)
            {
                writer.WriteLine("{0} \t {1}", r, requests[r].demand);
            }
            writer.WriteLine("/;");

            //----------------------------------------------------------------
            //writer.WriteLine("Scalar alpha /{0}/;", alpha);
            //writer.WriteLine("Scalar delta /{0}/;", delta);
            //----------------------------------------------------------------
            writer.WriteLine("Variables");
            writer.WriteLine("f(r,p,t)");
            writer.WriteLine("beta");
            writer.WriteLine("alpha");
            writer.WriteLine("delta");
            writer.WriteLine("util");
            //writer.WriteLine("dummybeta;");
            //----------------------------------------------------------------
            writer.WriteLine("Positive Variable f;");
            //----------------------------------------------------------------
            writer.WriteLine("Equations");
            writer.WriteLine("CapacityConstraints(e,t)");
            //writer.WriteLine("ZeroConstraint(r,p,t)");
            writer.WriteLine("DemandConstraint(r)");
            writer.WriteLine("UpperDemandConstraint(r)");
            writer.WriteLine("TotalFlowConstraint");
            writer.WriteLine("ObjectiveConstraint;");
            //----------------------------------------------------------------
            writer.WriteLine("CapacityConstraints(e,t) .. Sum((haspath(r,p),active(r,t))$edgeInPath(p,e), f(r,p,t)) =l= beta * capacity(e);");
            writer.WriteLine("DemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =e= alpha * demand(r);");
            writer.WriteLine("UpperDemandConstraint(r) .. Sum((haspath(r,p),active(r,t)), f(r,p,t)) =l= demand(r);");
            writer.WriteLine("TotalFlowConstraint .. Sum((haspath(r,p),active(r,t)), f(r,p,t)/demand(r)) =g= delta;");
            writer.WriteLine("ObjectiveConstraint .. util =e= alpha+delta/10/{0}-.001*beta;", requests.Count);
            //writer.WriteLine("ZeroConstraint(r,p,t) .. f(r,p,t)$((dupt(t) lt arrival(r)) or (dupt(t) gt deadline(r))) =e= 0;");
            //----------------------------------------------------------------
            writer.WriteLine("model calendaring	/all/;");
            writer.WriteLine("solve calendaring using lp maximizing util;");

            writer.Close();
        }



        // dump the network information into an xml file 
        public void dumpXML(
            BidirectionalGraph<int, Edge<int>> network, 
            List<Request> requests, 
            Dictionary<int, double> edgeCapacities, 
            Dictionary<int, List<Path>> pathDictionary, 
            int Tmax)
        {
            int nodeCount = network.VertexCount;
            StreamWriter file = new StreamWriter("Network.xml");
            file.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            file.WriteLine("<network>");
            file.WriteLine("<nodes>{0}</nodes>", nodeCount);
            file.WriteLine("<time>{0}</time>", Tmax);

            file.WriteLine("<edges>");
            int edge_count = 0;
            foreach (Edge<int> e in network.Edges)
            {
                file.WriteLine("<edge>");
                file.WriteLine("<end1> {0} </end1>", e.Source);
                file.WriteLine("<end2> {0} </end2>", e.Target);
                file.WriteLine("<id> {0} </id>", edge_count);
                file.WriteLine("<capacity> {0} </capacity>", edgeCapacities[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target]);
                file.WriteLine("</edge>");
                file.WriteLine();
                edge_count++;
            }
            file.WriteLine("</edges>");

            file.WriteLine("<reqs>");
            foreach (Request r in requests)
            {
                file.WriteLine("<req>");
                file.WriteLine("<source> {0} </source>", r.src);
                file.WriteLine("<destination> {0} </destination>", r.dest);
                file.WriteLine("<arrival> {0} </arrival>", r.arrival);
                file.WriteLine("<deadline> {0} </deadline>", r.deadline);
                file.WriteLine("<demand> {0} </demand>", r.demand);
                file.WriteLine("<id> {0} </id>", 0);
                file.WriteLine("</req>");
                file.WriteLine();
            }
            file.WriteLine("</reqs>");

            file.WriteLine("\n<paths>");
            foreach (int p_key in pathDictionary.Keys)
            {
                List<Path> plist = pathDictionary[p_key];
                
                file.WriteLine(@"
<plist>
<from> {0} </from>
<to> {1} </to>
<count> {2} </count>", 
               p_key >> YoungsAlgorithm.NumBitsForSource, 
               p_key &  ((1<<YoungsAlgorithm.NumBitsForSource)-1),
               plist.Count);
               
                foreach (Path p in plist)
                {
                    file.WriteLine(@"
<path>");
                    foreach (Edge<int> e in p.edgesList)
                        file.WriteLine(@"<edge> 
<source> {0} </source> 
<target> {1} </target> 
</edge>", 
                        e.Source, e.Target);
                    file.WriteLine("</path>");
                }
                file.WriteLine(@"
</plist>
");
            }
            file.WriteLine("</paths>");
            
            file.WriteLine("</network>");
            file.Close();

        }

        //Takes an xml file and constructs a QuickGraph.BidirectionalGraph from it
        //public BidirectionalGraph<int, Edge<int>> readFromXML()
        //{
        //    BidirectionalGraph<int, Edge<int>> network = new BidirectionalGraph<int, Edge<int>>();
        //    XDocument doc = XDocument.Load("NOFeasible.xml");

        //    var req_count_query = from r in doc.Element("network").Element("reqs").Elements("req") select r;
        //    int req_count = req_count_query.Count();

        //    var edge_count_query = from e in doc.Element("network").Element("edges").Elements("edge") select e;
        //    int src, tg;
        //    foreach (var e in edge_count_query)
        //    {
        //        src = Convert.ToInt32(e.Element("end1").Value);
        //        tg = Convert.ToInt32(e.Element("end2").Value);
        //        network.AddVerticesAndEdge(new Edge<int>(src,tg));

        //        edgeCapacities[new Tuple<int, int>(src, tg)] = Convert.ToDouble(e.Element("capacity").Value);
        //    }

        //    double dem;
        //    int arr, dead;
        //    foreach (var r in req_count_query)
        //    {
        //        src = Convert.ToInt32(r.Element("source").Value);
        //        tg = Convert.ToInt32(r.Element("destination").Value);
        //        dem = Convert.ToDouble(r.Element("demand").Value);
        //        arr = Convert.ToInt32(r.Element("arrival").Value);
        //        dead = Convert.ToInt32(r.Element("deadline").Value);
        //        Request req = new Request(src,tg,arr,dead,dem);
        //        requests.Add(req);
        //    }

        //    return network;
        //}

        public bool PathsDiffUseful(Path p1, Path p2)
        {
            Debug.Assert(p1.source == p2.source && p1.target == p2.target);

            // edge weights to dijkstra are positive; so neither p1 nor p2 can have cycles
            

            // check if they are identical
            if (p1.edgesList.Count != p2.edgesList.Count) return true;

            for (int ind = 0; ind < p1.edgesList.Count; ind++ )
            {
                Edge<int>
                    e1 = p1.edgesList[ind],
                    e2 = p2.edgesList[ind];

                if (e1.Source != e2.Source || e1.Target != e2.Target)
                    return true;
            }
            return false;
        }

        public Dictionary<int, List<Path>> ComputePathDictionary
            (List<Request> requests, BidirectionalGraph<int, Edge<int>> network, int K)
        {
            Stopwatch sw = Stopwatch.StartNew();

            Dictionary<int, List<Path>> pathDictionary =
                            new Dictionary<int, List<Path>>();
            int nodeCount = network.VertexCount;

            Dijkstra dijkstra = new Dijkstra();
            bool fullSolve = requests.Count > 10 * nodeCount;

            int num_ident_paths_skipped = 0;

            Dictionary<int, double> edgeLengths = new Dictionary<int, double>();
            foreach (Request r in requests)
            {
                int
                    i = r.src,
                    j = r.dest;

                if (i == j) continue;

                int e_k = i << YoungsAlgorithm.NumBitsForSource | j;
                
                if (pathDictionary.ContainsKey(e_k))
                    continue;

                //Setting up edgeLengths
                foreach (Edge<int> e in network.Edges)
                {
                    edgeLengths[
                        e.Source << YoungsAlgorithm.NumBitsForSource | e.Target
                        // new Tuple<int, int>(e.Source, e.Target)
                    ] = 1;
                }

                for (int k = 0; k < K; k++)
                {
                    Dictionary<int, int> routeMatrix = dijkstra.Solve(network, i, j, edgeLengths, fullSolve);
                    Path pij = null;

                    foreach (int target in (fullSolve? routeMatrix.Keys.ToList<int>(): new List<int>(){j}))
                    {
                        Path sp = new Path(i, target);

                        for(int _next = target; _next != i; _next = routeMatrix[_next])
                            sp.edgesList.Add( new Edge<int>( routeMatrix[_next], _next ));
                        sp.edgesList.Reverse();

                        if ( target == j ) pij = sp;

                        int e_i_target = i << YoungsAlgorithm.NumBitsForSource | target;

                        if (!pathDictionary.ContainsKey(e_i_target))
                            pathDictionary.Add(e_i_target, new List<Path>());

                        bool isNewPathUseful =
                            pathDictionary[e_i_target].All(
                            p => PathsDiffUseful(p, sp));

                        if (isNewPathUseful)
                        {
                            pathDictionary[e_i_target].Add(sp);
                        }
                        else
                        {
                            // Console.WriteLine("skip ident path");
                            num_ident_paths_skipped++;
                        }
                    }

                    // increase weights of used edges
                    if (fullSolve)
                        foreach (KeyValuePair<int, int> kvp in routeMatrix)
                        {
                            edgeLengths[
                                kvp.Value << YoungsAlgorithm.NumBitsForSource | kvp.Key
                                // new Tuple<int, int>(e.Source, e.Target)
                                ] += nodeCount;
                        }
                    else
                    {
                        int up = pij.edgesList.Count;
                        foreach (Edge<int> e in pij.edgesList)
                            edgeLengths[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target] += up;
                    }
                }

                /*
                Console.WriteLine("\n{0} {1}", i, j);
                foreach (Path p in pathDictionary[new Tuple<int, int>(i, j)])
                {
                    Console.WriteLine();
                    foreach (Edge<int> e in p.edgesList)
                    {
                        Console.Write("<{0},{1}>", e.Source, e.Target);
                    }
                }
                Console.WriteLine("--------------------");
                */
            }

            sw.Stop();
            Console.WriteLine("PathCompute: {0} reqs ret {1} paths s-d pairs {3} time {2}ms skipped {4}", 
                requests.Count, 
                pathDictionary.Values.Sum(i => i.Count), 
                sw.ElapsedMilliseconds, 
                pathDictionary.Keys.Count,
                num_ident_paths_skipped);
            return pathDictionary;
        }
    }
}
