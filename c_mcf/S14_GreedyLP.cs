using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;
using System.Diagnostics;

namespace MaxConcurrentFlow
{
    // 1/24/2014 sk
    // edits on top of GreedyLP

    // demands are at least remaining bytes/ remaining time and at most remaininig bytes

    class S14_GreedyLP
    {
        public Dictionary<int, AugRequest> augRequests;

        public double Solve(
            BidirectionalGraph<int, Edge<int>> network ,
            int T, 
            List<Request> requests,
            Dictionary<int, double> edgeLens, 
            Dictionary<int, double> edgeCaps,
            Dictionary<int,List<Path>> pathDictionary)
        {
            augRequests = new Dictionary<int, AugRequest>();
            foreach (Request r in requests)
            {
                AugRequest ar = new AugRequest(r);
                augRequests.Add(ar.id, ar);
            }

            Dictionary<Tuple<int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int>, Decision>();
            double[] demands_left = new double[requests.Count];
            double betaValue = 0.0;

            //Initialize demands left for each request
            for(int r = 0; r < requests.Count; r++)
            {
                demands_left[r] = (double)requests[r].demand;
            }

            MeanStdNum msn_beta = new MeanStdNum(); // samples = max util. at each timestep
            MeanStdNum msn_flow_fraction = new MeanStdNum(); // samples = fraction of flow that is finished by deadline

            //Initialize timer 
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<Tuple<int, int>, Term> edgeFlows = new Dictionary<Tuple<int, int>, Term>();
            Boolean feasibleFlag = true;
            //Solve sequence of LPs
            for (int t = 0; t <= T; t++)
            {
                Stopwatch fullTau = new Stopwatch();
                fullTau.Start();

                f = new Dictionary<Tuple<int, int, int>, Decision>();
                SolverContext context = new SolverContext();
                Model model = context.CreateModel();

                //Add decision variables to model
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline)
                        continue;
                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        f[new Tuple<int, int, int>(p, t, r)] = new Decision(Domain.RealNonnegative,
                            "f_" + p + "_" + t + "_" + r);
                        model.AddDecision(f[new Tuple<int, int, int>(p, t, r)]);
                    }
                }
                
                //Add beta and alpha as decision variables
                Decision beta = new Decision(Domain.RealNonnegative, "beta");
                model.AddDecision(beta);
                model.AddConstraint(null, 0 <= beta <= 1);

                // this is to ensure feasibility; is an instantaneous alpha, has nothing to do with overall flow fractions
                Decision alpha = new Decision(Domain.RealNonnegative, "alpha");
                model.AddDecision(alpha);
                model.AddConstraint(null, 0 <= alpha <= 1);

                foreach (Edge<int> e in network.Edges)
                {
                    edgeFlows[new Tuple<int, int>(e.Source, e.Target)] = 0;
                }
                
                //capacity constraints
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline)
                        continue;
                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for(int p=0; p < pathsList.Count; p++)
                    {
                        foreach (Edge<int> e in pathsList[p].edgesList)
                        {
                            edgeFlows[new Tuple<int, int>(e.Source, e.Target)] += f[new Tuple<int, int, int>(p, t, r)];
                        }
                        
                    }
                }

                //capacity constraints
                foreach (Edge<int> e in network.Edges)
                {
                    model.AddConstraint(null, edgeFlows[new Tuple<int, int>(e.Source, e.Target)] <=
                        beta * edgeCaps[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target]);
                }

                
                //demand constraints
                Term all_flow = 0;
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r]<=0) continue;
                    Term sum_flows = 0;

                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        sum_flows += f[new Tuple<int, int, int>(p, t, r)];
                    }

                    model.AddConstraint
                        (null, 
                        alpha * (demands_left[r]) / (requests[r].deadline - t + 1) 
                        <= sum_flows 
                        <= demands_left[r]);

                    all_flow += sum_flows;
                }

                //maximize flow will also max inst. alpha; slight pref. to keep beta small
                model.AddGoal(null, GoalKind.Maximize, all_flow - 0.1 *beta + alpha);
                // model.AddGoal(null, GoalKind.Maximize, - 0.001 * beta + alpha);
                Solution solution = context.Solve(new SimplexDirective());

                // guaranteed to be feasible                

                //update demand left for each request
                double actualFlow = 0;
                for (int r = 0; r < requests.Count; r++)
                {
                    actualFlow = 0;
                    if (t < requests[r].arrival || t > requests[r].deadline) continue;
                    if (demands_left[r] > 0)
                    {
                        List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                        for (int p = 0; p < pathsList.Count; p++)
                        {
                            actualFlow += f[new Tuple<int, int, int>(p, t, r)].ToDouble();
                        }

                        demands_left[r] = demands_left[r] - actualFlow;
                    }

                    augRequests[r].totalFlow += actualFlow;
                    augRequests[r].promisedAlphas[t - augRequests[r].awareTime] = augRequests[r].totalFlow / augRequests[r].demand;
                }

                // write out link usages
                Dictionary<int, double> linkUsages = new Dictionary<int, double>();
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline)
                        continue;
                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        foreach (Edge<int> e in pathsList[p].edgesList)
                        {
                            int e_k = (e.Source << YoungsAlgorithm.NumBitsForSource) | e.Target;
                            if (!linkUsages.ContainsKey(e_k)) 
                                linkUsages.Add(e_k, 0);

                            linkUsages[e_k] += f[new Tuple<int, int, int>(p, t, r)].ToDouble();
                        }
                    }
                }
                foreach (int edgeIndex in linkUsages.Keys)
                {
                    int source = edgeIndex >> YoungsAlgorithm.NumBitsForSource;
                    int target = edgeIndex & ( (1 << YoungsAlgorithm.NumBitsForSource) - 1);
                    Console.WriteLine("Finished edge usage {0} {1} -> {2} @ {3} cap {4} beta {5}",
                        linkUsages[edgeIndex],
                        source,
                        target,
                        t,
                        edgeCaps[edgeIndex],
                        beta.ToDouble()
                        );
                }

                //Minimizing beta in each time interval
                //But taking the worst of all such betas for comparing with calendaring method
                msn_beta.AddSample(beta.ToDouble());
                if (beta.ToDouble() >= betaValue)
                {
                    betaValue = beta.ToDouble();
                }
                if (beta.ToDouble() > 1)
                    feasibleFlag = false;

                fullTau.Stop();
                Console.WriteLine("timeElapsed Tau {0} {1} ms", t, fullTau.ElapsedMilliseconds);
            }

            //total number of flows that finished
            int fulfilledFlows = 0;
            for (int r = 0; r < requests.Count; r++)
            {
                if (demands_left[r] <= 0.0000001)   //to take care of precision issues
                {
                    fulfilledFlows += 1;
                }
            }

            //finding what fraction of demand was satisfied for each flow
            double bestAlpha = double.MaxValue;
            for (int r = 0; r < requests.Count; r++)
            {
                double flow_fraction = 1 - (demands_left[r] / requests[r].demand);
                msn_flow_fraction.AddSample(flow_fraction);

                if (flow_fraction <= bestAlpha)
                {
                    bestAlpha = flow_fraction;
                }
            }

            foreach (AugRequest ar in augRequests.Values)
            {
                Console.WriteLine("Finished flow {0}", ar);
            }

            timer.Stop();
            Console.WriteLine("-------Results of S14_Greedy LP------");
            Console.WriteLine("Beta from Greedy LP: {0} and feasibleFlag is {1}", betaValue, feasibleFlag);
            Console.WriteLine("Best alpha is {0}", bestAlpha);
            Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("Number of flows fulfilled: {0} / {1}", fulfilledFlows, requests.Count);
            Console.WriteLine("Betas: {0}", msn_beta.GetDetailedString());
            Console.WriteLine("Flow fractions: {0}", msn_flow_fraction.GetDetailedString());
            Console.WriteLine("-------End of S14_Greedy LP Results------");
            Console.WriteLine();
            return betaValue;
        }
    }
}
