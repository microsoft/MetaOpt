using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using Microsoft.SolverFoundation.Services;
using Microsoft.SolverFoundation.Common;

namespace MaxConcurrentFlow
{
    class GreedyLP
    {
        public double Solve(
            BidirectionalGraph<int, Edge<int>> network ,
            double alpha, 
            int T, 
            List<Request> requests,
            Dictionary<int, double> edgeLens, 
            Dictionary<int, double> edgeCaps,
            Dictionary<int,List<Path>> pathDictionary)
        {
            Dictionary<Tuple<int, int, int>, Decision> f = new Dictionary<Tuple<int, int, int>, Decision>();
            double[] demands_left = new double[requests.Count];
            double betaValue = 0.0;

            //Initialize demands left for each request
            for(int r = 0; r < requests.Count; r++)
            {
                demands_left[r] = (double)requests[r].demand;
            }

            //Initialize timer 
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<Tuple<int, int>, Term> edgeFlows = new Dictionary<Tuple<int, int>, Term>();
            Boolean feasibleFlag = true;
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
                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        f[new Tuple<int, int, int>(p, t, r)] = new Decision(Domain.RealNonnegative,
                            "f_" + p + "_" + t + "_" + r);
                        model.AddDecision(f[new Tuple<int, int, int>(p, t, r)]);
                    }
                }
                
                //Add beta as decision variable
                Decision beta = new Decision(Domain.RealNonnegative, "beta");
                model.AddDecision(beta);
                model.AddConstraint(null, 0 <= beta);

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
                for (int r = 0; r < requests.Count; r++)
                {
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r]<=0) continue;
                    Term sum_flows = 0;

                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        sum_flows += f[new Tuple<int, int, int>(p, t, r)];
                    }

                    model.AddConstraint(null, alpha * (demands_left[r]) / (requests[r].deadline - t + 1) <= sum_flows 
                        <= (demands_left[r]) / (requests[r].deadline - t + 1));
                }

                //minimize beta value
                model.AddGoal(null, GoalKind.Minimize, beta);
                Solution solution = context.Solve(new SimplexDirective());

                /*
                //Newlya added code - Maximize alpha if beta <=1 is not feasible
                if (beta.ToDouble() < -1)
                {
                    f = new Dictionary<Tuple<int, int, int>, Decision>();
                    context = new SolverContext();
                    model = context.CreateModel();

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
                    Decision Alpha = new Decision(Domain.RealNonnegative, "Alpha");
                    model.AddDecision(Alpha);
                    model.AddConstraint(null, 0 <= Alpha <= 1);

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

                        model.AddConstraint(null, Alpha * (demands_left[r]) / (requests[r].deadline - t + 1) <= sum_flows
                            <= (demands_left[r]) / (requests[r].deadline - t + 1));
                    }

                    //minimize beta value
                    model.AddGoal(null, GoalKind.Maximize, Alpha);
                    solution = context.Solve(new SimplexDirective());

                    Console.WriteLine("Maximizing alpha and got value {0}", Alpha.ToDouble());

                }
                //Maximizing alpha ends here
                */

                //update demand left for each request
                double actualFlow = 0;
                for (int r = 0; r < requests.Count; r++)
                {
                    actualFlow = 0;
                    if (t < requests[r].arrival || t > requests[r].deadline || demands_left[r] <=0) continue;
                    List<Path> pathsList = pathDictionary[requests[r].src << YoungsAlgorithm.NumBitsForSource | requests[r].dest];
                    for (int p = 0; p < pathsList.Count; p++)
                    {
                        actualFlow += f[new Tuple<int, int, int>(p, t, r)].ToDouble();
                    }

                    demands_left[r] = demands_left[r] - actualFlow;
                }

                //Minimizing beta in each time interval
                //But taking the worst of all such betas for comparing with calendaring method
                if (beta.ToDouble() >= betaValue)
                {
                    betaValue = beta.ToDouble();
                }
                if (beta.ToDouble() > 1)
                    feasibleFlag = false;

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
                if (1 - (demands_left[r] / requests[r].demand) <= bestAlpha)
                {
                    bestAlpha = 1 - demands_left[r] / requests[r].demand;
                }
            }

            timer.Stop();
            Console.WriteLine("-------Results of Greedy LP------");
            Console.WriteLine("Beta from Greedy LP: {0} and feasibleFlag is {1}", betaValue, feasibleFlag);
            Console.WriteLine("Best alpha is {0}", bestAlpha);
            Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds);
            Console.WriteLine("Number of flows fulfilled: {0}", fulfilledFlows);
            Console.WriteLine("-------End of LP Results------");
            Console.WriteLine();
            return betaValue;
        }
    }
}
