using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;
using System.IO;
using System.Xml.Linq;
using System.Diagnostics;
using Microsoft.SolverFoundation.Services;

namespace MaxConcurrentFlow
{
    public class RunArgs
    {
        public bool RunGreedy = false, RunGreedyS14=false;
        public bool RunOptimal = false, RunOptimalS14=false;
        public bool RunYoungsSt = false, RunYoungsMt = false, RunYoungsS14=false, RunYoungsS14_online = false;
        public bool UseReqsFromFile = false, WriteReqsToFile = false;
        public bool RunOldMain = false;

        public int numThreads = 6;
        public string networkFileName, tmsDirectory, reqFileName;
        public int ScaleDemandBy = 100; // load factor will be 1; capacity scaled by 50

        public RunArgs(string[] args)
        {
            if (args.Length == 0)
                Usage();

            try
            {
                int i = 0;
                while (i < args.Length)
                {
                    switch (args[i])
                    {
                        case "--old":
                            RunOldMain = true;
                            break;
                        case "-h":
                        case "--help":
                            Usage();
                            break;
                        case "-g":
                            RunGreedy = true;
                            break;
                        case "-gS14":
                            RunGreedyS14 = true;
                            break;
                        case "-o":
                            RunOptimal = true;
                            break;
                        case "-oS14":
                            RunOptimalS14 = true;
                            break;
                        case "-y_s":
                            RunYoungsSt = true;
                            break;
                        case "-y_m":
                            RunYoungsMt = true;
                            break;
                        case "-yS14":
                            RunYoungsS14 = true;
                            break;
                        case "-yOS14":
                            RunYoungsS14_online = true;
                            break;
                        case "-n":
                            i++;
                            numThreads = int.Parse(args[i]);
                            break;
                        case "--net":
                            i++;
                            networkFileName = args[i];
                            break;
                        case "--tms":
                            i++;
                            tmsDirectory = args[i];
                            break;
                        case "--genReqs":
                            i++;
                            WriteReqsToFile = true;
                            reqFileName = args[i];
                            i++;
                            ScaleDemandBy = int.Parse(args[i]);
                            break;
                        case "--useReqs":
                            UseReqsFromFile = true;
                            i++;
                            reqFileName = args[i];
                            break;
                        case "--cScale":
                            i++;
                            S14o_GlobalState.BetaExponentScale = int.Parse(args[i]);
                            Console.WriteLine("Beta exponent scale set to {0}", S14o_GlobalState.BetaExponentScale);
                            break;
                        default:
                            Usage();
                            break;
                    }
                    i++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("\t parse args error " + e);
                Usage();
            }
        }
        public void Usage()
        {
            Console.WriteLine(@"
./MaxConcurrentFlow.exe 
    -g, -gS14            # run greedy
    -o, -oS14            # run optimal
    -y_s                 # run young's single thread                
    -y_m                 # run young's multiple threads
    -n <int>             # how many threads
    --net <file>         # network topology
    --tms <dir>          # traffic matrices directory
    --genReqs <file> <scaleDemBy>    # write the generatedRequests to file
    --useReqs <file>     # use the requests in specified file
");
            Environment.Exit(0);
        }
    }

    public enum YoungsSearchingFor { alpha = 1, delta = 2, beta = 3 };

    public class Program
    {

        static double
            TestFake(
                double alpha, double beta, double delta,
                out double achieved_alpha, out double achieved_delta)
        {
            double goal_alpha = .8, goal_delta = .3, goal_beta = .2;

            achieved_alpha = -1;
            achieved_delta = -1;

            if (alpha < goal_alpha) // beta = 1, delta = 0
            {
                achieved_alpha = alpha;
                achieved_delta = goal_delta / 3;  // to be increased later
                return goal_beta *2 ; // to be decreased later
            }
            else if (alpha > goal_alpha)
            {
                return -1;
            }

            // if here, alpha = goal_alpha
            achieved_alpha = goal_alpha;

            if (delta < goal_delta)
            {
                achieved_alpha = alpha;
                achieved_delta = delta;
                return goal_beta * 2; // to be decreased later
            }
            else if (delta > goal_delta)
            {
                return -1;
            }

            // if here, delta = goal_delta
            achieved_delta = goal_delta;

            if (beta < goal_beta)
            {
                return -1;
            }
            else if (beta > goal_beta)
            {
                return beta;
            }

            return beta;
        }

        // new main for SIGCOMM
        static void Main(string[] args)
        {
            SolverContext sc = new SolverContext();

            RunArgs ra = new RunArgs(args);

            if (ra.RunOldMain)
            {
                Old_Main(args);
                return;
            }

            Debug.Assert(ra.networkFileName != null && ra.tmsDirectory != null);



            Dictionary<int, double> edgeCapacities;
            BidirectionalGraph<int, Edge<int>> originalNetwork;
            Dictionary<int, double>[] trafficMatrices;
            
            NetworkGenerator netGen = new NetworkGenerator();
            
            originalNetwork = netGen.readNetworkFromFile(ra.networkFileName, out edgeCapacities);
            trafficMatrices = netGen.readTrafficMatrices(ra.tmsDirectory);

            // massage the datasets
            int capacityScaleFactor = 50, demandScaleFactor = ra.ScaleDemandBy;
            int T = trafficMatrices.Length;
            trafficMatrices = netGen.spreadOutTrafficMatrices(T, demandScaleFactor, trafficMatrices);
            edgeCapacities = netGen.scaleCapacities(capacityScaleFactor, edgeCapacities);

            double begin_d, end_d;
            begin_d = netGen.HowMuchDemandsRemain(trafficMatrices);

            int reqCount = 10000;
            List<Request> requests = 
                (ra.UseReqsFromFile)?
                netGen.useReqsFromFile(ra.reqFileName):
                netGen.generateReqsToMatchTM(originalNetwork, trafficMatrices, reqCount, 20);

            end_d = netGen.HowMuchDemandsRemain(trafficMatrices);
            if ( !ra.UseReqsFromFile )
                Console.WriteLine("Total demands: {0} --> {1}", begin_d, end_d);

            if (ra.WriteReqsToFile)
                netGen.writeReqsToFile(ra.reqFileName, requests);
            


            // paths
            int K = 15; // number of paths between source destination pairs
            Dictionary<int, List<Path>> pathDictionary =
                netGen.ComputePathDictionary(requests, originalNetwork, K);

            // for ishai, to plugin to gurobi; change alpha
            netGen.dumpGAMSPathsMaxAlpha(originalNetwork, requests, T, edgeCapacities, 1, pathDictionary);


            if (ra.RunGreedyS14)
            {
                S14_GreedyLP s14g = new S14_GreedyLP();
                s14g.Solve(originalNetwork, T, requests, null, edgeCapacities, pathDictionary);
            }

            if (ra.RunGreedy)
            {
                GreedyLP greedyLp = new GreedyLP();
                double greedyBeta = greedyLp.Solve(originalNetwork, 1, T, requests, null, edgeCapacities, pathDictionary);
                Console.WriteLine("The value of greedy beta is {0}", greedyBeta);
                Console.WriteLine("------------End of Greedy LP algorithm---------------------");
            }

            if (ra.RunOptimalS14)
            {
                S14_OptimalLP optimalLp = new S14_OptimalLP();
                optimalLp.Solve(originalNetwork, T, requests, null,
                    edgeCapacities, pathDictionary);
            }

            if (ra.RunOptimal)
            {
                OptimalLP optimalLp = new OptimalLP();
                double optimalBeta = optimalLp.Solve(originalNetwork, 1, T, requests, null,
                    edgeCapacities, pathDictionary);
                Console.WriteLine("The value of optimal beta is {0}", optimalBeta);
                Console.WriteLine("-----------End of Optimal LP algorithm---------------------");
            }

            if (ra.RunYoungsS14_online)
            {   
                S14_YoungsAlgorithm young =
                    new S14_YoungsAlgorithm(originalNetwork, edgeCapacities, requests, T, pathDictionary, S14o_GlobalState.epsilon);

                S14o_Youngs oy = new S14o_Youngs(young, ra.numThreads);
                oy.NewRun();
            }

            if (ra.RunYoungsS14)
            {
                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                double epsilon = .2;

                S14_YoungsAlgorithm young =
                    new S14_YoungsAlgorithm(originalNetwork, edgeCapacities, requests, T, pathDictionary, epsilon);
                S14_2_YoungsAlgorithm_mt young_mt = new S14_2_YoungsAlgorithm_mt(young, ra.numThreads);

                //
                // first binary search for max alpha with beta =1; gamma =0 
                // then search for max gamma, then min beta                
                //

                double
                    alpha_max_feasible = 0, alpha_min_infeasible = 1,
                    beta_max_infeasible = 0, beta_min_feasible = 1,
                    delta_max_feasible = 0, delta_min_infeasible = 1;

                YoungsSearchingFor ysf = YoungsSearchingFor.alpha;

                double alpha = 1, beta = 1, delta = 0; // start here
                double best_alpha= 0, best_beta=1, best_delta=0;
                bool done = false;
                do
                {
                    double achieved_alpha, achieved_beta, achieved_delta;
                    Console.WriteLine("| Trying alpha = {0} delta = {1} beta = {2}", alpha, delta, beta);
                    Console.WriteLine("| So far, best feasible values= alpha {0} delta {1} beta {2}", best_alpha, best_delta, best_beta);

                    achieved_beta =
                        // TestFake(alpha, beta, delta, out achieved_alpha, out achieved_delta); 
                        young_mt.CheckFeasibility(alpha, beta, delta, out achieved_alpha, out achieved_delta);

                    if (achieved_beta < 0)
                        Console.WriteLine("--> X not feasible; so far {0} ms", timer.ElapsedMilliseconds);
                    else
                    {
                        Console.WriteLine("--> Achvd alpha {0} delta {1} beta {2}; so far {3} ms",
                            achieved_alpha, achieved_delta, achieved_beta, timer.ElapsedMilliseconds);

                        if (achieved_beta > 1)
                        {
                            achieved_alpha /= achieved_beta;
                            achieved_delta /= achieved_beta;
                            achieved_beta = 1;
                            Console.WriteLine("---> Scaling to alpha {0} delta {1} beta {2}",
                                achieved_alpha, achieved_delta, achieved_beta);
                        }

                        best_alpha = Math.Max(best_alpha, achieved_alpha);

                        // reset these whenever alpha changes
                        best_delta = 0;
                        best_beta = 1;
                        if (Math.Abs(achieved_alpha - best_alpha) < .01)
                        {
                            best_delta = Math.Max(best_delta, achieved_delta);

                            best_beta = 1; // reset whenever delta changes
                            if (Math.Abs(achieved_delta - best_delta) < .01)
                                best_beta = Math.Min(best_beta, achieved_beta);
                        }
                    }

                    switch (ysf)
                    {
                        case YoungsSearchingFor.alpha:
                            {
                                if (achieved_beta > 0)
                                    alpha_max_feasible = achieved_alpha;
                                else                                
                                    alpha_min_infeasible = alpha;
                                

                                if (alpha_max_feasible >= alpha_min_infeasible ||
                                    alpha_min_infeasible - alpha_max_feasible <= .011) // binary search on alpha is done
                                {
                                    alpha = best_alpha; // freeze to this
                                    if (best_delta >= .99)
                                    {
                                        delta = best_delta;
                                        if (best_beta <= .01)
                                        {
                                            beta = best_beta;
                                            done = true;
                                            break;
                                        }
                                        else
                                        {
                                            Console.WriteLine("| searching for alpha -> searching for beta");
                                            ysf = YoungsSearchingFor.beta;
                                            beta_min_feasible = achieved_beta;
                                            // beta = Math.Min(beta_min_feasible - .01, (beta_min_feasible + beta_max_infeasible) / 2);
                                            beta = Math.Max(beta_min_feasible - .02, (beta_min_feasible + beta_max_infeasible) / 2);
                                            beta = Math.Ceiling(beta * 100) / 100.0;
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("| searching for alpha -> searching for delta");
                                        ysf = YoungsSearchingFor.delta;

                                        delta_max_feasible = achieved_delta;
                                        delta = Math.Max(delta_max_feasible + .01, (delta_min_infeasible + delta_max_feasible) / 2);

                                        delta = Math.Floor(delta * 100) / 100.0;
                                    }
                                }
                                else
                                {
                                    alpha = Math.Max(alpha_max_feasible + .01, (alpha_max_feasible + alpha_min_infeasible) / 2.0);
                                    // sk: we are reaching nearly the best alpha we can ever get to when searching for \alpha=1
                                    // alpha = Math.Min(alpha_max_feasible + .02, (alpha_max_feasible + alpha_min_infeasible) / 2.0);
                                    
                                    alpha = Math.Floor(alpha * 100) / 100.0;
                                }
                            }
                            break;
                        case YoungsSearchingFor.delta:
                            {
                                if (achieved_beta > 0)
                                    delta_max_feasible = achieved_delta;
                                else
                                    delta_min_infeasible = delta;

                                if (delta_max_feasible >= delta_min_infeasible ||
                                    delta_min_infeasible - delta_max_feasible <= .011)
                                {
                                    delta = best_delta; // freeze to this
                                    if (best_beta <= .01)
                                    {
                                        beta = best_beta;
                                        done = true;
                                        break;
                                    }
                                    else
                                    {
                                        Console.WriteLine("| searching for delta -> searching for beta");
                                        ysf = YoungsSearchingFor.beta;
                                        beta_min_feasible = achieved_beta;
                                        // beta = Math.Min(beta_min_feasible - .01, (beta_min_feasible + beta_max_infeasible) / 2);
                                        beta = Math.Max(beta_min_feasible - .02, (beta_min_feasible + beta_max_infeasible) / 2);
                                        beta = Math.Ceiling(beta * 100) / 100.0;
                                    }
                                }
                                else
                                {
                                    delta = Math.Max(delta_max_feasible + .01, (delta_min_infeasible + delta_max_feasible) / 2);

                                    delta = Math.Floor(delta * 100) / 100.0;
                                }
                            }
                            break;
                        case YoungsSearchingFor.beta:
                            {
                                if (achieved_beta > 0)
                                    beta_min_feasible = achieved_beta;
                                else
                                    beta_max_infeasible = beta;

                                if (beta_max_infeasible >= beta_min_feasible ||
                                     beta_min_feasible - beta_max_infeasible <= .011)
                                {
                                    beta = best_beta; // freeze to this
                                    done = true;
                                    break;
                                }
                                else
                                {
                                    // beta = Math.Min(beta_min_feasible - .01, (beta_min_feasible + beta_max_infeasible) / 2);
                                    beta = Math.Max(beta_min_feasible - .02, (beta_min_feasible + beta_max_infeasible) / 2);
                                    beta = Math.Ceiling(beta * 100) / 100.0;
                                }
                            }
                            break;
                        default:
                            break;
                    }

                    if (done)
                        break;

                } while (true);

                timer.Stop();
                Console.WriteLine("The feasible values are alpha {0} delta {1} beta {2}",  best_alpha, best_delta, best_beta);
                Console.WriteLine("Time elapsed is {0}", timer.ElapsedMilliseconds);
                Console.WriteLine("------------End of running young's algorithm---------------");
            }

            if (ra.RunYoungsMt || ra.RunYoungsSt)
            {
                double alpha = 1, epsilon = .2;
       
                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                YoungsAlgorithm young = new YoungsAlgorithm(originalNetwork, edgeCapacities, requests, T, pathDictionary, epsilon, alpha);
                YoungsAlgorithm_mt young_mt =
                    ra.RunYoungsMt ? new YoungsAlgorithm_mt(young, ra.numThreads) : null;

                int low = 0;
                int high = 100;
                int mid = (low + high) / 2;
                double besthit = double.MaxValue;
                double feasible = -1;

                while (low <= high)
                {
                    mid = low + (high - low) / 2;
                    Console.WriteLine("Calling young's algorithm on beta = {0}", mid / 100.0);
                    feasible =
                        ra.RunYoungsMt ?
                        young_mt.CheckFeasibility(mid * 1.0 / 100) :
                        young.CheckFeasibility(mid * 1.0 / 100);
                    ;
                    if (feasible > 0)
                    {
                        //
                        // feasible*100 < mid; guaranteed
                        //
                        besthit = Math.Min(feasible * 100, besthit);

                        //
                        // sometimes feasible > mid
                        // in this case, we still want high to move or we 
                        // will not converge
                        //
                        high = Math.Min((int)(besthit - 1),
                                        mid - 1);
                    }
                    else
                    {
                        low = mid + 1;
                    }
                    Console.WriteLine("--> so far {0}ms", timer.ElapsedMilliseconds);
                }

                timer.Stop();
                Console.WriteLine("The feasible value of beta is {0}", besthit * 1.0 / 100);
                Console.WriteLine("Time elapsed is {0}", timer.ElapsedMilliseconds);
                Console.WriteLine("------------End of running young's algorithm---------------");
            }

        }

        static void Old_Main(string[] args) // Main before SIGCOMM crunch
        {
           //parameters of problem
            RunArgs ra = new RunArgs(args);

           // sk: make this also args at some point
            int nodeCount = 200; // 100; // 25;
            int T = 5000;
            int edgeCount = 1000; // 300; // 100;
            int reqCount = 20000;
            int sampleCap = 10000;

            double alpha = 1;
            double epsilon = 0.2;
            int K = 15; // number of paths between source destination pairs



            NetworkGenerator netGen = new NetworkGenerator();
            BidirectionalGraph<int, Edge<int>> originalNetwork = netGen.generateOriginalNetwork(nodeCount, edgeCount);
            Console.WriteLine("N={0} R={1} T={2} M={3} C={4} alpha={5} epsilon={6} K={7}", 
                nodeCount, reqCount, T, originalNetwork.EdgeCount, sampleCap, alpha, epsilon, K);

            Dictionary<int, double> edgeCapacities = netGen.generateCapacities(originalNetwork, sampleCap);
            List<Request> requests = netGen.generateRequests(originalNetwork, T, reqCount, 80);
            netGen.generateDemands(requests);

            //Construct path library
            Dictionary<int, List<Path>> pathDictionary =
                netGen.ComputePathDictionary(requests, originalNetwork, K);
            netGen.dumpXML(originalNetwork, requests, edgeCapacities, pathDictionary, T);
            //netGen.dumpGAMS(originalNetwork, requests, T, edgeCapacities, alpha);
            //netGen.dumpGAMSPaths(originalNetwork, requests, T, edgeCapacities, alpha, pathDictionary);
            //netGen.dumpGAMSPathsE(originalNetwork, requests, T, edgeCapacities, alpha, pathDictionary);
            netGen.dumpGAMSPathsEb(originalNetwork, requests, T, edgeCapacities, alpha, pathDictionary);

            //netGen.dumpGAMS(originalNetwork, requests, T, edgeCapacities, alpha);

            netGen.dumpGAMSAllPaths(originalNetwork, requests, T, edgeCapacities, alpha);
                
            // [sk] skipping all paths for now
            /*
            GreedyLP_AllPaths g = new GreedyLP_AllPaths();
            g.Solve(originalNetwork, alpha, T, requests, edgeLengths, edgeCapacities);
            Console.ReadKey();

            OptimalLP_AllPaths o = new OptimalLP_AllPaths();
            o.Solve(originalNetwork, alpha, T, requests, edgeLengths, edgeCapacities);
            Console.ReadKey();

            Young_AllPaths young = new Young_AllPaths(edgeLengths, edgeCapacities, requests);
            System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            timer.Start();


            int low = 0;
            int high = 100;
            int mid = (low + high) / 2;
            int lasthit = 0;
            bool feasible = false;

            while (low <= high)
            {
                mid = low + (high - low) / 2;
                Console.WriteLine("Calling young's algorithm on beta = {0}", mid / 100.0);
                feasible = young.Run(originalNetwork, alpha, mid * 1.0 / 100, T, epsilon);
                if (feasible == true)
                {
                    lasthit = mid;
                    Console.WriteLine("Lasthit is {0}", lasthit);
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }

            Console.WriteLine("The value of last hit is {0}\n");
            */


            if (ra.RunGreedy)
            {
                GreedyLP greedyLp = new GreedyLP();
                double greedyBeta = greedyLp.Solve(originalNetwork, alpha, T, requests, null, edgeCapacities, pathDictionary);
                Console.WriteLine("The value of greedy beta is {0}", greedyBeta);
                Console.WriteLine("------------End of Greedy LP algorithm---------------------");
            }

            if (ra.RunOptimal)
            {
                OptimalLP optimalLp = new OptimalLP();
                double optimalBeta = optimalLp.Solve(originalNetwork, alpha, T, requests, null,
                    edgeCapacities, pathDictionary);
                Console.WriteLine("The value of optimal beta is {0}", optimalBeta);
                Console.WriteLine("-----------End of Optimal LP algorithm---------------------");
            }

            if (ra.RunYoungsMt || ra.RunYoungsSt)
            {
                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                YoungsAlgorithm young = new YoungsAlgorithm(originalNetwork, edgeCapacities, requests, T, pathDictionary, epsilon, alpha);
                YoungsAlgorithm_mt young_mt =
                    ra.RunYoungsMt ? new YoungsAlgorithm_mt(young, ra.numThreads) : null;

                int low = 0;
                int high = 100;
                int mid = (low + high) / 2;
                double besthit = double.MaxValue;
                double feasible = -1;

                while (low <= high)
                {
                    mid = low + (high - low) / 2;
                    Console.WriteLine("Calling young's algorithm on beta = {0}", mid / 100.0);
                    feasible =
                        ra.RunYoungsMt ?
                        young_mt.CheckFeasibility(mid * 1.0 / 100) :
                        young.CheckFeasibility(mid * 1.0 / 100);
                    ;
                    if (feasible > 0)
                    {
                        //
                        // feasible*100 < mid; guaranteed
                        //
                        besthit = Math.Min(feasible * 100, besthit);

                        //
                        // sometimes feasible > mid
                        // in this case, we still want high to move or we 
                        // will not converge
                        //
                        high = Math.Min((int)(besthit - 1),
                                        mid - 1);
                    }
                    else
                    {
                        low = mid + 1;
                    }
                    Console.WriteLine("--> so far {0}ms", timer.ElapsedMilliseconds);
                }

                timer.Stop();
                Console.WriteLine("The feasible value of beta is {0}", besthit * 1.0 / 100);
                Console.WriteLine("Time elapsed is {0}", timer.ElapsedMilliseconds);
                Console.WriteLine("------------End of running young's algorithm---------------");
            }
        }
    //    public static void SearchForBest(
    //        S14_online_YoungsAlgorithm_mt y,
    //        int currTimestep,
    //        double alpha, double delta, double beta, // initial values
    //        double epsilon,
    //        out double best_alpha, out double best_delta, out double best_beta // converged values
    //        )
    //    {
    //        System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
    //        timer.Start();

    //        double
    //alpha_max_feasible = 0, alpha_min_infeasible = 1,
    //beta_max_infeasible = 0, beta_min_feasible = 1,
    //delta_max_feasible = 0, delta_min_infeasible = 1;

    //        YoungsSearchingFor ysf = YoungsSearchingFor.alpha;

    //        best_alpha = 0;
    //        best_beta = 1;
    //        best_delta = 0;

    //        bool done = false;
    //        do
    //        {
    //            double achieved_alpha, achieved_beta, achieved_delta;
    //            Console.WriteLine("| Trying alpha = {0} delta = {1} beta = {2}", alpha, delta, beta);
    //            Console.WriteLine("| So far, best feasible values= alpha {0} delta {1} beta {2}", best_alpha, best_delta, best_beta);

    //            achieved_beta =
    //                y.CheckFeasibility(currTimestep, alpha, beta, delta, out achieved_alpha, out achieved_delta);

    //            if (achieved_beta < 0)
    //                Console.WriteLine("--> X not feasible; so far {0} ms", timer.ElapsedMilliseconds);
    //            else
    //            {
    //                Console.WriteLine("--> Achvd alpha {0} delta {1} beta {2}; so far {3} ms",
    //                    achieved_alpha, achieved_delta, achieved_beta, timer.ElapsedMilliseconds);

    //                // reset these whenever alpha changes
    //                best_delta = 0;
    //                best_beta = 1;
    //                if (Math.Abs(achieved_alpha - best_alpha) < .01)
    //                {
    //                    best_delta = Math.Max(best_delta, achieved_delta);

    //                    best_beta = 1; // reset whenever delta changes
    //                    if (Math.Abs(achieved_delta - best_delta) < .01)
    //                        best_beta = Math.Min(best_beta, achieved_beta);
    //                }
    //            }

    //            switch (ysf)
    //            {
    //                case YoungsSearchingFor.alpha:
    //                    {
    //                        if (achieved_beta > 0)
    //                            alpha_max_feasible = achieved_alpha;
    //                        else
    //                            alpha_min_infeasible = alpha;


    //                        if (alpha_max_feasible >= alpha_min_infeasible ||
    //                            alpha_min_infeasible - alpha_max_feasible <= .011) // binary search on alpha is done
    //                        {
    //                            alpha = best_alpha; // freeze to this
    //                            if (best_delta >= .99)
    //                            {
    //                                delta = best_delta;
    //                                if (best_beta <= .01)
    //                                {
    //                                    beta = best_beta;
    //                                    done = true;
    //                                    break;
    //                                }
    //                                else
    //                                {
    //                                    Console.WriteLine("| searching for alpha -> searching for beta");
    //                                    ysf = YoungsSearchingFor.beta;
    //                                    beta_min_feasible = achieved_beta;
    //                                    // beta = Math.Min(beta_min_feasible - .01, (beta_min_feasible + beta_max_infeasible) / 2);
    //                                    beta = Math.Max(beta_min_feasible - .02, (beta_min_feasible + beta_max_infeasible) / 2);
    //                                    beta = Math.Ceiling(beta * 100) / 100.0;
    //                                }
    //                            }
    //                            else
    //                            {
    //                                Console.WriteLine("| searching for alpha -> searching for delta");
    //                                ysf = YoungsSearchingFor.delta;

    //                                delta_max_feasible = achieved_delta;
    //                                delta = Math.Max(delta_max_feasible + .01, (delta_min_infeasible + delta_max_feasible) / 2);

    //                                delta = Math.Floor(delta * 100) / 100.0;
    //                            }
    //                        }
    //                        else
    //                        {
    //                            // alpha = Math.Max(alpha_max_feasible + .01, (alpha_max_feasible + alpha_min_infeasible) / 2.0);
    //                            // sk: we are reaching nearly the best alpha we can ever get to when searching for \alpha=1
    //                            alpha = Math.Min(alpha_max_feasible + .02, (alpha_max_feasible + alpha_min_infeasible) / 2.0);

    //                            alpha = Math.Floor(alpha * 100) / 100.0;
    //                        }
    //                    }
    //                    break;
    //                case YoungsSearchingFor.delta:
    //                    {
    //                        if (achieved_beta > 0)
    //                            delta_max_feasible = achieved_delta;
    //                        else
    //                            delta_min_infeasible = delta;

    //                        if (delta_max_feasible >= delta_min_infeasible ||
    //                            delta_min_infeasible - delta_max_feasible <= .011)
    //                        {
    //                            delta = best_delta; // freeze to this
    //                            if (best_beta <= .01)
    //                            {
    //                                beta = best_beta;
    //                                done = true;
    //                                break;
    //                            }
    //                            else
    //                            {
    //                                Console.WriteLine("| searching for delta -> searching for beta");
    //                                ysf = YoungsSearchingFor.beta;
    //                                beta_min_feasible = achieved_beta;
    //                                // beta = Math.Min(beta_min_feasible - .01, (beta_min_feasible + beta_max_infeasible) / 2);
    //                                beta = Math.Max(beta_min_feasible - .02, (beta_min_feasible + beta_max_infeasible) / 2);
    //                                beta = Math.Ceiling(beta * 100) / 100.0;
    //                            }
    //                        }
    //                        else
    //                        {
    //                            delta = Math.Max(delta_max_feasible + .01, (delta_min_infeasible + delta_max_feasible) / 2);

    //                            delta = Math.Floor(delta * 100) / 100.0;
    //                        }
    //                    }
    //                    break;
    //                case YoungsSearchingFor.beta:
    //                    {
    //                        if (achieved_beta > 0)
    //                            beta_min_feasible = achieved_beta;
    //                        else
    //                            beta_max_infeasible = beta;

    //                        if (beta_max_infeasible >= beta_min_feasible ||
    //                             beta_min_feasible - beta_max_infeasible <= .011)
    //                        {
    //                            beta = best_beta; // freeze to this
    //                            done = true;
    //                            break;
    //                        }
    //                        else
    //                        {
    //                            // beta = Math.Min(beta_min_feasible - .01, (beta_min_feasible + beta_max_infeasible) / 2);
    //                            beta = Math.Max(beta_min_feasible - .02, (beta_min_feasible + beta_max_infeasible) / 2);
    //                            beta = Math.Ceiling(beta * 100) / 100.0;
    //                        }
    //                    }
    //                    break;
    //                default:
    //                    break;
    //            }

    //            if (done)
    //                break;

    //        } while (true);

    //        timer.Stop();
    //        Console.WriteLine(
    //            "The feasible values are alpha {0} delta {1} beta {2}", 
    //            best_alpha, best_delta, best_beta);
    //        Console.WriteLine("Time elapsed is {0}", timer.ElapsedMilliseconds);
    //    }
    }


}
