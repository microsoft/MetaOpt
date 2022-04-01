using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    class OptimalLP
    {
        public double Solve(BidirectionalGraph<int, Edge<int>> network, double alpha, int T, List<Request> requests,
            Dictionary<int, double> edgeLens, Dictionary<int, double> edgeCaps,
            Dictionary<int, List<Path>> pathDictionary)
        {
            Dictionary<Tuple<int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int>, Decision>();

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
            model.AddConstraint(null, 0 <= beta);

            //capacity constraints
            for (int t = 0; t <= T; t++ )
            {
                // convenience dict of terms
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
            }

            //minimize beta value
            model.AddGoal(null, GoalKind.Minimize, beta);
            Console.WriteLine("Ready to run solve; time elapsed {0}", timer.ElapsedMilliseconds);
            Solution solution = context.Solve(new SimplexDirective());
            timer.Stop();


            Console.WriteLine("-------Results of Optimal LP------");
            Console.WriteLine("Model: {0} var {1} constraints", model.Decisions.Sum(i=>1), model.Constraints.Sum(i => 1));
            Console.WriteLine("Beta from Optimal LP: {0}", beta.ToDouble());
            Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("-------End of LP Results------");
            return beta.ToDouble();
        }
    }
}
