using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using QuickGraph;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    public class AugRequest: Request
    {
        static int NextAugRequestId = 0;
        public double totalFlow;
        public double[] promisedAlphas;
        public int id;

        public AugRequest(Request r): base(r.src, r.dest, r.arrival, r.deadline, r.demand, r.awareTime)
        {
            promisedAlphas = new double[r.deadline - r.awareTime + 1];
            totalFlow = 0;
            id = AugRequest.NextAugRequestId ++;
        }
        public override string ToString()
        {
            return String.Format("AR {0} {1} {2} alphas: {3}", 
                id, 
                (totalFlow/ demand).ToString("F4"),
                base.ToString(),
                String.Join(",", promisedAlphas));
        }
    }

    /// <summary>
    /// this holds relevant information over all time
    /// </summary>
    public class S14o_GlobalState
    {
        public const double InitZsTo = 1000000000000, ScaleZsBy = 1000000;
        public static int BetaExponentScale =50;

        public Dictionary<int, AugRequest> augmentedRequests;  // read-write copy
        public Dictionary<int, double> finishedEdges;

        public S14o_GlobalState(S14_YoungsAlgorithm y)
        {
            augmentedRequests = new Dictionary<int,AugRequest>();

            foreach(Request r in y.requests)
            {
                AugRequest ar = new AugRequest(r);
                augmentedRequests.Add(ar.id, ar);
            }

            finishedEdges = new Dictionary<int,double>();
        }
        public const double epsilon = .2;

        internal void PrintSummary(S14_YoungsAlgorithm yRef)
        {
            Console.WriteLine("------ran out of time---------------");
            MeanStdNum
                msn_fin_alpha = new MeanStdNum(),
                msn_fin_linkUsages = new MeanStdNum();
            int numFullfilled = 0;
            foreach (AugRequest ar in augmentedRequests.Values)
            {
                Console.WriteLine("---- Req {0}", ar);

                msn_fin_alpha.AddSample(ar.totalFlow / ar.demand);

                if (ar.totalFlow > .999 * ar.demand)
                    numFullfilled++;
            }
            foreach (int edgeIndex in finishedEdges.Keys)
            {
                int e_k = edgeIndex & ((1 << (2 * YoungsAlgorithm.NumBitsForSource)) - 1);
                msn_fin_linkUsages.AddSample(finishedEdges[edgeIndex] / yRef.edgeCapacities[e_k]);
            }
            Console.WriteLine("Flow Sats. {0}", msn_fin_alpha.GetDetailedString());
            Console.WriteLine("Usage all edges {0}", msn_fin_linkUsages.GetDetailedString());
            Console.WriteLine("Num fulfilled: {0}", numFullfilled);
        }
    }

    /// <summary>
    /// this is an in-between state to record output of alpha exploration and allow delta exploration
    /// </summary>
    public class S14o_CurrentLPSilverState
    {
        public AugRequest[] activeRequests;

        public int currTimestep;
        public S14_YoungsAlgorithm yRef;  // requests and other shit 
        public S14o_GlobalState gsRef;


        public double[] requestFlow;
        public double[][] requestFlowByTime;
        public double[] promisedAlpha;
        public double[] priorFlow;

        public Dictionary<int, double> edgeFlows;
        public Dictionary<int, double> edgeLengths;
        public Dictionary<int, double> hardBetas;

        public int timesZhasBeenScaled;

        public S14o_CurrentLPSilverState(S14o_CurrentLPDirtyState sDs)
        {
            yRef = sDs.yRef;
            gsRef = sDs.gsRef;

            currTimestep = sDs.currTimestep;

            activeRequests = sDs.activeRequests;
            timesZhasBeenScaled = sDs.timesZhasBeenScaled;

            edgeFlows = new Dictionary<int,double>();
            edgeLengths = new Dictionary<int,double>();
            hardBetas = new Dictionary<int, double>();
            
            Debug.Assert(edgeLengths.Count == edgeFlows.Count);

            foreach(int edgeIndex in sDs.edgeFlows.Keys)
            {
                edgeFlows.Add(edgeIndex, sDs.edgeFlows[edgeIndex]);

                //
                // because we allow grace at end of \alpha exploration, we have to correct the constraint below
                //
                int timestamp = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << 2*YoungsAlgorithm.NumBitsForSource) -1);

                double beta_specified = Math.Exp(-1.0 * (timestamp - currTimestep) / S14o_GlobalState.BetaExponentScale);
                double actual_beta = edgeFlows[edgeIndex] / yRef.edgeCapacities[e_k];  // can be higher because of grace

                double real_beta = Math.Max(beta_specified, actual_beta);

                double newEdgeLength = 
                    Math.Pow(sDs.edgeLengths[edgeIndex] * beta_specified * yRef.edgeCapacities[e_k], beta_specified / real_beta)
                    / real_beta 
                    / yRef.edgeCapacities[e_k];

                if (real_beta > beta_specified)
                {
                    int i = 0;
                    i++;
                }

                edgeLengths.Add(edgeIndex, newEdgeLength);
                hardBetas.Add(edgeIndex, real_beta);
            }

            requestFlow = new double[activeRequests.Length];
            requestFlowByTime = new double[activeRequests.Length][];
            promisedAlpha = new double[activeRequests.Length];
            priorFlow = new double[activeRequests.Length];

            for (int i = 0; i < activeRequests.Length; i++)
            {
                requestFlow[i] = sDs.requestFlow[i];
                promisedAlpha[i] = sDs.promisedAlpha[i];
                priorFlow[i] = sDs.priorFlow[i];
                
                requestFlowByTime[i] = new double[yRef.T - currTimestep +1];
                for(int t=0; t < yRef.T - currTimestep + 1; t++)
                    requestFlowByTime[i][t] = sDs.requestFlowByTime[i][t];
            }
        }
    }

    /// <summary>
    /// this maintains "starting point" information for the current state
    /// </summary>
    public class S14o_CurrentLPGoldState
    {
        public AugRequest[] activeRequests;

        public int currTimestep;
        public S14_YoungsAlgorithm yRef;  // requests and other shit 
        public S14o_GlobalState gsRef;


        public double[] requestFlow;
        public double[][] requestFlowByTime;
        public double[] promisedAlpha;
        public double[] priorFlow;
        
        public Dictionary<int, double> edgeFlows;
        public Dictionary<int, double> edgeLengths;
        public Dictionary<int, double> hardBetas;

        public int timesZhasBeenScaled;

        /// <summary>
        /// here, we initialize the gold state for the next timestep based on the "best" dirty state from the current timestep
        /// </summary>
        /// <param name="?"></param>
        public S14o_CurrentLPGoldState(S14o_CurrentLPDirtyState sDs)
        {
            yRef = sDs.yRef;
            gsRef = sDs.gsRef;

            currTimestep = sDs.currTimestep + 1;
            timesZhasBeenScaled = sDs.timesZhasBeenScaled;

            edgeFlows = new Dictionary<int, double>();
            edgeLengths = new Dictionary<int, double>();
            hardBetas = new Dictionary<int, double>();

            foreach(int edgeIndex in sDs.edgeFlows.Keys)
            {
                int timestamp = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << 2*YoungsAlgorithm.NumBitsForSource) -1);

                // remove "old" edges
                if (timestamp < currTimestep)
                {
                    gsRef.finishedEdges.Add(edgeIndex, sDs.edgeFlows[edgeIndex]);

                    int s = e_k >> YoungsAlgorithm.NumBitsForSource;
                    int t = e_k & ((1 << YoungsAlgorithm.NumBitsForSource) - 1);

                    Console.WriteLine("Finished edge usage {3} {0} -> {1} @ {2}  cap {4}", 
                        s, t, timestamp, 
                        (sDs.edgeFlows[edgeIndex]/ yRef.edgeCapacities[e_k]).ToString("F4"), 
                        yRef.edgeCapacities[e_k]);
                }
                else
                {
                    // copy over "still active" edges
                    edgeFlows.Add(edgeIndex, sDs.edgeFlows[edgeIndex]);
                }

                // sk: we have to add new edges in the bounded horizon case; skip for nwo.
            }

            foreach(int edgeIndex in sDs.edgeLengths.Keys)
            {
                int timestamp = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << 2*YoungsAlgorithm.NumBitsForSource) -1);

                if (timestamp < currTimestep)
                {
                    continue; // ignore the old edges
                }
                else
                {
                    // re-scale these
                    double old_beta =
                        Math.Exp(-1.0 * (timestamp - currTimestep + 1) / S14o_GlobalState.BetaExponentScale);
                    double new_beta =
                        Math.Exp(-1.0 * (timestamp - currTimestep) / S14o_GlobalState.BetaExponentScale);

                    //
                    // this is to make sure that the grace we allowed on the packing constraints does not lead to 
                    // infeasibility in the next timestep
                    // 
                    double actual_beta = edgeFlows[edgeIndex] / yRef.edgeCapacities[e_k];
                    double real_beta = Math.Max(new_beta, actual_beta);

                    double newEdgeLength = 
                        Math.Pow(sDs.edgeLengths[edgeIndex] * old_beta * yRef.edgeCapacities[e_k], old_beta / real_beta)
                        / real_beta / yRef.edgeCapacities[e_k];

                    if (real_beta > old_beta)
                    {
                        int i = 0;
                        i ++;
                    }

                    hardBetas[edgeIndex] = real_beta;

                    edgeLengths[edgeIndex] = newEdgeLength;
                }
            }

            // let's count how many requests will be active
            int x = 0;
            foreach(AugRequest r in sDs.activeRequests)
            {
                if(r.deadline >= currTimestep)
                    x++;
            }
            foreach(AugRequest r in gsRef.augmentedRequests.Values)
            {
                if (r.awareTime == currTimestep)
                    x++;
            }

            // now create datastructures
            activeRequests = new AugRequest[x];
            // z = new double[x];
            requestFlow = new double[x];
            requestFlowByTime = new double[x][];
            priorFlow = new double[x];
            promisedAlpha = new double[x];

            x = 0;
            // get the new requests
            foreach(AugRequest r in gsRef.augmentedRequests.Values)
            {
                if(r.awareTime == currTimestep)
                {
                    activeRequests[x] = r;

                    requestFlow[x] = 0;
                    requestFlowByTime[x] = new double[ yRef.T - currTimestep + 1];
                    promisedAlpha[x] = 0;
                    priorFlow[x] = 0;

                    x++;
                }
            }


            // now edit for the old requests
            for(int i=0; i < sDs.activeRequests.Length; i++)
            {
                AugRequest r = sDs.activeRequests[i];
                if (r.deadline >= currTimestep)
                {
                    activeRequests[x] = r;
                    promisedAlpha[x] = sDs.promisedAlpha[i];

                    requestFlow[x] = sDs.requestFlow[i] - sDs.requestFlowByTime[i][0];

                    requestFlowByTime[x] = new double[yRef.T - currTimestep + 1];

                    for(int t =0; t < yRef.T - currTimestep + 1; t++)
                        requestFlowByTime[x][t] = sDs.requestFlowByTime[i][t+1];

                    priorFlow[x] = sDs.priorFlow[i] + sDs.requestFlowByTime[i][0];

                    x++;
                }
            }

            //
            // record the promised alphas for all flows
            // for flows that are just done, record total flow
            //
            for (int ri = 0; ri < sDs.activeRequests.Length; ri++)
            {
                AugRequest r = sDs.activeRequests[ri];

                r.promisedAlphas[currTimestep - 1 - r.awareTime] =
                    sDs.promisedAlpha[ri];

                if (r.deadline == currTimestep - 1 ||
                   currTimestep == yRef.T)
                {
                    r.totalFlow = sDs.requestFlow[ri] + sDs.priorFlow[ri];
                    Console.WriteLine("Finished flow {0}", r);
                }
            }
        }

        public S14o_CurrentLPGoldState(S14_YoungsAlgorithm y, S14o_GlobalState gs)
        {
            yRef = y;
            gsRef = gs;

            currTimestep = 0; // not "timed" yet
            timesZhasBeenScaled = 0;

            edgeFlows = new Dictionary<int, double>();
            edgeLengths = new Dictionary<int, double>();
            hardBetas = new Dictionary<int, double>();

            foreach (Edge<int> e in yRef.orig_network.Edges)
            {
                int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                for (int t = 0; t <= yRef.T; t++)
                {
                    int t_k = t << 2 * YoungsAlgorithm.NumBitsForSource | e_k;
                    edgeFlows[t_k] = 0;

                    // these have both 1/c_e and 1/\beta; the hard-coded time-dependent beta embedded in them
                    hardBetas[t_k] = Math.Exp(-1.0 * t/ S14o_GlobalState.BetaExponentScale);
                    edgeLengths[t_k] = 1.0 / (yRef.edgeCapacities[e_k] * hardBetas[t_k]);
                }
            }

            //
            // pick currently active requests
            //
            List<AugRequest> temp_currentlyActiveRequests = new List<AugRequest>();
            foreach(AugRequest r in gs.augmentedRequests.Values)
                if(r.awareTime <= currTimestep &&
                    r.deadline >= currTimestep)
                    temp_currentlyActiveRequests.Add(r);

            activeRequests = temp_currentlyActiveRequests.ToArray<AugRequest>();


            // z = new double[activeRequests.Length];
            requestFlow = new double[activeRequests.Length];
            requestFlowByTime = new double[activeRequests.Length][];
            promisedAlpha = new double[activeRequests.Length];
            priorFlow = new double[activeRequests.Length];


            for (int i = 0; i < activeRequests.Length; i++)
            {
                requestFlow[i] = 0;
                // z[i] = S14o_GlobalState.InitZsTo;

                requestFlowByTime[i] = new double[yRef.T + 1];
                for(int t=0; t <= yRef.T; t++)
                    requestFlowByTime[i][t] = 0;

                promisedAlpha[i] = 0;

                priorFlow[i] = 0;
            }

        }
    }

    /// <summary>
    /// this maintains the current working set over internal variables shared across threads
    /// </summary>
    public class S14o_CurrentLPDirtyState
    {
        // read only
        public S14_YoungsAlgorithm yRef;
        public S14o_GlobalState gsRef;

        public AugRequest[] activeRequests;

        // for online
        public int currTimestep;

        //
        // partitioned state
        //
        // by request
        public double[] requestFlow;
        public double[] priorFlow; // read only

        // by request and time
        public double[][] requestFlowByTime;

        // by time
        public ConcurrentDictionary<int, double> edgeFlows;
        public ConcurrentDictionary<int, double> edgeLengths;
        public ConcurrentDictionary<int, double> hardBetas;

        // r-w conflicts
        public double[] z; // updates are mostly per-req, except "scaling"
        public bool[] yetToSatisfyReqs; // removing satisfied requests

        public double sumY, sumZ; // truly shared

        // for delta
        public double covering_r;
        public double totalDemand;
        public bool total_flow_active; // is this covering constraint still active
        public double totalDemand_satisfied;

        // to avoid numerical issues
        public int timesZhasBeenScaled;

        // try to copy and copy back
        // memorize the shortest paths for each request in each time graph
        public Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>
            req2shortestPathLength2time_and_index;
        public ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>
                req2time2shortestPathLength_and_index;

        public double[] promisedAlpha;

        // locks
        public ReaderWriterLock rwl;

        public double[] proposedAlpha;
        public double proposedDelta;
        public double proposedBeta;

        public double[] s; // additional packing constraints that show up during delta exploration; to insist flow remains below demand
        public double sumS;
        public double sumDee;

        public void ProposeAlpha(double alpha)
        {
            proposedBeta = 1;
            proposedDelta = 0;

            proposedAlpha = new double[promisedAlpha.Length];

            // convenience
            int m = yRef.orig_network.EdgeCount * (yRef.T + 1);
            double epsilon = S14o_GlobalState.epsilon;


            sumZ = 0;
            for (int i = 0; i < promisedAlpha.Length; i++)
            {
                proposedAlpha[i] = Math.Max(promisedAlpha[i], alpha);

                if (requestFlow[i] + priorFlow[i] >= .99 * proposedAlpha[i] * activeRequests[i].demand)
                {
                    yetToSatisfyReqs[i] = false;
                }
                else
                {
                    z[i] = Math.Exp(
                        -1.0 * Math.Log(m) * requestFlow[i] / (proposedAlpha[i] * activeRequests[i].demand - priorFlow[i]) / epsilon +
                        timesZhasBeenScaled * Math.Log(S14o_GlobalState.ScaleZsBy) +
                        Math.Log(S14o_GlobalState.InitZsTo)
                    );
                    sumZ += z[i];
                    yetToSatisfyReqs[i] = true;
                }
            }
        }


        public void ProposeDelta(double delta)
        {
            proposedDelta = delta;
            proposedBeta = 1;

            // over "active requests"

            sumDee = 0;  // \sum \delta -priorFlow[i]/ activeRequest[i].Demand
            double    d_2 = 0;  // \sum requestFlow[i] / activeRequest[i].Demand

            //
            // which requests can be increased to improve delta
            // 
            for (int i = 0; i < activeRequests.Length; i++)
                if (requestFlow[i] + priorFlow[i] >= .999 * activeRequests[i].demand)
                {
                    // cannot be filled any more; so not "active"
                    yetToSatisfyReqs[i] = false;
                }
                else
                {
                    sumDee += proposedDelta - (priorFlow[i] / activeRequests[i].demand);
                    d_2 += requestFlow[i] / activeRequests[i].demand;
                    yetToSatisfyReqs[i] = true;
                }

            // convenience
            int m = yRef.orig_network.EdgeCount * (yRef.T + 1);
            double epsilon = S14o_GlobalState.epsilon;

            covering_r = 
                Math.Exp(-1.0 * Math.Log(m) * d_2 / sumDee / epsilon);

            sumS = 0;
            s = new double[activeRequests.Length];
            for(int i=0; i < activeRequests.Length; i++)
                if (yetToSatisfyReqs[i])
                {
                    s[i] = 
                        Math.Exp(Math.Log(m) * requestFlow[i] / (activeRequests[i].demand - priorFlow[i]) / epsilon);

                    sumS += s[i];
                }
        }

        //
        //  intialize from currentLP "gold" state
        //
        public S14o_CurrentLPDirtyState(S14o_CurrentLPGoldState soCGS)
        {
            rwl = new ReaderWriterLock();
            yRef = soCGS.yRef;
            gsRef = soCGS.gsRef;

            currTimestep = soCGS.currTimestep;



            edgeFlows = new ConcurrentDictionary<int, double>();
            edgeLengths = new ConcurrentDictionary<int, double>();
            hardBetas = new ConcurrentDictionary<int, double>();

            foreach (int edgeIndex in soCGS.edgeFlows.Keys)
                edgeFlows[edgeIndex] = soCGS.edgeFlows[edgeIndex];

            sumY = 0;
            foreach (int edgeIndex in soCGS.edgeLengths.Keys)
            {
                int _t = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << (2*YoungsAlgorithm.NumBitsForSource))-1);
                Debug.Assert (_t >= currTimestep && _t <= yRef.T);
                
                sumY += 
                    soCGS.edgeLengths[edgeIndex] 
                    * yRef.edgeCapacities[e_k] 
                    * Math.Exp(-1.0 * (_t - currTimestep) / S14o_GlobalState.BetaExponentScale)
                    ;

                edgeLengths[edgeIndex] = soCGS.edgeLengths[edgeIndex];
                hardBetas[edgeIndex] = soCGS.hardBetas[edgeIndex];
            }

            activeRequests = soCGS.activeRequests; // do not edit; since these changes are temporary
            requestFlow = new double[activeRequests.Length];
            z = new double[activeRequests.Length];
            priorFlow = new double[activeRequests.Length];
            requestFlowByTime = new double[activeRequests.Length][];
            yetToSatisfyReqs = new bool[activeRequests.Length];
            promisedAlpha = new double[activeRequests.Length];

            for (int i = 0; i < activeRequests.Length; i++)
            {
                requestFlow[i] = soCGS.requestFlow[i];
                requestFlowByTime[i] = new double[yRef.T - currTimestep + 1];
                for(int t=0; t < yRef.T - currTimestep + 1; t++)
                    requestFlowByTime[i][t] = soCGS.requestFlowByTime[i][t];

                priorFlow[i] = soCGS.priorFlow[i];
                promisedAlpha[i] = soCGS.promisedAlpha[i];
            }

            timesZhasBeenScaled = soCGS.timesZhasBeenScaled;

            //
            // compute the path dictionary
            //
            req2shortestPathLength2time_and_index =
                new Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>();
            req2time2shortestPathLength_and_index =
                new ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>();

            for (int r = 0; r < activeRequests.Length; r++)
            {
                req2shortestPathLength2time_and_index[r] = new SortedDictionary<double, List<Tuple<int, int>>>();
                req2time2shortestPathLength_and_index[r] = new Dictionary<int, Tuple<double, int>>();

                AugRequest r_r = activeRequests[r];
                int r_k = r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest;

                for (int t = Math.Max(r_r.arrival, currTimestep); t <= r_r.deadline; t++)
                {
                    double minLength = double.MaxValue;
                    int minPathInd = -1, pathInd = 0;
                    foreach (Path p in yRef.pathDictionary[r_k])
                    {
                        double pathLength =
                            p.edgesList.Sum(e =>
                                edgeLengths[(t << 2*YoungsAlgorithm.NumBitsForSource) | (e.Source << YoungsAlgorithm.NumBitsForSource) | (e.Target)]
                                );

                        if (pathLength <= minLength)
                        {
                            minLength = pathLength;
                            minPathInd = pathInd;
                        }
                        pathInd++;
                    }

                    if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                        req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());

                    req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(t, minPathInd));
                    req2time2shortestPathLength_and_index[r][t] = new Tuple<double, int>(minLength, minPathInd);
                }
            }
        }

        public S14o_CurrentLPDirtyState(S14o_CurrentLPSilverState soCSS)
        {
            rwl = new ReaderWriterLock();
            yRef = soCSS.yRef;
            gsRef = soCSS.gsRef;

            currTimestep = soCSS.currTimestep;

            edgeFlows = new ConcurrentDictionary<int, double>();
            edgeLengths = new ConcurrentDictionary<int, double>();
            hardBetas = new ConcurrentDictionary<int, double>();

            foreach (int edgeIndex in soCSS.edgeFlows.Keys)
                edgeFlows[edgeIndex] = soCSS.edgeFlows[edgeIndex];

            sumY = 0;
            foreach (int edgeIndex in soCSS.edgeLengths.Keys)
            {
                int _t = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << (2 * YoungsAlgorithm.NumBitsForSource)) - 1);
                Debug.Assert(_t >= currTimestep && _t <= yRef.T);

                sumY +=
                    soCSS.edgeLengths[edgeIndex]
                    * yRef.edgeCapacities[e_k]
                    * Math.Exp(-1.0 * (_t - currTimestep) / S14o_GlobalState.BetaExponentScale)
                    ;

                edgeLengths[edgeIndex] = soCSS.edgeLengths[edgeIndex];
                hardBetas[edgeIndex] = soCSS.edgeLengths[edgeIndex];
            }

            activeRequests = soCSS.activeRequests; // do not edit; since these changes are temporary
            requestFlow = new double[activeRequests.Length];
            z = new double[activeRequests.Length];
            priorFlow = new double[activeRequests.Length];
            requestFlowByTime = new double[activeRequests.Length][];
            yetToSatisfyReqs = new bool[activeRequests.Length];
            promisedAlpha = new double[activeRequests.Length];

            for (int i = 0; i < activeRequests.Length; i++)
            {
                requestFlow[i] = soCSS.requestFlow[i];
                requestFlowByTime[i] = new double[yRef.T - currTimestep + 1];
                for (int t = 0; t < yRef.T - currTimestep + 1; t++)
                    requestFlowByTime[i][t] = soCSS.requestFlowByTime[i][t];

                priorFlow[i] = soCSS.priorFlow[i];
                promisedAlpha[i] = soCSS.promisedAlpha[i];
            }

            timesZhasBeenScaled = soCSS.timesZhasBeenScaled;

            //
            // compute the path dictionary
            //
            req2shortestPathLength2time_and_index =
                new Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>();
            req2time2shortestPathLength_and_index =
                new ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>();

            for (int r = 0; r < activeRequests.Length; r++)
            {
                req2shortestPathLength2time_and_index[r] = new SortedDictionary<double, List<Tuple<int, int>>>();
                req2time2shortestPathLength_and_index[r] = new Dictionary<int, Tuple<double, int>>();

                AugRequest r_r = activeRequests[r];
                int r_k = r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest;

                for (int t = Math.Max(r_r.arrival, currTimestep); t <= r_r.deadline; t++)
                {
                    double minLength = double.MaxValue;
                    int minPathInd = -1, pathInd = 0;
                    foreach (Path p in yRef.pathDictionary[r_k])
                    {
                        double pathLength =
                            p.edgesList.Sum(e =>
                                edgeLengths[(t << 2 * YoungsAlgorithm.NumBitsForSource) | (e.Source << YoungsAlgorithm.NumBitsForSource) | (e.Target)]
                                );

                        if (pathLength <= minLength)
                        {
                            minLength = pathLength;
                            minPathInd = pathInd;
                        }
                        pathInd++;
                    }

                    if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                        req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());

                    req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(t, minPathInd));
                    req2time2shortestPathLength_and_index[r][t] = new Tuple<double, int>(minLength, minPathInd);
                }
            }
        }
    }



    public class S14o_WorkerThread
    {
        static int ReadTimeoutMS = 1000, WriteTimeoutMS = 3000;
        public static int HowManyIters = 10000;
        Stopwatch sw;

        S14o_CurrentLPDirtyState sds;

        /*-------------------------------------------------------------------*/
        //
        // thread local variables; these are small enough; avoid concurrent access
        //
        public int threadId;
        public int success_iterations;

        int begin_t, T;  // this thread is responsible for times [begin_t, T]
        int[] req_ids;   // which requests

        public Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>
            req2shortestPathLength2time_and_index;

        public bool madeForwardProgress;


        public Dictionary<int, double> edgeCapacities;
        public List<Tuple<int, int>> orig_network_edges;
        public int nodecount;
        public Dictionary<int, HashSet<int>> edgeUsedByReqPaths;

        // this is potentially quite large; but using it to avoid concurrent dictionary
        public Dictionary<int, int[][]> pathDictionary; // key: src<<x | target, value: src, hop1, ..., target

        /*-------------------------------------------------------------------*/

        public void SetWork(int b, int e, int[] _req_ids)
        {
            begin_t = b;
            T = e;

            req_ids = _req_ids;
        }

        public S14o_WorkerThread
            (S14_YoungsAlgorithm y, int _tid)
        {
            threadId = _tid;

            // copy individually
            edgeCapacities = new Dictionary<int, double>();
            foreach (int e_key in y.edgeCapacities.Keys)
                edgeCapacities.Add(e_key, y.edgeCapacities[e_key]);

            orig_network_edges = new List<Tuple<int, int>>();
            foreach (Edge<int> e in y.orig_network.Edges)
                orig_network_edges.Add(new Tuple<int, int>(e.Source, e.Target));

            nodecount = y.orig_network.VertexCount;

            pathDictionary = new Dictionary<int, int[][]>();
            foreach (int p_key in y.pathDictionary.Keys)
            {
                List<Path> l_paths = y.pathDictionary[p_key];
                int[][] paths = new int[l_paths.Count][];

                pathDictionary.Add(p_key, paths);
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

            edgeUsedByReqPaths = new Dictionary<int, HashSet<int>>();
            foreach (int e_key in y.edgeUsedByReqPaths.Keys)
            {
                HashSet<int> h = new HashSet<int>();
                foreach (int pind in y.edgeUsedByReqPaths[e_key])
                    h.Add(pind);
                edgeUsedByReqPaths.Add(e_key, h);
            }
        }

        public void CopyMinPaths(S14o_CurrentLPDirtyState sds, bool singleThread = false)
        {
            sw = new Stopwatch();
            sw.Start();
            this.sds = sds;

            if (singleThread)
                req2shortestPathLength2time_and_index =
                    sds.req2shortestPathLength2time_and_index;
            else
            {
                req2shortestPathLength2time_and_index =
                    new Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>();

                foreach (int r in req_ids)
                {
                    SortedDictionary<double, List<Tuple<int, int>>>
                        sd_d_ltii = new SortedDictionary<double, List<Tuple<int, int>>>();

                    Request r_r = sds.activeRequests[r];

                    // pass 1: copy in
                    foreach (KeyValuePair<double, List<Tuple<int, int>>> kvp1 in
                            sds.req2shortestPathLength2time_and_index[r])
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
                    foreach (double d in sds.req2shortestPathLength2time_and_index[r].Keys)
                    {
                        sds.req2shortestPathLength2time_and_index[r][d].RemoveAll
                            (tii =>
                                tii.Item1 >= Math.Max(r_r.arrival, begin_t) &&
                                tii.Item1 <= Math.Min(r_r.deadline, T));

                        if (sds.req2shortestPathLength2time_and_index[r][d].Count == 0)
                            d_r.Add(d);
                    }
                    foreach (double d in d_r)
                        sds.req2shortestPathLength2time_and_index[r].Remove(d);

                }
            }
            sw.Stop();
            Console.WriteLine("Thread {0} copyminpaths {1}ms", threadId, sw.ElapsedMilliseconds);
        }

        public void RunOnPartitionForAlpha()
        {
            Console.WriteLine("AThread {0} start #reqs {1} time {2}-{3}",
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

            // convenience variables
            int m = orig_network_edges.Count * (sds.yRef.T + 1);
            double epsilon = S14o_GlobalState.epsilon;

            while (iterations < HowManyIters)
            {
                iterations++;

                int istar = 0, jstar = 0;
                double old_sumy = 0, old_sumz = 0, new_sumY = 0, old_r=0;
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

                    r_i = sds.activeRequests[i];

                    KeyValuePair<double, List<Tuple<int, int>>> kvp_d_li =
                        req2shortestPathLength2time_and_index[i].First();
                    pathLength = kvp_d_li.Key;
                    Tuple<int, int> f = kvp_d_li.Value[0];
                    minT = f.Item1;
                    int minPathInd = f.Item2;

                    try
                    {
                        sds.rwl.AcquireReaderLock(S14o_WorkerThread.ReadTimeoutMS);

                        old_sumy = sds.sumY;
                        old_sumz = sds.sumZ;

                        sds.rwl.ReleaseReaderLock();
                    }
                    catch (ApplicationException)
                    {
                        Console.WriteLine("Thread {0} findFeasible can't get reader lock", threadId);
                        continue;
                    }

                    if (((sds.proposedAlpha[i] * r_i.demand - sds.priorFlow[i]) * pathLength * old_sumz) <= (old_sumy * sds.proposedBeta * sds.z[i]))
                    //if (sds.proposedAlpha[i] * r_i.demand * (pathLength / old_sumy) * (old_sumz / sds.z[i]) <= sds.proposedBeta)
                    /*
                    if ((pathLength * (old_sumz + (old_total_flow_active? old_r: 0))) <= 
                        (old_sumy * ss.beta * ((ss.z[i] / (ss.alpha * r_i.demand)) + (old_total_flow_active? (old_r/ (ss.delta * ss.totalDemand)):0) )))
                     */
                    {
                        average_requestsSearchedPerIteration += (j - average_requestsSearchedPerIteration) / iterations;

                        shortestPath = pathDictionary[r_i.src << YoungsAlgorithm.NumBitsForSource | r_i.dest][minPathInd];

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
                    double minCapInShortestPath_prodHardBeta = double.MaxValue;
                    for (int i = 1; i < shortestPath.Length; i++)
                    {
                        int s = shortestPath[i - 1],
                            t = shortestPath[i];

                        int e_k = s << YoungsAlgorithm.NumBitsForSource | t;
                        int t_k = minT << (2*YoungsAlgorithm.NumBitsForSource) | e_k;

                        minCapInShortestPath_prodHardBeta = Math.Min(
                            minCapInShortestPath_prodHardBeta,
                            edgeCapacities[e_k] * sds.hardBetas[t_k]
                            );
                    }

                    Debug.Assert(minCapInShortestPath_prodHardBeta != double.MaxValue);
                    gamma = epsilon * 
                        Math.Min(sds.proposedAlpha[istar] * sds.activeRequests[istar].demand - sds.priorFlow[istar],
                        sds.proposedBeta * minCapInShortestPath_prodHardBeta);

                    Debug.Assert(gamma > 10 * double.MinValue);
                }

                // step 4b: allocate some flow to request istar
                double newFlow = gamma * epsilon / Math.Log(m);

                // prepare updates to y(e), flow, sumY, r
                Dictionary<int, double> newY =
                    new Dictionary<int, double>();

                new_sumY = old_sumy;
                for (int e_i = 1; e_i < shortestPath.Length; e_i++)
                {
                    int s = shortestPath[e_i - 1], t = shortestPath[e_i];
                    int e_k = s << YoungsAlgorithm.NumBitsForSource | t;
                    int key = minT << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

                    double old_y = sds.edgeLengths[key] * sds.hardBetas[key] * edgeCapacities[e_k];
                    double new_y = old_y *
                        Math.Exp(gamma / edgeCapacities[e_k] / sds.hardBetas[key] / sds.proposedBeta);

                    if (key == 33559560 || key == 30421007 || key == 7347205 || key == 20979727)
                    {
                        int i = 0;
                        i++;
                    }

                    if (new_y > .1 * double.MaxValue ||
                        new_sumY > .1 * double.MaxValue)
                    {
                        Console.WriteLine("WARN! edgeLength or sumY overflows {0} {1} {2}",
                            minT, sds.edgeLengths[key], new_sumY);
                    }
                    new_sumY = new_sumY + (new_y - old_y);

                    newY[key] = new_y;
                }

                // prepare updates to z, sumZ


                // check if request should be done
                bool request_istar_done =
                    sds.requestFlow[istar] + sds.priorFlow[istar] + newFlow >= sds.proposedAlpha[istar] * sds.activeRequests[istar].demand;

                newFlow = Math.Min(newFlow, 
                    sds.proposedAlpha[istar] * sds.activeRequests[istar].demand - sds.requestFlow[istar] - sds.priorFlow[istar]);

                // check that updates can be done
                bool go_on = false;
                try
                {
                    sds.rwl.AcquireWriterLock(S14o_WorkerThread.WriteTimeoutMS);

                    // update the shared ones first
                  if (((sds.proposedAlpha[istar] * r_i.demand - sds.priorFlow[istar]) * pathLength * sds.sumZ) <= 
                      (sds.proposedBeta * sds.sumY * sds.z[istar]))

                  // if ((sds.proposedAlpha[istar] * r_i.demand * (pathLength/sds.sumY) * (sds.sumZ/sds.z[istar])) <= sds.proposedBeta )
                    /*
                    if ((pathLength * (ss.sumZ + (ss.total_flow_active ? ss.covering_r : 0))) <=
    (ss.sumY * ss.beta * ((ss.z[istar] / (ss.alpha * r_i.demand)) + (ss.total_flow_active ? (ss.covering_r / (ss.delta * ss.totalDemand)) : 0))))
                     */
                    {
                        go_on = true;
                        sds.sumY = (sds.sumY - old_sumy) + new_sumY;


                        sds.totalDemand_satisfied += newFlow;
                      /*
                        if (sds.total_flow_active &&
                            sds.totalDemand_satisfied >= sds.delta * sds.totalDemand)
                        {
                            Console.WriteLine("| delta constraint met");
                            sds.total_flow_active = false;
                        }
                        if (sds.total_flow_active)
                        {
                            sds.covering_r *= Math.Pow(Math.E, -1 * gamma / (sds.delta * sds.totalDemand));
                            Debug.Asdsert(sds.covering_r > .0000001, String.Format("covering_r underflow {0}", sds.covering_r));
                        }
                       */

                        double old_z = sds.z[istar];
                        sds.z[istar] = old_z * Math.Exp(-1.0 * gamma / (sds.proposedAlpha[istar] * sds.activeRequests[istar].demand - sds.priorFlow[istar]));

                        if (sds.z[istar] < 100)
                        {
                            double _curr_sumZ = 0;
                            for (int i = 0; i < sds.yetToSatisfyReqs.Length; i++)
                            {
                                if (!sds.yetToSatisfyReqs[i]) continue;

                                sds.z[i] *= S14o_GlobalState.ScaleZsBy;
                                _curr_sumZ += sds.z[i];
                            }
                            sds.sumZ = _curr_sumZ;

                            /*
                            if (sds.total_flow_active)
                                sds.covering_r *= S14_online_SharedState.ScaleZsBy;
                            */
                            sds.timesZhasBeenScaled++;
                        }
                        else
                            sds.sumZ = (sds.sumZ + sds.z[istar]) - old_z;

                        if (request_istar_done)
                        {
                            sds.yetToSatisfyReqs[istar] = false;
                            sds.sumZ -= sds.z[istar];
                        }
                    }

                    // release lock
                    sds.rwl.ReleaseWriterLock();
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
                    sds.requestFlow[istar] += newFlow;
                    sds.requestFlowByTime[istar][minT - sds.currTimestep] += newFlow;
                    foreach (int key in newY.Keys)
                    {
                        int e_k = key & ((1 << (2*YoungsAlgorithm.NumBitsForSource))-1);
                        sds.edgeFlows[key] += newFlow;
                        sds.edgeLengths[key] = newY[key] / sds.hardBetas[key] / edgeCapacities[e_k];
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
                            if (sds.activeRequests[r].arrival > minT || sds.activeRequests[r].deadline < minT) continue;

                            AugRequest a_r = sds.activeRequests[r];

                            int minPathInd = sds.req2time2shortestPathLength_and_index[r][minT].Item2;
                            if (edgeUsedByReqPaths[e_k].Contains(a_r.id << YoungsAlgorithm.NumBitsForPaths | minPathInd))
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
                        Request r_r = sds.activeRequests[r];

                        double minLength = double.MaxValue;
                        int minPathInd = -1, pathInd = 0;
                        foreach (int[] p_array in pathDictionary[r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest])
                        {
                            double p_length = 0;
                            for (int e_i = 1; e_i < p_array.Length; e_i++)
                            {
                                int s = p_array[e_i - 1], t = p_array[e_i];
                                p_length += sds.edgeLengths[
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
                        double _x = sds.req2time2shortestPathLength_and_index[r][minT].Item1;

                        req2shortestPathLength2time_and_index[r][_x].RemoveAll(t => t.Item1 == minT);
                        if (req2shortestPathLength2time_and_index[r][_x].Count == 0)
                            req2shortestPathLength2time_and_index[r].Remove(_x);

                        if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                            req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());
                        req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(minT, minPathInd));

                        sds.req2time2shortestPathLength_and_index[r][minT] = new Tuple<double, int>(minLength, minPathInd);
                    }
                }
            }

            sw.Stop();
            Console.WriteLine("Thread {0} runOnPartition {1}ms {2}iter spec%{3}",
                threadId, sw.ElapsedMilliseconds, iterations, success_iterations * 1.0 / iterations);
        }
        public void RunOnPartitionForDelta()
        {
            Console.WriteLine("DThread {0} start #reqs {1} time {2}-{3}",
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

            // convenience variables
            int m = sds.yRef.orig_network.EdgeCount * (sds.yRef.T + 1);
            double epsilon = S14o_GlobalState.epsilon;

            while (iterations < HowManyIters)
            {
                iterations++;

                int istar = 0, jstar = 0;
                double old_sumy = 0, old_sums = 0, new_sumY = 0;

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

                    r_i = sds.activeRequests[i];

                    KeyValuePair<double, List<Tuple<int, int>>> kvp_d_li =
                        req2shortestPathLength2time_and_index[i].First();
                    pathLength = kvp_d_li.Key;
                    Tuple<int, int> f = kvp_d_li.Value[0];
                    minT = f.Item1;
                    int minPathInd = f.Item2;

                    try
                    {
                        sds.rwl.AcquireReaderLock(S14o_WorkerThread.ReadTimeoutMS);

                        old_sumy = sds.sumY;
                        old_sums = sds.sumS;

                        sds.rwl.ReleaseReaderLock();
                    }
                    catch (ApplicationException)
                    {
                        Console.WriteLine("Thread {0} findFeasible can't get reader lock", threadId);
                        continue;
                    }

                    if( ((pathLength/sds.proposedBeta) + (sds.s[i]/ (r_i.demand - sds.priorFlow[i]))) * r_i.demand * sds.sumDee <=
                        (old_sumy + old_sums))
                    //if ((sds.proposedAlpha[i] * r_i.demand * pathLength * old_sumz) <= (old_sumy * sds.proposedBeta * sds.z[i]))
                    //if (sds.proposedAlpha[i] * r_i.demand * (pathLength / old_sumy) * (old_sumz / sds.z[i]) <= sds.proposedBeta)
                    /*
                    if ((pathLength * (old_sumz + (old_total_flow_active? old_r: 0))) <= 
                        (old_sumy * ss.beta * ((ss.z[i] / (ss.alpha * r_i.demand)) + (old_total_flow_active? (old_r/ (ss.delta * ss.totalDemand)):0) )))
                     */
                    {
                        average_requestsSearchedPerIteration += (j - average_requestsSearchedPerIteration) / iterations;

                        shortestPath = pathDictionary[r_i.src << YoungsAlgorithm.NumBitsForSource | r_i.dest][minPathInd];

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
                    double minCapInShortestPath_prodHardBeta = double.MaxValue;
                    for (int i = 1; i < shortestPath.Length; i++)
                    {
                        int s = shortestPath[i - 1],
                            t = shortestPath[i];

                        int e_k = s << YoungsAlgorithm.NumBitsForSource | t;
                        int t_k = minT << (2 * YoungsAlgorithm.NumBitsForSource) | e_k;

                        minCapInShortestPath_prodHardBeta = Math.Min(
                            minCapInShortestPath_prodHardBeta,
                            edgeCapacities[e_k] * sds.hardBetas[t_k]
                            );
                    }

                    Debug.Assert(minCapInShortestPath_prodHardBeta != double.MaxValue);
                    gamma = epsilon *
                        Math.Min(
                        Math.Min(
                        sds.activeRequests[istar].demand - sds.priorFlow[istar],
                        sds.proposedBeta * minCapInShortestPath_prodHardBeta),
                        sds.activeRequests[istar].demand * sds.sumDee);

                    Debug.Assert(gamma > 10 * double.MinValue);
                }

                // step 4b: allocate some flow to request istar
                double newFlow = gamma * epsilon / Math.Log(m);

                // prepare updates to y(e), flow, sumY, r
                Dictionary<int, double> newY =
                    new Dictionary<int, double>();

                new_sumY = old_sumy;
                for (int e_i = 1; e_i < shortestPath.Length; e_i++)
                {
                    int s = shortestPath[e_i - 1], t = shortestPath[e_i];
                    int e_k = s << YoungsAlgorithm.NumBitsForSource | t;
                    int key = minT << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

                    double old_y = sds.edgeLengths[key] * sds.hardBetas[key] * edgeCapacities[e_k];
                    double new_y = old_y *
                        Math.Exp(gamma / (sds.proposedBeta * edgeCapacities[e_k] * sds.hardBetas[key]));
                    if (new_y > .1 * double.MaxValue ||
                        new_sumY > .1 * double.MaxValue)
                    {
                        Console.WriteLine("WARN! edgeLength or sumY overflows {0} {1} {2}",
                            minT, sds.edgeLengths[key], new_sumY);
                    }
                    new_sumY = new_sumY + (new_y - old_y);

                    newY[key] = new_y;
                }

                // check if request should be done
                bool request_istar_done =
                    sds.requestFlow[istar] + sds.priorFlow[istar] + newFlow >= sds.activeRequests[istar].demand;

                newFlow = Math.Min(newFlow,
                    sds.activeRequests[istar].demand - sds.requestFlow[istar] - sds.priorFlow[istar]);

                // check that updates can be done
                bool go_on = false;
                try
                {
                    sds.rwl.AcquireWriterLock(S14o_WorkerThread.WriteTimeoutMS);

                    // update the shared ones first
                    if ( ( (pathLength/sds.proposedBeta) + (sds.s[istar]/ (r_i.demand - sds.priorFlow[istar]))) * r_i.demand * sds.sumDee <=
                        (sds.sumY + sds.sumS))
//                    if ((sds.proposedAlpha[istar] * r_i.demand * pathLength * sds.sumZ) <= (sds.proposedBeta * sds.sumY * sds.z[istar]))

                    // if ((sds.proposedAlpha[istar] * r_i.demand * (pathLength/sds.sumY) * (sds.sumZ/sds.z[istar])) <= sds.proposedBeta )
                    /*
                    if ((pathLength * (ss.sumZ + (ss.total_flow_active ? ss.covering_r : 0))) <=
    (ss.sumY * ss.beta * ((ss.z[istar] / (ss.alpha * r_i.demand)) + (ss.total_flow_active ? (ss.covering_r / (ss.delta * ss.totalDemand)) : 0))))
                     */
                    {
                        go_on = true;
                        sds.sumY = (sds.sumY - old_sumy) + new_sumY;


                        sds.totalDemand_satisfied += newFlow;

                        sds.s[istar] *= Math.Exp(gamma / (sds.activeRequests[istar].demand - sds.priorFlow[istar]));

                        if (request_istar_done)
                        {
                            sds.yetToSatisfyReqs[istar] = false;
                        }
                    }

                    // release lock
                    sds.rwl.ReleaseWriterLock();
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
                    sds.requestFlow[istar] += newFlow;
                    sds.requestFlowByTime[istar][minT - sds.currTimestep] += newFlow;
                    foreach (int key in newY.Keys)
                    {
                        int e_k = key & ((1 << (2 * YoungsAlgorithm.NumBitsForSource)) - 1);                        
                        sds.edgeFlows[key] += newFlow;
                        sds.edgeLengths[key] = newY[key] / sds.hardBetas[key] / edgeCapacities[e_k];
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
                            if (sds.activeRequests[r].arrival > minT || sds.activeRequests[r].deadline < minT) continue;

                            AugRequest a_r = sds.activeRequests[r];

                            int minPathInd = sds.req2time2shortestPathLength_and_index[r][minT].Item2;
                            if (edgeUsedByReqPaths[e_k].Contains(a_r.id << YoungsAlgorithm.NumBitsForPaths | minPathInd))
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
                        Request r_r = sds.activeRequests[r];

                        double minLength = double.MaxValue;
                        int minPathInd = -1, pathInd = 0;
                        foreach (int[] p_array in pathDictionary[r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest])
                        {
                            double p_length = 0;
                            for (int e_i = 1; e_i < p_array.Length; e_i++)
                            {
                                int s = p_array[e_i - 1], t = p_array[e_i];
                                p_length += sds.edgeLengths[
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
                        double _x = sds.req2time2shortestPathLength_and_index[r][minT].Item1;

                        req2shortestPathLength2time_and_index[r][_x].RemoveAll(t => t.Item1 == minT);
                        if (req2shortestPathLength2time_and_index[r][_x].Count == 0)
                            req2shortestPathLength2time_and_index[r].Remove(_x);

                        if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                            req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());
                        req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(minT, minPathInd));

                        sds.req2time2shortestPathLength_and_index[r][minT] = new Tuple<double, int>(minLength, minPathInd);
                    }
                }
            }

            sw.Stop();
            Console.WriteLine("Thread {0} runOnPartition {1}ms {2}iter spec%{3}",
                threadId, sw.ElapsedMilliseconds, iterations, success_iterations * 1.0 / iterations);
        }
    }

    public class S14_online_stashSharedState
    {
        public double[] requestFlow;
        public double[] z;
        
        public Dictionary<int, double> edgeFlows;
        public Dictionary<int, double> edgeLengths;

        public int timesZhasBeenScaled;

        public double covering_r;

        public S14_online_stashSharedState(S14_online_SharedState soSS)
        {
            requestFlow = new double[soSS.requestFlow.Length];
            for (int i = 0; i < soSS.requestFlow.Length; i++)
                requestFlow[i] = soSS.requestFlow[i];

            z = new double[soSS.z.Length];
            for (int i = 0; i < soSS.z.Length; i++)
                z[i] = soSS.z[i];

            edgeFlows = new Dictionary<int, double>();
            foreach (int edgeIndex in soSS.edgeFlows.Keys)
                edgeFlows.Add(edgeIndex, soSS.edgeFlows[edgeIndex]);

            edgeLengths = new Dictionary<int, double>();
            foreach (int edgeIndex in soSS.edgeLengths.Keys)
                edgeLengths.Add(edgeIndex, edgeLengths[edgeIndex]);

            covering_r = soSS.covering_r;

            timesZhasBeenScaled = soSS.timesZhasBeenScaled;
        }
    }

    public class S14_online_SharedState
    {
        public const double InitZsTo = 1000000000000, ScaleZsBy = 1000000;
        public const int BetaExponentScale = 10;


        // read only
        public S14_YoungsAlgorithm yRef; // just a reference
        public Request[] requests;
        public double epsilon, alpha, beta, delta;
        public int m, T, E;
        public double totalDemand;
        public int currTimestep;
        public int numAwareRequests;


        //
        // partitioned state
        //
        // by request
        public double[] requestFlow;

        // by time
        public ConcurrentDictionary<int, double> edgeFlows;
        public ConcurrentDictionary<int, double> edgeLengths;

        // r-w conflicts
        public double[] z; // updates are mostly per-req, except "scaling"
        public bool[] yetToSatisfyReqs; // removing satisfied requests

        public double sumY, sumZ, covering_r; // truly shared
        public bool total_flow_active; // is this covering constraint still active
        public double totalDemand_satisfied;

        public int timesZhasBeenScaled;

        // try to copy and copy back
        // memorize the shortest paths for each request in each time graph
        public Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>
            req2shortestPathLength2time_and_index;
        public ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>
                req2time2shortestPathLength_and_index;


        // locks
        public ReaderWriterLock rwl;

        //
        //  used to initialize from some past stored state; needed if forward progress has been impeded
        //
        public S14_online_SharedState(S14_online_stashSharedState soSSS, S14_YoungsAlgorithm y, int _currTimestep)
        {
            rwl = new ReaderWriterLock();
            yRef = y;

            currTimestep = _currTimestep;

            // default values
            beta = 1;
            alpha = 1;
            delta = 0;

            T = y.T;
            m = y.orig_network.EdgeCount;
            sumY = 0;

            edgeFlows = new ConcurrentDictionary<int, double>();
            edgeLengths = new ConcurrentDictionary<int, double>();

            foreach (int edgeIndex in soSSS.edgeFlows.Keys)
                edgeFlows[edgeIndex] = soSSS.edgeFlows[edgeIndex];

            foreach (int edgeIndex in soSSS.edgeLengths.Keys)
            {
                int _t = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                if (_t >= currTimestep && _t <= T)
                    sumY += soSSS.edgeLengths[edgeIndex];

                edgeLengths[edgeIndex] = soSSS.edgeLengths[edgeIndex];
            }

            Debug.Assert
                (
                y.requests.Length == soSSS.requestFlow.Length &&
                y.requests.Length == soSSS.z.Length
                );

            requests = new Request[y.requests.Length];
            requestFlow = new double[soSSS.requestFlow.Length];
            z = new double[soSSS.z.Length];

            sumZ = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                requests[i] = y.requests[i];
                requestFlow[i] = soSSS.requestFlow[i];
                z[i] = soSSS.z[i];

                if (requestFlow[i] < .999 * requests[i].demand &&
                     requests[i].deadline >= currTimestep &&
                     requests[i].awareTime <= currTimestep)
                {
                    yetToSatisfyReqs[i] = true;
                    sumZ += z[i];
                }
                else
                    yetToSatisfyReqs[i] = true;
            }

            covering_r = soSSS.covering_r;
            timesZhasBeenScaled = soSSS.timesZhasBeenScaled;
        }

        // 
        // used to initialize state at timestep 0
        //
        public S14_online_SharedState(S14_YoungsAlgorithm y)
        {
            rwl = new ReaderWriterLock();
            yRef = y;

            currTimestep = 0; // not "timed" yet

            // default values
            beta = 1;
            alpha = 1;
            delta = 0;

            T = y.T;
            m = y.orig_network.EdgeCount;
            sumY = m * (T + 1);

            edgeFlows = new ConcurrentDictionary<int, double>();
            edgeLengths = new ConcurrentDictionary<int, double>();

            foreach (Edge<int> e in y.orig_network.Edges)
            {
                int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
                for (int t = 0; t <= T; t++)
                {
                    int t_k = t << 2 * YoungsAlgorithm.NumBitsForSource | e_k;
                    edgeFlows[t_k] = 0;

                    // these have both 1/c_e and 1/\beta; the hard-coded time-dependent beta embedded in them
                    edgeLengths[t_k] = 1 / (y.edgeCapacities[e_k] * Math.Exp(-1.0 * t/ S14_online_SharedState.BetaExponentScale));
                }
            }

            requests = new Request[y.requests.Length];
            z = new double[requests.Length];
            requestFlow = new double[requests.Length];
            yetToSatisfyReqs = new bool[requests.Length];

            totalDemand_satisfied = 0;

            for (int i = 0; i < requests.Length; i++)
            {
                requests[i] = y.requests[i];
                requestFlow[i] = 0;
                yetToSatisfyReqs[i] = false;
                z[i] = 0; // will be set when flow is aware
            }
            covering_r = InitZsTo;
            sumZ = 0; // no flows active now
            totalDemand = 0;

            //
            // make flows that arrive at timestep 0 active
            //
            total_flow_active = true;
            for (int i = 0; i < requests.Length; i++)
            {
                Debug.Assert(requests[i].awareTime >= 0);
                if (requests[i].awareTime == 0)
                {
                    z[i] = InitZsTo;
                    sumZ += z[i];
                    yetToSatisfyReqs[i] = true;
                    totalDemand += requests[i].demand;
                }
                else
                {
                    numAwareRequests = i; // requests are sorted by awareTime
                    break;
                }
            }

            epsilon = y.epsilon;
        }

        // give it a new goal and a new timestep
        public void Step(int _currTimestep, double _alpha, double _delta, double _beta)
        {
            Debug.Assert(_currTimestep > 0 && _currTimestep == currTimestep + 1);

            currTimestep = _currTimestep;
            alpha = _alpha;
            delta = _delta;
            beta = _beta;

            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i].awareTime < currTimestep)
                {
                    if (requests[i].deadline >= currTimestep && 
                        requestFlow[i] < .999 * requests[i].demand)
                        yetToSatisfyReqs[i] = true;

                    continue;
                }
                else if (requests[i].awareTime == currTimestep)
                {
                    yetToSatisfyReqs[i] = true;

                    totalDemand += requests[i].demand;

                    // check for numerical overflow
                    z[i] = InitZsTo * timesZhasBeenScaled;
                    sumZ += z[i];
                }
                else
                {
                    numAwareRequests = i;
                    break;
                }
            }
            total_flow_active = true; // just set this on again

            //
            // flows whose deadlines are past; if they have they not been removed already, remove them
            //
            for (int i = 0; i < requests.Length; i++)
                if (requests[i].awareTime > currTimestep) break;
                else
                {
                    if (requests[i].deadline == currTimestep-1 && // just past their deadline
                        requestFlow[i] < .999 * requests[i].demand) // not removed yet
                    {
                        sumZ -= z[i];
                        yetToSatisfyReqs[i] = false; // just to be doubly sure
                    }
                }

            //
            // scale the edge lengths; edit sumY
            //
            double sumY_toDeduct =0;

            foreach (int edgeIndex in edgeLengths.Keys)
            {
                int timestamp = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << 2*YoungsAlgorithm.NumBitsForSource) -1);

                if (timestamp < currTimestep - 1)
                {
                    continue;
                }
                else if (timestamp == currTimestep - 1)
                {
                    double old_beta =
                        Math.Exp(-1.0 * (timestamp - currTimestep + 1) / 10);
                    sumY_toDeduct += edgeLengths[edgeIndex] * yRef.edgeCapacities[e_k] * old_beta;
                    continue;
                }
                else
                {
                    double old_beta =
                        Math.Exp(-1.0 * (timestamp - currTimestep + 1) / 10);
                    double new_beta =
                        Math.Exp(-1.0 * (timestamp - currTimestep) / 10);

                    double newY = Math.Pow(edgeLengths[edgeIndex] * old_beta * yRef.edgeCapacities[e_k], old_beta / new_beta)
                        / new_beta / yRef.edgeCapacities[e_k];

                    sumY_toDeduct += ((edgeLengths[edgeIndex]*old_beta) - (newY*new_beta)) * yRef.edgeCapacities[e_k];

                    edgeLengths[edgeIndex] = newY;
                }
            }

            sumY -= sumY_toDeduct;

            //
            // seed path lengths afresh
            //
            req2shortestPathLength2time_and_index =
                new Dictionary<int, SortedDictionary<double, List<Tuple<int, int>>>>();
            req2time2shortestPathLength_and_index =
                new ConcurrentDictionary<int, Dictionary<int, Tuple<double, int>>>();


            for (int r = 0; r < numAwareRequests; r++)
            {
                if (!yetToSatisfyReqs[r]) continue;

                req2shortestPathLength2time_and_index[r] = new SortedDictionary<double, List<Tuple<int, int>>>();
                req2time2shortestPathLength_and_index[r] = new Dictionary<int, Tuple<double, int>>();

                Request r_r = requests[r];
                int r_k = r_r.src << YoungsAlgorithm.NumBitsForSource | r_r.dest;

                double minLength = double.MaxValue;
                int minPathInd = -1, pathInd = 0;
                foreach (Path p in yRef.pathDictionary[r_k])
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
                for (int t = Math.Max(requests[r].arrival, currTimestep); t <= requests[r].deadline; t++)
                {
                    if (!req2shortestPathLength2time_and_index[r].ContainsKey(minLength))
                        req2shortestPathLength2time_and_index[r].Add(minLength, new List<Tuple<int, int>>());

                    req2shortestPathLength2time_and_index[r][minLength].Add(new Tuple<int, int>(t, minPathInd));

                    req2time2shortestPathLength_and_index[r][t] = new Tuple<double, int>(minLength, minPathInd);
                }
            }
        }
    }

    public class FeasibilityResult
    {
        public MeanStdNum 
            msn_beta, // one sample per edge, time
            msn_flow_fraction // one sample per flow
            ;
        public double 
            delta_achieved,             // delta
            fractionTotalDemandMet,     // convenience
            minFractionMet              // alpha
            ;
        public double[] fractionDemandMet;

        public bool packingConstraintsFeasible;

        public bool isRatioAViolationNextBeta(double ratio, int timestamp, int currTimestep)
        {
            if (ratio < 1) return false;

            if (timestamp == currTimestep)
                return ratio > 1.001;
            else
            {
                double
                    beta = Math.Exp(-1.0 * (timestamp - currTimestep) / S14o_GlobalState.BetaExponentScale),
                    beta_next = Math.Exp( -1.0 * (timestamp - currTimestep -1)/ S14o_GlobalState.BetaExponentScale);

                return ratio < (beta_next / beta);
            }
        }

        public bool isRatioAViolationFullCapacity(double ratio, int timestamp, int currTimestep)
        {
            if (ratio < 1) return false;

            double beta = Math.Exp(-1.0 * (timestamp - currTimestep) / S14o_GlobalState.BetaExponentScale);
            return ratio > 1.001 / beta;
        }

        public FeasibilityResult(S14o_CurrentLPDirtyState soCDS)
        {
            minFractionMet = double.MaxValue;
            packingConstraintsFeasible = true; // checks if _any_ packing constraints are overblown
            fractionDemandMet = new double[soCDS.activeRequests.Length];

            msn_beta = new MeanStdNum();
            foreach (int edgeIndex in soCDS.edgeFlows.Keys)
            {
                int timestamp = edgeIndex >> (2 * YoungsAlgorithm.NumBitsForSource);
                int e_k = edgeIndex & ((1 << 2 * YoungsAlgorithm.NumBitsForSource) - 1);

                double f = soCDS.edgeFlows[edgeIndex];
                double beta = Math.Exp(-1.0 * (timestamp - soCDS.currTimestep) / S14o_GlobalState.BetaExponentScale);
                double cap = soCDS.yRef.edgeCapacities[e_k];

                double ratio = f / (beta * cap);
                msn_beta.AddSample(ratio);

                if(isRatioAViolationFullCapacity(ratio, timestamp, soCDS.currTimestep))
                {
//                if (ratio > 1.001)
//                {
                    Console.WriteLine("|| xx link usage > cap e_k {0} e {1}: f{2} beta {3} cap {4}", e_k, edgeIndex, f, beta, cap);
                    packingConstraintsFeasible = false;
                }
            }

            double sum_flow_fractions = 0, totalDemandMet = 0, totalDemand = 0;

            msn_flow_fraction = new MeanStdNum();
            for (int i = 0; i < soCDS.activeRequests.Length; i++)
            {
                double flow_fraction_met = (soCDS.requestFlow[i] + soCDS.priorFlow[i]) / soCDS.activeRequests[i].demand;
                msn_flow_fraction.AddSample(flow_fraction_met);

                sum_flow_fractions += flow_fraction_met;
                totalDemand += soCDS.activeRequests[i].demand;
                totalDemandMet += (soCDS.requestFlow[i] + soCDS.priorFlow[i]);

                if (flow_fraction_met > 1.001)
                {
                    Console.WriteLine("|| xx flow allocation > demand for flow: {0}", soCDS.activeRequests[i]);
                    packingConstraintsFeasible = false;
                }

                minFractionMet = Math.Min(minFractionMet, flow_fraction_met);
                fractionDemandMet[i] = flow_fraction_met;
            }
            delta_achieved = sum_flow_fractions / soCDS.activeRequests.Length;
            fractionTotalDemandMet = totalDemandMet / totalDemand;
        }

        public override string ToString()
        {
            return 
                String.Format("-----\n Betas: {0}\n Flow fractions: {1}\n Delta Achvd. {2} totDemandMet {3}% Alpha Achvd. {4}",
                msn_beta.GetDetailedString(), 
                msn_flow_fraction.GetDetailedString(), 
                delta_achieved.ToString("F4"), 
                (fractionTotalDemandMet*100).ToString("F4"),
                minFractionMet.ToString("F4"));
        }
    }


    public class S14o_Youngs
    {
        S14_YoungsAlgorithm yRef;
        int numThreads;
        Randomness randomness;

        public S14o_Youngs(S14_YoungsAlgorithm y, int _numThreads)
        {
            yRef = y;
            numThreads = _numThreads;
            randomness = new Randomness(300);

            Console.WriteLine("jfyi; BetaExponentScale = {0}", S14o_GlobalState.BetaExponentScale);
        }

        public void Run()
        {
            S14o_GlobalState gs = new S14o_GlobalState(yRef);
            S14o_CurrentLPGoldState currentGoldState = new S14o_CurrentLPGoldState(yRef, gs); // init for t=0
 

            for (int t = 0; t <= yRef.T; t++)
            {
                Stopwatch 
                    fullTau = new Stopwatch(), 
                    firstAlphaRun = new Stopwatch(), 
                    finAlpha = new Stopwatch(), 
                    firstDeltaRun= new Stopwatch(), 
                    finDelta=new Stopwatch();
                bool firstAlpha = true, firstDelta = true;

                fullTau.Start();
                Console.WriteLine("|-");
                Console.WriteLine("----------------- tau ={0}--------------------", t);
                Console.WriteLine("");

                S14o_CurrentLPDirtyState bestDirtyState = null;
                FeasibilityResult fr, bestAlpha_fr = null, bestDelta_fr =null;
                //
                // search for best possible \alpha, \delta, \beta for t=0
                // several calls to this:
                //

                finAlpha.Start();
                // search for best alpha
                double alpha_max_feasible = 0, alpha_min_infeasible = 1;
                double proposed_alpha = 1;
                do
                {
                    Console.WriteLine("\n|| ------------ propose alpha ={0}---------\n", proposed_alpha);

                    if(firstAlpha)
                        firstAlphaRun.Start();

                    S14o_CurrentLPDirtyState attemptDirtyState = new S14o_CurrentLPDirtyState(currentGoldState);
                    attemptDirtyState.ProposeAlpha(proposed_alpha);
                    fr = CheckFeasibility(attemptDirtyState, YoungsSearchingFor.alpha);

                    if (firstAlpha)
                    {
                        firstAlphaRun.Stop();
                        firstAlpha = false;

                        Console.WriteLine("timeElapsed firstAlpha {0}", firstAlphaRun.ElapsedMilliseconds);
                    }


                    Console.WriteLine("------> Result: {0}", fr);

                    if (fr.packingConstraintsFeasible && fr.minFractionMet >= .999 * proposed_alpha)
                    {
                        alpha_max_feasible = proposed_alpha;
                        bestDirtyState = attemptDirtyState;
                        bestAlpha_fr = fr;
                    }
                    else
                    {
                        alpha_min_infeasible = proposed_alpha;
                    }

                    if (alpha_min_infeasible > .01)
                    {
                        proposed_alpha =
                            Math.Max(alpha_max_feasible + .01, (alpha_max_feasible + alpha_min_infeasible) / 2.0);

                        proposed_alpha = Math.Floor(proposed_alpha * 100) / 100.0;
                    }
                    else
                    {
                        // bottom fishing here; \alpha = .01 is infeasible
                        if(bestDirtyState == null )
                            proposed_alpha = (alpha_max_feasible + alpha_min_infeasible) / 2.0;
                    }
                }
                while(alpha_max_feasible < alpha_min_infeasible && 
                      proposed_alpha < alpha_min_infeasible &&
                      proposed_alpha > alpha_max_feasible);

                finAlpha.Stop();

                Console.WriteLine("timeElapsed alpha {0}", finAlpha.ElapsedMilliseconds);

                double best_alpha = alpha_max_feasible;
                Console.WriteLine("|-| best alpha = {0}", best_alpha);

                //
                // record the alpha into the bestDirtyState
                //
                for (int i = 0; i < bestDirtyState.activeRequests.Length; i++)
                    bestDirtyState.promisedAlpha[i] =
                        Math.Max(bestDirtyState.promisedAlpha[i], best_alpha);

                // prepare silver state for exploration on delta
                S14o_CurrentLPSilverState sSs = new S14o_CurrentLPSilverState(bestDirtyState);
                S14o_CurrentLPDirtyState bestDirtyStateDelta = null;

                double deltaAcheivedAfterAlpha=0;
                for(int i=0; i < sSs.activeRequests.Length; i++)
                    deltaAcheivedAfterAlpha+= (sSs.requestFlow[i] + sSs.priorFlow[i])/ sSs.activeRequests[i].demand;
                deltaAcheivedAfterAlpha /= sSs.activeRequests.Length;

                finDelta.Start();
                // explore delta
                double delta_max_feasible = deltaAcheivedAfterAlpha, delta_min_infeasible = 1;
                double proposed_delta = 1;
                do
                {
                    Console.WriteLine("\n|| ------------ propose delta ={0}---------\n", proposed_delta);

                    if (firstDelta)
                        firstDeltaRun.Start();

                    S14o_CurrentLPDirtyState attemptDirtyState =
                        new S14o_CurrentLPDirtyState(sSs);
                    attemptDirtyState.ProposeDelta(proposed_delta);
                    fr = CheckFeasibility(attemptDirtyState, YoungsSearchingFor.delta);

                    if (firstDelta)
                    {
                        firstDelta = false;
                        firstDeltaRun.Stop();
                        Console.WriteLine("timeElapsed firstDelta {0}", firstDeltaRun.ElapsedMilliseconds);
                    }
                    Console.WriteLine("------> Result: {0}", fr);

                    if (fr.packingConstraintsFeasible && fr.delta_achieved >= proposed_delta)
                    {
                        delta_max_feasible = Math.Max(proposed_delta, fr.delta_achieved);
                        bestDirtyStateDelta = attemptDirtyState;
                        bestDelta_fr = fr;
                    }
                    else
                    {
                        delta_min_infeasible = proposed_delta;
                    }

                    proposed_delta =
                        Math.Max(delta_max_feasible + .01, (delta_max_feasible + delta_min_infeasible) / 2.0);

                    proposed_delta = Math.Floor(proposed_delta * 100) / 100.0;

                } while (delta_max_feasible < delta_min_infeasible &&
                    proposed_delta > delta_max_feasible &&
                    proposed_delta < delta_min_infeasible);
                
                finDelta.Stop();
                Console.WriteLine("timeElapsed delta {0}", finDelta.ElapsedMilliseconds);
                //
                // now, roll that into the new gold state
                //
                if (bestDirtyStateDelta != null)
                {
                    for (int i = 0; i < bestDirtyStateDelta.activeRequests.Length; i++)
                        bestDirtyStateDelta.promisedAlpha[i] =
                            Math.Max(bestDirtyStateDelta.promisedAlpha[i], bestDelta_fr.fractionDemandMet[i]);
                    Console.WriteLine("||| useful progress w. delta {0} --> {1}", deltaAcheivedAfterAlpha, bestDelta_fr.delta_achieved);
                }

                if (bestDirtyStateDelta == null)
                    bestDirtyStateDelta = bestDirtyState;

                currentGoldState = new S14o_CurrentLPGoldState(bestDirtyStateDelta);
                fullTau.Stop();
                Console.WriteLine("timeElapsed Tau {0} {1} ms", t, fullTau.ElapsedMilliseconds);
            }

            gs.PrintSummary(yRef);
        }
        public void NewRun()
        {
            S14o_GlobalState gs = new S14o_GlobalState(yRef);
            S14o_CurrentLPGoldState currentGoldState = new S14o_CurrentLPGoldState(yRef, gs); // init for t=0


            for (int t = 0; t <= yRef.T; t++)
            {
                Stopwatch
                    fullTau = new Stopwatch(),
                    firstAlphaRun = new Stopwatch(),
                    finAlpha = new Stopwatch(),
                    firstDeltaRun = new Stopwatch(),
                    finDelta = new Stopwatch();
                bool firstAlpha = true, firstDelta = true;

                fullTau.Start();
                Console.WriteLine("|-");
                Console.WriteLine("----------------- tau ={0}--------------------", t);
                Console.WriteLine("");

                S14o_CurrentLPDirtyState bestDirtyState = null;
                FeasibilityResult fr, bestAlpha_fr = null, bestDelta_fr = null;
                //
                // search for best possible \alpha, \delta, \beta for t=0
                // several calls to this:
                //

                finAlpha.Start();
                // search for best alpha
                double alpha_max_feasible = 0, alpha_min_infeasible = 1;
                double proposed_alpha = 1;
                do
                {
                    Console.WriteLine("\n|| ------------ propose alpha ={0}---------\n", proposed_alpha);

                    if (firstAlpha)
                        firstAlphaRun.Start();

                    S14o_CurrentLPDirtyState attemptDirtyState = new S14o_CurrentLPDirtyState(currentGoldState);
                    attemptDirtyState.ProposeAlpha(proposed_alpha);
                    fr = CheckFeasibility(attemptDirtyState, YoungsSearchingFor.alpha);

                    if (firstAlpha)
                    {
                        firstAlphaRun.Stop();
                        firstAlpha = false;

                        Console.WriteLine("timeElapsed firstAlpha {0}", firstAlphaRun.ElapsedMilliseconds);
                    }


                    Console.WriteLine("------> Result: {0}", fr);

                    if (fr.packingConstraintsFeasible && fr.minFractionMet >= .999 * proposed_alpha)
                    {
                        alpha_max_feasible = proposed_alpha;
                        bestDirtyState = attemptDirtyState;
                        bestAlpha_fr = fr;
                    }
                    else
                    {
                        alpha_min_infeasible = proposed_alpha;
                    }

                    if (alpha_min_infeasible > .01)
                    {
                        proposed_alpha =
                            Math.Max(alpha_max_feasible + .01, (alpha_max_feasible + alpha_min_infeasible) / 2.0);

                        proposed_alpha = Math.Floor(proposed_alpha * 100) / 100.0;
                    }
                    else
                    {
                        // bottom fishing here; \alpha = .01 is infeasible
                        if (bestDirtyState == null)
                            proposed_alpha = (alpha_max_feasible + alpha_min_infeasible) / 2.0;
                    }
                }
                while (alpha_max_feasible < alpha_min_infeasible &&
                      proposed_alpha < alpha_min_infeasible &&
                      proposed_alpha > alpha_max_feasible);

                finAlpha.Stop();

                Console.WriteLine("timeElapsed alpha {0}", finAlpha.ElapsedMilliseconds);

                double best_alpha = alpha_max_feasible;
                Console.WriteLine("|-| best alpha = {0}", best_alpha);

                //
                // record the alpha into the bestDirtyState
                //
                for (int i = 0; i < bestDirtyState.activeRequests.Length; i++)
                    bestDirtyState.promisedAlpha[i] =
                        Math.Max(bestDirtyState.promisedAlpha[i], best_alpha);

                // prepare silver state for exploration on delta
                S14o_CurrentLPSilverState sSs = new S14o_CurrentLPSilverState(bestDirtyState);
                S14o_CurrentLPDirtyState bestDirtyStateDelta = null;

                double deltaAcheivedAfterAlpha = 0;
                for (int i = 0; i < sSs.activeRequests.Length; i++)
                    deltaAcheivedAfterAlpha += (sSs.requestFlow[i] + sSs.priorFlow[i]) / sSs.activeRequests[i].demand;
                deltaAcheivedAfterAlpha /= sSs.activeRequests.Length;

                finDelta.Start();
                // explore delta
                double delta_max_feasible = deltaAcheivedAfterAlpha, delta_min_infeasible = 1;
                double proposed_delta = 1;
                do
                {
                    Console.WriteLine("\n|| ------------ propose delta ={0}---------\n", proposed_delta);

                    if (firstDelta)
                        firstDeltaRun.Start();

                    S14o_CurrentLPDirtyState attemptDirtyState =
                        new S14o_CurrentLPDirtyState(sSs);
                    attemptDirtyState.ProposeDelta(proposed_delta);
                    fr = CheckFeasibility(attemptDirtyState, YoungsSearchingFor.delta);

                    if (firstDelta)
                    {
                        firstDelta = false;
                        firstDeltaRun.Stop();
                        Console.WriteLine("timeElapsed firstDelta {0}", firstDeltaRun.ElapsedMilliseconds);
                    }
                    Console.WriteLine("------> Result: {0}", fr);

                    if (fr.packingConstraintsFeasible && fr.delta_achieved >= proposed_delta)
                    {
                        delta_max_feasible = Math.Max(proposed_delta, fr.delta_achieved);
                        bestDirtyStateDelta = attemptDirtyState;
                        bestDelta_fr = fr;
                    }
                    else
                    {
                        delta_min_infeasible = proposed_delta;
                    }

                    proposed_delta =
                        Math.Max(delta_max_feasible + .01, (delta_max_feasible + delta_min_infeasible) / 2.0);

                    proposed_delta = Math.Floor(proposed_delta * 100) / 100.0;

                } while (delta_max_feasible < delta_min_infeasible &&
                    proposed_delta > delta_max_feasible &&
                    proposed_delta < delta_min_infeasible);

                finDelta.Stop();
                Console.WriteLine("timeElapsed delta {0}", finDelta.ElapsedMilliseconds);




                //
                // now, roll that into the new gold state
                //
                if (bestDirtyStateDelta != null)
                {
                    for (int i = 0; i < bestDirtyStateDelta.activeRequests.Length; i++)
                    {
                        Debug.Assert(.9999 * bestDirtyStateDelta.promisedAlpha[i] <= bestDelta_fr.fractionDemandMet[i]);

                        bestDirtyStateDelta.promisedAlpha[i] =
                            Math.Max(bestDirtyStateDelta.promisedAlpha[i], 
                            bestDelta_fr.fractionDemandMet[i]);
                    }
                    Console.WriteLine("||| useful progress w. delta {0} --> {1}", deltaAcheivedAfterAlpha, bestDelta_fr.delta_achieved);
                }

                if (bestDirtyStateDelta != null)
                    bestDirtyState = bestDirtyStateDelta; // can only be better


                // greedily pack all the remaining bandwidth
                S14o_CurrentLPDirtyState afterGreedy = GreedyPackEverything(bestDirtyState);

                currentGoldState = new S14o_CurrentLPGoldState(afterGreedy);
                fullTau.Stop();
                Console.WriteLine("timeElapsed Tau {0} {1} ms", t, fullTau.ElapsedMilliseconds);
            }

            gs.PrintSummary(yRef);
        }


        private S14o_CurrentLPDirtyState GreedyPackEverything(S14o_CurrentLPDirtyState bestDirtyState)
        {
            SolverContext context = new SolverContext();
            Model model = context.CreateModel();

            // 
            // inst. alpha 
            //
            Decision alpha = new Decision(Domain.RealNonnegative, "alpha");
            model.AddDecision(alpha);
            model.AddConstraint(null, 0 <= alpha <= 1);

            Dictionary<Tuple<int, int>, Decision> f = new Dictionary<Tuple<int, int>, Decision>();
            Dictionary<int, Term> edgeFlow = new Dictionary<int, Term>();
            Term allFlow = 0;
           
            for (int r = 0; r < bestDirtyState.activeRequests.Length; r++)
            {
                if (bestDirtyState.requestFlow[r] + bestDirtyState.priorFlow[r] >
                    .9999 * bestDirtyState.activeRequests[r].demand)
                    continue;

                List<Path> pathsList = 
                    bestDirtyState.yRef.pathDictionary
                    [bestDirtyState.activeRequests[r].src << YoungsAlgorithm.NumBitsForSource | 
                     bestDirtyState.activeRequests[r].dest];

                Term additionalFlow = 0;

                for (int p = 0; p < pathsList.Count; p++)
                {
                    Decision f_p_r = new Decision(Domain.RealNonnegative, "f_" + p + "_" + r); ;
                    f[new Tuple<int, int>(p, r)] = f_p_r;
                    model.AddDecision(f_p_r);

                    foreach (Edge<int> e in pathsList[p].edgesList)
                    {
                        int e_k = (e.Source << YoungsAlgorithm.NumBitsForSource) | e.Target;
                        if (!edgeFlow.ContainsKey(e_k))
                        {
                            edgeFlow.Add(e_k, 0);
                        }

                        edgeFlow[e_k] += f_p_r;
                    }

                    additionalFlow += f_p_r;
                    allFlow += f_p_r;
                }
                //
                // can allocate up to residual demand
                //
                model.AddConstraint(null,
                    0 <= additionalFlow <=
                    bestDirtyState.activeRequests[r].demand -
                    bestDirtyState.requestFlow[r] -
                    bestDirtyState.priorFlow[r]);

                // min alpha
                model.AddConstraint(null,
                    alpha <=
                    (additionalFlow + bestDirtyState.requestFlow[r] + bestDirtyState.priorFlow[r]) /
                    bestDirtyState.activeRequests[r].demand);
            }

            // can only allocate residual capacity
            foreach (int e_k in edgeFlow.Keys)
            {
                int key =
                    (bestDirtyState.currTimestep << (2 * YoungsAlgorithm.NumBitsForSource)) |
                    e_k;

                model.AddConstraint(null,
                    edgeFlow[e_k] + bestDirtyState.edgeFlows[key] <=
                    bestDirtyState.yRef.edgeCapacities[e_k]
                    );
            }

            // this is to ensure feasibility; is an instantaneous alpha, has nothing to do with overall flow fractions
            model.AddGoal(null, GoalKind.Maximize, allFlow + alpha);


            Solution solution = context.Solve(new SimplexDirective());


            //
            // now have to roll this into the dirty state
            //
            Dictionary<int, double> flowOnEdge = new Dictionary<int, double>();
            foreach(Tuple<int, int> tii in f.Keys)
            {
                int r = tii.Item2;
                int p = tii.Item1;

                double x = f[tii].ToDouble();
                Debug.Assert(x >= 0);

                bestDirtyState.requestFlow[r] += x;
                bestDirtyState.requestFlowByTime[r][0] += x;

                List<Path> pathsList =
                    bestDirtyState.yRef.pathDictionary
                    [bestDirtyState.activeRequests[r].src << YoungsAlgorithm.NumBitsForSource |
                    bestDirtyState.activeRequests[r].dest];

                foreach (Edge<int> e in pathsList[p].edgesList)
                {
                    int e_k = (e.Source << YoungsAlgorithm.NumBitsForSource) | e.Target;
                    if (!flowOnEdge.ContainsKey(e_k))
                    {
                        flowOnEdge.Add(e_k, 0);
                    }

                    flowOnEdge[e_k] += x;
                }
            }

            //
            // change promised alphas
            //
            for (int r = 0; r < bestDirtyState.activeRequests.Length; r++)
            {
                double newAlpha =
                    (bestDirtyState.requestFlow[r] + bestDirtyState.priorFlow[r]) /
                    bestDirtyState.activeRequests[r].demand;

                Debug.Assert(newAlpha >= .9999 * bestDirtyState.promisedAlpha[r]);

                bestDirtyState.promisedAlpha[r] =
                    Math.Max(bestDirtyState.promisedAlpha[r], newAlpha);
            }

            int m = bestDirtyState.yRef.orig_network.EdgeCount * (bestDirtyState.yRef.T + 1);
            Debug.Assert(flowOnEdge.Count == edgeFlow.Count);
            foreach(int e_k in flowOnEdge.Keys)
            {
                int key = 
                    (bestDirtyState.currTimestep << (2*YoungsAlgorithm.NumBitsForSource)) | 
                    e_k;

                bestDirtyState.edgeFlows[key] += flowOnEdge[e_k];


                double old_y = 
                    bestDirtyState.edgeLengths[key] * 
                    bestDirtyState.hardBetas[key] *
                    bestDirtyState.yRef.edgeCapacities[e_k];

                double new_y = 
                    old_y * 
                    Math.Exp(
                    (flowOnEdge[e_k] * Math.Log(m)) /
                    S14o_GlobalState.epsilon /
                    bestDirtyState.yRef.edgeCapacities[e_k] / 
                    bestDirtyState.hardBetas[key] / 
                    bestDirtyState.proposedBeta);
                
                bestDirtyState.edgeLengths[key] = 
                    new_y / 
                    bestDirtyState.hardBetas[key] / 
                    bestDirtyState.yRef.edgeCapacities[e_k];
            }

            return bestDirtyState;
        }

        public List<Tuple<int, int>> SplitTimeRangeForThreads(AugRequest[] requests)
        {
            List<double>
    arrivals = new List<double>(),
    deadlines = new List<double>();
            for (int i = 0; i < requests.Length; i++)
            {
                arrivals.Add(requests[i].arrival);
                deadlines.Add(requests[i].deadline);
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
                    reqsSoFar += 3;
                }


                if (reqsSoFar > 4 * requests.Length / numThreads)
                {
                    endTimes.Add((int)Math.Ceiling(next_t));
                    reqsSoFar = 0;
                }
            }

            if (endTimes.Count != numThreads)
                endTimes.Add(yRef.T);

            //
            // figure out how to partition work across threads
            //
            List<Tuple<int, int>> timeRanges = new List<Tuple<int, int>>();
            int _time_partition = (int)Math.Floor(yRef.T * 1.0 / numThreads);
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
                    i == 0 ? 0 : endTimes[i - 1]+1, endTimes[i]
                    ));

            return timeRanges;
        }
        
        public Dictionary<int, double[]> ComputeReqProbForTimeRange
            (AugRequest[] requests, List<Tuple<int, int>> timeRanges)
        {
            Dictionary<int, double[]> requestProbs = new Dictionary<int, double[]>();
            for (int j = 0; j <requests.Length; j++)
            {
                Request r_j = requests[j];

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
            return requestProbs;
        }
        
        public Dictionary<int, List<int>>
            AssignRequestsToThreads(S14o_CurrentLPDirtyState ss, Dictionary<int, double[]> requestProbs)
        {
            Dictionary<int, List<int>> threadsToRequests = new Dictionary<int, List<int>>();
            for (int i = 0; i < ss.activeRequests.Length; i++)
            {
                if (ss.yetToSatisfyReqs[i] == false) continue;

                double d = randomness.pickRandomDouble();

                int x = 0;
                while (requestProbs[i][x] < d)
                    x++;

                if (!threadsToRequests.ContainsKey(x))
                    threadsToRequests.Add(x, new List<int>());
                threadsToRequests[x].Add(i);
            }
            return threadsToRequests;
        }

        public FeasibilityResult CheckFeasibility(S14o_CurrentLPDirtyState ss, YoungsSearchingFor ysf)
        {
            S14o_WorkerThread[] workers = new S14o_WorkerThread[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                workers[i] = new S14o_WorkerThread(ss.yRef, i);
            }

            Thread[] threads = new Thread[numThreads];

            // figure out how to partition work among threads
            List<Tuple<int, int>> timeRanges = SplitTimeRangeForThreads(ss.activeRequests);
            Dictionary<int, double[]> requestProbs = ComputeReqProbForTimeRange(ss.activeRequests, timeRanges);            

            Stopwatch sw;

            Debug.Assert(ysf == YoungsSearchingFor.alpha || ysf == YoungsSearchingFor.delta);

            int scatter_iter = 0;
            int num_rem_req;
            bool forceSingleThread = false;
            do
            {
                sw = new Stopwatch();
                sw.Start();
                Console.WriteLine("Scatter {0} {1}", scatter_iter++, forceSingleThread ? "!!ST!!" : "");

                if (!forceSingleThread  && numThreads > 1)
                {

                    // map requests to threads based on probabilities
                    Dictionary<int, List<int>> threadsToRequests = 
                        AssignRequestsToThreads(ss, requestProbs);

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
                        threads[j] = 
                            ysf == YoungsSearchingFor.alpha?
                            new Thread(new ThreadStart(workers[j].RunOnPartitionForAlpha)):
                            new Thread(new ThreadStart(workers[j].RunOnPartitionForDelta));
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
                    for (int i = 0; i < ss.activeRequests.Length; i++)
                        if (ss.yetToSatisfyReqs[i])
                            rem_req.Add(i);

                    workers[0].SetWork(0, yRef.T, rem_req.ToArray<int>());
                    workers[0].CopyMinPaths(ss, true);

                    threads[0] = 
                            ysf == YoungsSearchingFor.alpha?
                            new Thread(new ThreadStart(workers[0].RunOnPartitionForAlpha)):
                            new Thread(new ThreadStart(workers[0].RunOnPartitionForDelta));
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
                    ss.totalDemand_satisfied * 100.0 / ss.totalDemand);
                Console.WriteLine("------------------------");

                // what to do next?
                if (num_threads_no_progress == numThreads)
                    break;
                if ((forceSingleThread || numThreads == 1) &&
                    num_success_iters != S14o_WorkerThread.HowManyIters)
                    break;

                /* sk: should also be a function of T */
                if (!forceSingleThread && numThreads >1
                    &&
                    (num_rem_req < 20 ||
                    num_threads_no_progress > .5 * numThreads ||
                    num_success_iters < 1000))
                {
                    Console.WriteLine("\t <--- will force single thread");
                    forceSingleThread = true;
                }

                // reset worker state since the worker may not be invoked later...
                foreach (S14o_WorkerThread w in workers)
                {
                    w.success_iterations = 0;
                    w.madeForwardProgress = false;
                }

            } while (num_rem_req > 0);

            if (ss.yetToSatisfyReqs.Count(i => i == true) == 0)
            {
                Console.WriteLine("|| sat. all requests");
                return new FeasibilityResult(ss);
            }
            
            Console.WriteLine("|| req pending; no further steps feasible");
            return new FeasibilityResult(ss);
        }
    }

    //public class S14_online_YoungsAlgorithm_mt
    //{
    //    // all inputs are here
    //    S14_YoungsAlgorithm y;

    //    // thread local state
    //    S14_online_WorkerThread[] workers;
    //    int numThreads;

    //    S14_online_SharedState ss;

    //    public S14_online_YoungsAlgorithm_mt(S14_YoungsAlgorithm _y, int _numThreads)
    //    {
    //        y = _y;
    //        numThreads = _numThreads;
    //        ss = new S14_online_SharedState(y);
    //    }

    //    public double CheckFeasibility
    //        (int currTimestep,  // which timestep are we at?
    //         double alpha, double beta, double delta,  // what goal are we aiming for?
    //         out double achieved_alpha, out double achieved_delta // how far did we get
    //        )
    //    {
    //        ss.Step(currTimestep, alpha, delta, beta);

    //        // partition the work, issue the threads

    //        workers = new S14_online_WorkerThread[numThreads];
    //        for (int i = 0; i < numThreads; i++)
    //        {
    //            workers[i] = new S14_online_WorkerThread(y, i);
    //        }

    //        Thread[] threads = new Thread[numThreads];


    //        List<double>
    //            arrivals = new List<double>(),
    //            deadlines = new List<double>();
    //        for (int i = 0; i < ss.numAwareRequests; i++)
    //        {
    //            arrivals.Add(y.requests[i].arrival);
    //            deadlines.Add(y.requests[i].deadline);
    //        }
    //        arrivals.Sort();
    //        deadlines.Sort();

    //        List<int> endTimes = new List<int>();
    //        int reqsSoFar = 0;
    //        int a_i = 0, d_i = 0;
    //        while (a_i < arrivals.Count || d_i < deadlines.Count)
    //        {
    //            double next_t;
    //            double
    //                a = a_i < arrivals.Count ? arrivals[a_i] : double.MaxValue,
    //                d = d_i < deadlines.Count ? deadlines[d_i] : double.MaxValue;

    //            if (a < d)
    //            {
    //                next_t = arrivals[a_i];
    //                a_i++;
    //                reqsSoFar += 1;
    //            }
    //            else
    //            {
    //                next_t = deadlines[d_i];
    //                d_i++;
    //                reqsSoFar += 2;
    //            }


    //            if (reqsSoFar > 3 * ss.numAwareRequests / numThreads)
    //            {
    //                endTimes.Add((int)Math.Ceiling(next_t));
    //                reqsSoFar = 0;
    //            }
    //        }

    //        if (endTimes.Count != numThreads)
    //            endTimes.Add(y.T);

    //        //
    //        // figure out how to partition work across threads
    //        //
    //        List<Tuple<int, int>> timeRanges = new List<Tuple<int, int>>();
    //        int _time_partition = (int)Math.Floor(y.T * 1.0 / numThreads);
    //        for (int i = 0; i < numThreads; i++)
    //            /* equi-partition
    //            timeRanges.Add(
    //                new Tuple<int, int>(
    //                i * _time_partition,
    //                i == numThreads - 1 ? y.T : ((i + 1) * _time_partition - 1)
    //                ));
    //             */
    //            // weighted partition
    //            timeRanges.Add(new Tuple<int, int>(
    //                i==0? 0: endTimes[i-1], endTimes[i]-1
    //                ));

    //        Dictionary<int, double[]> requestProbs = new Dictionary<int, double[]>();
    //        for (int j = 0; j < ss.numAwareRequests; j++)
    //        {
    //            Request r_j = ss.requests[j];

    //            double[] probabilities = new double[timeRanges.Count];
    //            for (int k = 0; k < timeRanges.Count; k++)
    //            {
    //                Tuple<int, int> t_k = timeRanges[k];
    //                if (t_k.Item1 > r_j.deadline ||
    //                     t_k.Item2 < r_j.arrival)
    //                    probabilities[k] = 0;
    //                else
    //                    probabilities[k] =
    //                        (Math.Min(r_j.deadline, t_k.Item2) - Math.Max(r_j.arrival, t_k.Item1) + 1) * 1.0 /
    //                        (t_k.Item2 - t_k.Item1 + 1);
    //            }
    //            double sump = probabilities.Sum(i => i);
    //            double sumSoFar = 0;
    //            for (int k = 0; k < probabilities.Length; k++)
    //            {
    //                sumSoFar += probabilities[k];
    //                probabilities[k] = sumSoFar / sump;
    //            }
    //            probabilities[probabilities.Length - 1] = 1;

    //            requestProbs.Add(j, probabilities);
    //        }

    //        Stopwatch sw; 
    //        Random r = new Random(300);
    //        int scatter_iter = 0;
    //        int num_rem_req;
    //        bool forceSingleThread = false;
    //        do
    //        {
    //            sw = new Stopwatch();
    //            sw.Start();
    //            Console.WriteLine("Scatter {0} {1}", scatter_iter++, forceSingleThread?"!!ST!!":"");

    //            if (!forceSingleThread)
    //            {
    //                // thread ids go from 0 to numThreads-1
    //                // map requests to threads based on probabilities

    //                int[] requestsToThreads = new int[ss.numAwareRequests];
    //                Dictionary<int, List<int>> threadsToRequests = new Dictionary<int, List<int>>();
    //                for (int i = 0; i < ss.numAwareRequests; i++)
    //                {
    //                    if (ss.yetToSatisfyReqs[i] == false) continue;

    //                    double d = r.NextDouble();

    //                    int x = 0;
    //                    while (requestProbs[i][x] < d)
    //                        x++;

    //                    requestsToThreads[i] = x;
    //                    if (!threadsToRequests.ContainsKey(x))
    //                        threadsToRequests.Add(x, new List<int>());
    //                    threadsToRequests[x].Add(i);
    //                }

    //                // set them off to do some work
    //                WorkerThread.HowManyIters = 10000;

    //                for (int j = 0; j < numThreads; j++)
    //                {
    //                    if (!threadsToRequests.ContainsKey(j) ||
    //                         threadsToRequests[j].Count == 0)
    //                        continue;


    //                    workers[j].SetWork(
    //                        timeRanges[j].Item1,
    //                        timeRanges[j].Item2,
    //                        threadsToRequests[j].ToArray<int>()
    //                        );
    //                    workers[j].CopyMinPaths(ss);
    //                    threads[j] = new Thread(new ThreadStart(workers[j].RunOnPartition));
    //                    threads[j].Start();
    //                }


    //                // wait for them to join; gather some of their local state
    //                for (int j = 0; j < numThreads; j++)
    //                {
    //                    if (!threadsToRequests.ContainsKey(j) ||
    //                        threadsToRequests[j].Count == 0)
    //                        continue;

    //                    threads[j].Join();

    //                    // reassemble the paths
    //                    foreach (int rid in workers[j].req2shortestPathLength2time_and_index.Keys)
    //                        foreach (double k_d in workers[j].req2shortestPathLength2time_and_index[rid].Keys)
    //                        {
    //                            if (!ss.req2shortestPathLength2time_and_index[rid].ContainsKey(k_d))
    //                                ss.req2shortestPathLength2time_and_index[rid][k_d] =
    //                                   workers[j].req2shortestPathLength2time_and_index[rid][k_d];
    //                            else
    //                                ss.req2shortestPathLength2time_and_index[rid][k_d].AddRange
    //                                    (workers[j].req2shortestPathLength2time_and_index[rid][k_d]);
    //                        }
    //                }
    //            }
    //            else
    //            {
    //                List<int> rem_req = new List<int>();
    //                for(int i=0; i < ss.numAwareRequests; i++)
    //                    if ( ss.yetToSatisfyReqs[i] )
    //                        rem_req.Add( i );
                   
    //                workers[0].SetWork(0, ss.T, rem_req.ToArray<int>());
    //                workers[0].CopyMinPaths(ss, true);

    //                threads[0] = new Thread(new ThreadStart(workers[0].RunOnPartition));
    //                threads[0].Start();
    //                threads[0].Join();
    //            }
    //            sw.Stop();

    //            int num_threads_no_progress = workers.Count(w => w.madeForwardProgress == false);
    //            num_rem_req = ss.yetToSatisfyReqs.Count(i => i == true);
    //            int num_success_iters = workers.Sum(w => w.success_iterations);

    //            Console.WriteLine("Scatter iter {0} = {1}ms {2}#req_remain progress {3}T {4}I %FlowMet {5:F4}",
    //                scatter_iter - 1,
    //                sw.ElapsedMilliseconds,
    //                num_rem_req,
    //                numThreads - num_threads_no_progress,
    //                num_success_iters,
    //                ss.totalDemand_satisfied*100.0/ ss.totalDemand);
    //            Console.WriteLine("------------------------");

    //            // what to do next?
    //            if (num_threads_no_progress == numThreads)
    //                break;
    //            if (forceSingleThread &&
    //                num_success_iters != WorkerThread.HowManyIters)
    //                break;

    //            /* sk: should also be a function of T */
    //            if (num_rem_req < 20 ||
    //                num_threads_no_progress > .5 * numThreads ||
    //                num_success_iters < 1000)
    //            {
    //                Console.WriteLine("\t <--- will force single thread");
    //                forceSingleThread = true;
    //            }

    //            // reset worker state since the worker may not be invoked later...
    //            foreach (S14_online_WorkerThread w in workers)
    //            {
    //                w.success_iterations = 0;
    //                w.madeForwardProgress = false;
    //            }

    //        } while (num_rem_req > 0);

    //        double retval = -1;
    //        if (ss.yetToSatisfyReqs.Count(i=>i==true) == 0)
    //        {
    //            double worst_beta = 0;
    //            Console.WriteLine("|| Feasible solution for alpha {0} delta {1} beta {2}", alpha, delta, beta);
    //            foreach (Edge<int> e in y.orig_network.Edges)
    //            {
    //                int e_k = e.Source << YoungsAlgorithm.NumBitsForSource | e.Target;
    //                for (int t = 0; t <= y.T; t++)
    //                {
    //                    int key = t << 2 * YoungsAlgorithm.NumBitsForSource | e_k;

    //                    worst_beta = Math.Max(worst_beta, ss.edgeFlows[key] / y.edgeCapacities[e_k]);
    //                }
    //            }
    //            retval = worst_beta;

    //            // compute achieved_alpha
    //            achieved_alpha = 1;
    //            for (int _r = 0; _r < ss.requests.Length; _r++ )
    //            {
    //                achieved_alpha = Math.Min(achieved_alpha, ss.requestFlow[_r] / ss.requests[_r].demand);
    //            }

    //            achieved_delta = Math.Min(ss.totalDemand_satisfied / ss.totalDemand, 1);                
    //            Console.WriteLine("|| Obtained alpha {0} delta {1} beta {2}", achieved_alpha, achieved_delta, worst_beta);
    //         }
    //        else
    //        {
    //            Console.WriteLine("|| XX No Feasible solution for alpha {0} delta {1} beta {2}", alpha, delta, beta);
    //            achieved_alpha = 0;
    //            achieved_delta = 0;
    //        }

    //        return retval;
    //    }
    //}
}
