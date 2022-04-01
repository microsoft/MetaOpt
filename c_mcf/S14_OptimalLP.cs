using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    class S14_OptimalLP
    {
        public double Solve(BidirectionalGraph<int, Edge<int>> network, int T, List<Request> requests,
            Dictionary<int, double> edgeLens, Dictionary<int, double> edgeCaps,
            Dictionary<int, List<Path>> pathDictionary)
        {
            Dictionary<Tuple<int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int>, Decision>();

            // samples = max. link utilization over each timestep
            MeanStdNum msn_beta = new MeanStdNum();
            MeanStdNum msn_flow_fraction = new MeanStdNum();

            //Initialize timer 
            var timer = System.Diagnostics.Stopwatch.StartNew();
            
            SolverContext context = new SolverContext();
            Model model = context.CreateModel();

            //Add decision variables to model
            for (int r = 0; r < requests.Count; r++)
            {
                List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                {
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        f[new Tuple<int, int, int>(p, t, r)] = new Decision(Domain.RealNonnegative,
                            "f_" + p + "_" + t + "_" + r);
                        model.AddDecision(f[new Tuple<int, int, int>(p, t, r)]);
                    }
                }
            }

            //Add beta as decision variable
            Decision beta = new Decision(Domain.RealNonnegative, "beta");
            model.AddDecision(beta);
            model.AddConstraint(null, 0 <= beta <= 1);

            // full alpha
            Decision alpha = new Decision(Domain.RealNonnegative, "alpha");
            model.AddDecision(alpha);
            model.AddConstraint(null, 0 <= alpha <= 1);

            //capacity constraints
            for (int t = 0; t <= T; t++ )
            {
                //
                // convenience dict of terms
                // these are not decision variables; so no need to make them per time
                //
                Dictionary<Tuple<int, int>, Term> edgeFlows = new Dictionary<Tuple<int, int>, Term>();

                foreach (Edge<int> e in network.Edges)
                {
                    edgeFlows[new Tuple<int, int>(e.Source, e.Target)] = 0;
                }

                for (int r = 0; r < requests.Count; r++ )
                {
                    if (requests[r].arrival > t || requests[r].deadline < t) continue;
                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
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
                        beta * edgeCaps[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target]);
                }
            }

            //demand constraints
            Term totalFlow = 0;
            for (int r = 0; r < requests.Count; r++)
            {
                Term sum_flows = 0;

                List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                {
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        sum_flows += f[new Tuple<int, int, int>(p, t, r)];
                    }
                }
                model.AddConstraint(null, requests[r].demand >= sum_flows >= alpha * requests[r].demand);

                totalFlow += sum_flows / requests[r].demand;
            }

            //
            // \alpha <= totalFlow <= \alpha + requests.Count -1
            // 
            // totalFlow's weight is such that its contribution to goal is always much less than that from inc. alpha
            //
            // very low weight on beta; just to smooth out network load
            //
            model.AddGoal(null, GoalKind.Maximize, alpha + totalFlow / (100 * requests.Count) - .0001 * beta);
            // model.AddGoal(null, GoalKind.Maximize, alpha);
            
            Console.WriteLine("Ready to run solve; time elapsed {0}", timer.ElapsedMilliseconds);
            Solution solution = context.Solve(new SimplexDirective());
            timer.Stop();

            Console.WriteLine("solution quality: {0}, goal {1}", solution.Quality, solution.Goals.First().ToDouble());

            // extract individual flow fractions
            int numFulfilled = 0;
            for (int r = 0; r < requests.Count; r++)
            {
                double total_flow = 0;
                List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                {
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        total_flow += f[new Tuple<int, int, int>(p, t, r)].ToDouble();
                    }
                }

                double flow_fraction = total_flow / requests[r].demand;
                if (flow_fraction > .9999)
                    numFulfilled++;
                msn_flow_fraction.AddSample(flow_fraction);
            }

            // extract betas of individual timesteps
            for (int t = 0; t <= T; t++)
            {
                Dictionary<Tuple<int, int>, double> linkUtilization =
                    new Dictionary<Tuple<int, int>, double>();

                foreach (Edge<int> e in network.Edges)
                    linkUtilization.Add(new Tuple<int, int>(e.Source, e.Target), 0);

                for (int r = 0; r < requests.Count; r++)
                {
                    if (requests[r].arrival > t || requests[r].deadline < t) continue;

                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        foreach (Edge<int> e in pathsList[p].edgesList)
                        {
                            linkUtilization[new Tuple<int, int>(e.Source, e.Target)] += 
                                f[new Tuple<int, int, int>(p, t, r)].ToDouble() / 
                                edgeCaps[e.Source << YoungsAlgorithm.NumBitsForSource | e.Target];
                        }
                    }
                }

                double maxUtilization = linkUtilization.Values.Max(i => i);
                msn_beta.AddSample(maxUtilization);
            }

            Console.WriteLine("-------Results of S14 Optimal LP------");
            Console.WriteLine("Model: {0} var {1} constraints", model.Decisions.Sum(i => 1), model.Constraints.Sum(i => 1));
            Console.WriteLine("Beta from Optimal LP: {0}", beta.ToDouble());
            Console.WriteLine("Alpha from Optimal LP: {0}", alpha.ToDouble());
            Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("Betas: {0}", msn_beta.GetDetailedString());
            Console.WriteLine("Flow fractions: {0}", msn_flow_fraction.GetDetailedString());
            Console.WriteLine("Number of flows fulfilled: {0} / {1}", numFulfilled, requests.Count);
            Console.WriteLine("-------End of S14 Optimal LP Results------");
            return beta.ToDouble();
        }
    }
}
