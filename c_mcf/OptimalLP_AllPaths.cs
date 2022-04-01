using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    class OptimalLP_AllPaths
    {
        public double Solve(BidirectionalGraph<int, Edge<int>> network, double alpha, int T, List<Request> requests,
            Dictionary<Tuple<int, int>, double> edgeLens, Dictionary<Tuple<int, int>, double> edgeCaps)
        {
            Dictionary<Tuple<int, int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int, int>, Decision>();
            Dictionary<Tuple<int, int>, Term> edgeFlows = new Dictionary<Tuple<int, int>, Term>();

            SolverContext context = new SolverContext();
            Model model = context.CreateModel();

            //Add decision variables to model
            for (int r = 0; r < requests.Count; r++)
            {
                for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                {
                    foreach (Edge<int> e in network.Edges)
                    {
                        f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)] = new Decision(Domain.RealNonnegative,
                            "f_" + e.Source + "_" + e.Target + "_" + t + "_" + r);
                        model.AddDecision(f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)]);
                    }
                }
            }

            //Add beta as decision variable
            Decision beta = new Decision(Domain.RealNonnegative, "beta");
            model.AddDecision(beta);
            model.AddConstraint(null, 0 <= beta);

            //demand constraints
            for (int r = 0; r < requests.Count; r++)
            {
                Term sum_flows = 0;
                for (int t = requests[r].arrival; t <= requests[r].deadline; t++)
                {
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
                }
                model.AddConstraint(null, alpha * requests[r].demand <= sum_flows
                    <= requests[r].demand);
            }

            //flow conservation constraints
            for (int t = 0; t <= T; t++)
            {
                for (int v = 0; v < network.VertexCount; v++)
                {
                    for (int r = 0; r < requests.Count; r++)
                    {
                        if (t < requests[r].arrival || t > requests[r].deadline) continue;
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
            }
            

            //capacity constraints
            Term sum = 0;
            for (int t = 0; t <= T; t++)
            {
                foreach (Edge<int> e in network.Edges)
                {
                    sum = 0;
                    for (int r = 0; r < requests.Count; r++)
                    {
                        if (t < requests[r].arrival || t > requests[r].deadline) continue;
                        sum = Model.Sum(sum, f[new Tuple<int, int, int, int>(e.Source, e.Target, t, r)]);
                    }

                    model.AddConstraint(null, sum <= beta * edgeCaps[new Tuple<int, int>(e.Source, e.Target)]);
                }
            }
            
            //minimize beta value
            model.AddGoal(null, GoalKind.Minimize, beta);
            Solution solution = context.Solve(new SimplexDirective());

            Console.WriteLine("-------Results of Optimal LP with all paths------");
            Console.WriteLine("Beta from Optimal LP with all paths: {0}", beta.ToDouble());
            Console.WriteLine("-------End of LP Results------");
            return beta.ToDouble();

        }
    }
}
