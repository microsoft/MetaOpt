using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    class GreedyLP_AllPaths
    {
        public double Solve(BidirectionalGraph<int, Edge<int>> network, double alpha, int T, List<Request> requests,
            Dictionary<Tuple<int, int>, double> edgeLens, Dictionary<Tuple<int, int>, double> edgeCaps)
        {
            Dictionary<Tuple<int, int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int, int>, Decision>();
            double[] demands_left = new double[requests.Count];
            double betaValue = 0.0;

            //Initialize demands left for each request
            for (int r = 0; r < requests.Count; r++)
            {
                demands_left[r] = (double)requests[r].demand;
            }

            //Initialize timer 
            var timer = System.Diagnostics.Stopwatch.StartNew();
            //Solve sequence of LPs
            for (int t = 0; t <= T; t++)
            {

                f = new Dictionary<Tuple<int, int, int, int>, Decision>();
                SolverContext context = new SolverContext();
                Model model = context.CreateModel();

                //Add decision variables to model
                foreach (Edge<int> e in network.Edges)
                {
                    for (int r = 0; r < requests.Count; r++)
                    {
                        if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                        f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)] = new Decision(Domain.RealNonnegative,
                            "f_" + e.Source + "_" + e.Target + "_" + t + "_" + r);
                        model.AddDecision(f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)]);
                    }
                }


                //Add beta as decision variable
                Decision beta = new Decision(Domain.RealNonnegative, "beta");
                model.AddDecision(beta);
                model.AddConstraint(null, 0 <= beta);

                //capacity constraints
                Term sum = 0;
                foreach (Edge<int> e in network.Edges)
                {
                    sum = 0;
                    for (int r = 0; r < requests.Count; r++)
                    {
                        if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                        sum = Model.Sum(sum, f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)]);
                    }

                    model.AddConstraint(null, sum <= beta * edgeCaps[new Tuple<int, int>(e.Source, e.Target)]);
                }

                //flow conservation constraints
                for (int v = 0; v < network.VertexCount; v++)
                {
                    for (int r = 0; r < requests.Count; r++)
                    {
                        if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                        if (v == requests[r].src || v == requests[r].dest)
                            continue;

                        Term sum_in = 0;
                        Term sum_out = 0;

                        IEnumerable<Edge<int>> outEdges;
                        bool hasOutEdges = network.TryGetOutEdges(v, out outEdges);
                        if (hasOutEdges)
                        {
                            foreach (Edge<int> e in outEdges)
                            {
                                sum_out = Model.Sum(sum_out, f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)]);
                            }
                        }

                        IEnumerable<Edge<int>> inEdges;
                        bool hasInEdges = network.TryGetInEdges(v, out inEdges);
                        if (hasInEdges)
                        {
                            foreach (Edge<int> e in inEdges)
                            {
                                sum_in = Model.Sum(sum_in, f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)]);
                            }
                        }
                        model.AddConstraint(null, sum_in == sum_out);
                    }
                }

                //demand constraints
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                    Term sum_flows = 0;
                    IEnumerable<Edge<int>> sourceOutEdges, sourceInEdges;
                    network.TryGetInEdges(requests[r].src, out sourceInEdges);
                    network.TryGetOutEdges(requests[r].src, out sourceOutEdges);

                    foreach (Edge<int> e in sourceOutEdges)
                    {
                        sum_flows += f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)];
                    }
                    foreach (Edge<int> e in sourceInEdges)
                    {
                        sum_flows -= f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)];
                    }
                    model.AddConstraint(null, alpha * (demands_left[r]) / (requests[r].deadline - t + 1) <= sum_flows
                        <= (demands_left[r]) / (requests[r].deadline - t + 1));
                }

                //minimize beta value
                model.AddGoal(null, GoalKind.Minimize, beta);
                Solution solution = context.Solve(new SimplexDirective());

                //update demand left for each request
                double actualFlow = 0;
                for (int r = 0; r < requests.Count; r++)
                {
                    actualFlow = 0;
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <= 0) continue;
                    IEnumerable<Edge<int>> sourceOutEdges, sourceInEdges;
                    network.TryGetInEdges(requests[r].src, out sourceInEdges);
                    network.TryGetOutEdges(requests[r].src, out sourceOutEdges);

                    foreach (Edge<int> e in sourceOutEdges)
                    {
                        actualFlow += f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)].ToDouble();
                    }
                    foreach (Edge<int> e in sourceInEdges)
                    {
                        actualFlow -= f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)].ToDouble();
                    }

                    demands_left[r] = demands_left[r] - actualFlow;
                }

                //Minimizing beta in each time interval
                //But taking the worst of all such betas for comparing with calendaring method
                if (beta.ToDouble() >= betaValue)
                {
                    betaValue = beta.ToDouble();
                }

                //Report report = solution.GetReport();
                //Console.WriteLine(report.ToString());

                foreach (Edge<int> e in network.Edges)
                {
                    double sum_flow_edge = 0;
                    for (int r = 0; r < requests.Count; r++)
                    {
                        if (requests[r].arrival > t || requests[r].deadline < t) continue;
                        sum_flow_edge += f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)].ToDouble();
                    }
                }
            }

            int fulfilledFlows = 0;
            for (int r = 0; r < requests.Count; r++)
            {
                if (demands_left[r] <= 0.0000001)
                {
                    fulfilledFlows += 1;
                }
            }

            timer.Stop();
            Console.WriteLine("-------Results of Greedy LP------");
            Console.WriteLine("Beta from Greedy LP: {0}", betaValue);
            Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("Number of flows fulfilled: {0}", fulfilledFlows);
            Console.WriteLine("-------End of LP Results------");
            Console.WriteLine();
            return betaValue;
        }
    }
}
