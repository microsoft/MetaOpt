// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Gurobi;
    class GurobiCallback : GRBCallback
    {
        private bool storeProgressEnabled = false;
        private GurobiStoreProgressCallback storeProgressCallback;
        private bool terminationCallbackEnabled = false;
        private GurobiTerminationCallback terminationCallback;
        private bool timeoutCallbackEnabled = false;
        private GurobiTimeoutCallback timeoutCallback;
        // double presolvetime_ms = -1;
        // Stopwatch presolvetimer = null;

        public GurobiCallback(
            GRBModel model,
            bool storeProgress = false,
            String dirname = null,
            String filename = null,
            double terminateNoImprovement_ms = -1,
            double timeout = 0)
        {
            if (storeProgress) {
                this.storeProgressEnabled = true;
                this.storeProgressCallback = new GurobiStoreProgressCallback(model, dirname, filename);
            }
            if (terminateNoImprovement_ms > 0) {
                this.terminationCallbackEnabled = true;
                this.terminationCallback = new GurobiTerminationCallback(model, terminateNoImprovement_ms);
            }
            if (timeout > 0) {
                this.timeoutCallbackEnabled = true;
                this.timeoutCallback = new GurobiTimeoutCallback(model, timeout);
            }
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIP) {
                    // if (this.presolvetime_ms <= 0) {
                    //     this.presolvetime_ms = this.presolvetimer.ElapsedMilliseconds;
                    //     this.presolvetimer.Stop();
                    //     Console.WriteLine("presolve timer=", presolvetime_ms);
                    // }
                    var obj = GetDoubleInfo(GRB.Callback.MIP_OBJBST);
                    if (this.storeProgressEnabled) {
                        this.storeProgressCallback.CallCallback(obj);
                    }
                    if (this.terminationCallbackEnabled) {
                        this.terminationCallback.CallCallback(obj);
                    }
                    if (this.timeoutCallbackEnabled) {
                        this.timeoutCallback.CallCallback();
                    }
                }
                // else if (where == GRB.Callback.PRESOLVE) {
                //     if (this.presolvetimer == null) {
                //         this.presolvetimer = Stopwatch.StartNew();
                //     }
                // }
            } catch (GRBException e) {
                Console.WriteLine("Error code: " + e.ErrorCode);
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            } catch (Exception e) {
                Console.WriteLine("Error during callback");
                Console.WriteLine(e.StackTrace);
            }
        }

        private void ResetTermination()
        {
            if (this.terminationCallbackEnabled) {
                this.terminationCallback.ResetTermination();
            }
        }

        private void ResetProgressTimer()
        {
            if (this.storeProgressEnabled) {
                this.storeProgressCallback.ResetProgressTimer();
            }
        }

        private void ResetTimeout()
        {
            if (this.timeoutCallbackEnabled) {
                this.timeoutCallback.ResetTermination();
            }
        }

        public void ResetAll()
        {
            // this.presolvetime_ms = -1;
            // this.presolvetimer = null;
            this.ResetProgressTimer();
            this.ResetTermination();
            this.ResetTimeout();
        }
    }
}