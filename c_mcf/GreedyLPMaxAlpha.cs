using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    class GreedyLPMaxAlpha
    {
        public double Solve(BidirectionalGraph<int, Edge<int>> network,int T, List<Request> requests,
            Dictionary<Tuple<int, int>, double> edgeLens, Dictionary<Tuple<int, int>, double> edgeCaps,
            Dictionary<Tuple<int, int>, List<Path>> pathDictionary)
        {
            Dictionary<Tuple<int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int>, Decision>();
            double[] demands_left = new double[requests.Count];
            // double betaValue = 0.0;

            //Initialize demands left for each request
            for (int r = 0; r < requests.Count; r++)
            {
                demands_left[r] = (double)requests[r].demand;
            }

            //Initialize timer 
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<Tuple<int, int>, Term> edgeFlows = new Dictionary<Tuple<int, int>, Term>();
            //Solve sequence of LPs
            for (int t = 0; t <= T; t++)
            {

                f = new Dictionary<Tuple<int, int, int>, Decision>();
                SolverContext context = new SolverContext();
                Model model = context.CreateModel();

                //Add decision variables to model
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline)
                        continue;
                    List<Path> pathsList = pathDictionary[new Tuple<int, int>(requests[r].src, requests[r].dest)];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        f[new Tuple<int, int, int>(p, t, r)] = new Decision(Domain.RealNonnegative,
                            "f_" + p + "_" + t + "_" + r);
                        model.AddDecision(f[new Tuple<int, int, int>(p, t, r)]);
                    }
                }

                //Add beta as decision variable
                Decision alpha = new Decision(Domain.RealNonnegative, "alpha");
                model.AddDecision(alpha);
                model.AddConstraint(null, 0 <= alpha <=1);

                foreach (Edge<int> e in network.Edges)
                {
                    edgeFlows[new Tuple<int, int>(e.Source, e.Target)] = 0;
                }

                //capacity constraints
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline)
                        continue;
                    List<Path> pathsList = pathDictionary[new Tuple<int, int>(requests[r].src, requests[r].dest)];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        foreach (Edge<int> e in pathsList[p].edgesList)
                        {
                            edgeFlows[new Tuple<int, int>(e.Source, e.Target)] += f[new Tuple<int, int, int>(p, t, r)];
                        }

                    }
                }

                foreach (Edge<int> e in network.Edges)
                {
                    model.AddConstraint(null, edgeFlows[new Tuple<int, int>(e.Source, e.Target)] <=
                        edgeCaps[new Tuple<int, int>(e.Source, e.Target)]);
                }


                //demand constraints
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                    Term sum_flows = 0;

                    List<Path> pathsList = pathDictionary[new Tuple<int, int>(requests[r].src, requests[r].dest)];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        sum_flows += f[new Tuple<int, int, int>(p, t, r)];
                    }

                    model.AddConstraint(null, alpha * (demands_left[r]) / (requests[r].deadline - t + 1) <= sum_flows
                        <= (demands_left[r]) / (requests[r].deadline - t + 1));
                }

                //minimize beta value
                model.AddGoal(null, GoalKind.Maximize, alpha);
                Solution solution = context.Solve(new SimplexDirective());

                //update demand left for each request
                double actualFlow = 0;
                for (int r = 0; r < requests.Count; r++)
                {
                    actualFlow = 0;
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                    List<Path> pathsList = pathDictionary[new Tuple<int, int>(requests[r].src, requests[r].dest)];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        actualFlow += f[new Tuple<int, int, int>(p, t, r)].ToDouble();
                    }

                    demands_left[r] = demands_left[r] - actualFlow;
                }
            }

            double bestAlpha = double.MaxValue; 
            for (int r = 0; r < requests.Count; r++)
            {
                if (1 - (demands_left[r]/requests[r].demand) <= bestAlpha)
                {
                    bestAlpha = 1 - demands_left[r] / requests[r].demand;
                }
            }
            timer.Stop();
            Console.WriteLine("-------Results of Greedy LP Which Maximizes Alpha------");
            Console.WriteLine("Alpha from Greedy LP: {0}",bestAlpha);
            Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("-------End of LP Results------");
            Console.WriteLine();
            return bestAlpha;
        }
    }
}
