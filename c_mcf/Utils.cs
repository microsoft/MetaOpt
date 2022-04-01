using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using System.Diagnostics;

namespace MaxConcurrentFlow
{

    public class MeanStdNum
    {
        public double mean, std, meanSquare, minVal, maxVal;
        public int numSamples;
        List<double> samples;
        // public int multiplicativeFactor;

        bool isSortNeeded;

        public MeanStdNum()
        {
            mean = 0;
            meanSquare = 0;
            std = 0;
            numSamples = 0;

            minVal = 0;
            maxVal = 0;

            samples = new List<double>();
            isSortNeeded = false;
        }

        public void AddSample(double d)
        {
            isSortNeeded = true;
            if (numSamples == 0)
            {
                minVal = d;
                maxVal = d;
            }
            else
            {
                minVal = (minVal > d) ? d : minVal;
                maxVal = (maxVal < d) ? d : maxVal;
            }

            //// if value is higher, just push it down
            //while (d / multiplicativeFactor > 100000)
            //{
            //  multiplicativeFactor *= 10;
            //  mean /= 10;
            //  mean_square /= 10;
            //}

            //// internal values are at multiplicativeFactor
            //d = d / multiplicativeFactor;

            samples.Add(d);
            numSamples++;
            mean += (d - mean) / (double)numSamples;
            meanSquare += ((d * d) - meanSquare) / (double)numSamples;
            std = Math.Sqrt(meanSquare - (mean * mean));
        }

        public void AddSamples(List<double> valList)
        {
            isSortNeeded = true;
            foreach (double val in valList)
            {
                AddSample(val);
            }
        }

        public double GetPercentile(double x)
        {
            Debug.Assert(x <= 1 && x >= 0);
            if (isSortNeeded)
            {
                samples.Sort();
                isSortNeeded = false;
            }

            if (samples.Count == 0)
                return double.NaN;
            
            int sampleCount = (int)(x * samples.Count);
            if (x == samples.Count) x -= 1;
            return samples[sampleCount];
        }

        public double GetMedian()
        {
            if (isSortNeeded)
            {
                samples.Sort();
                isSortNeeded = false;
            }

            if (samples.Count == 0)
            {
                return 0;
            }

            if (samples.Count % 2 == 1)
            {
                return samples[samples.Count / 2];
            }

            return (samples[samples.Count / 2] + samples[(samples.Count / 2) - 1]) / 2;
        }

        public void MergeInputMeanStdNum(MeanStdNum inputStats)
        {
            isSortNeeded = true;
            this.AddSamples(inputStats.samples);
        }

        public string GetMinMeanMaxStdMedianString()
        {
            return String.Format("{0} {1:F4} {2:F4} {3:F4} {4:F4} {5:F5}", numSamples, minVal, mean, maxVal, std, GetMedian());
        }

        public string GetDetailedString()
        {
            return String.Format("numS {0} min {1:F4} 10th {2:F4} 25th {3:F4} 50th {4:F4} 75th {5:F4} 90th {6:F4} max {7:F4} mean {8:F4} std {9:F4}",
                numSamples, minVal, GetPercentile(.1), GetPercentile(.25), GetPercentile(.5), GetPercentile(.75), GetPercentile(.9), maxVal, 
                mean,
                std
                );
        }

        public override string ToString()
        {
            return string.Format("num {0} mean {1:F4}, std {2:F4}",
                                 numSamples, mean, std);
        }
    }

    public class MeanStdNumSimple
    {
        public double Mean, Std, mean_square;
        public int numSamples;
        // public int multiplicativeFactor;

        public MeanStdNumSimple()
        {
            Mean = 0;
            mean_square = 0;
            Std = 0;
            numSamples = 0;
        }
        public void addSample(double d)
        {

            //// if value is higher, just push it down
            //while (d / multiplicativeFactor > 100000)
            //{
            //  multiplicativeFactor *= 10;
            //  mean /= 10;
            //  mean_square /= 10;
            //}

            //// internal values are at multiplicativeFactor
            //d = d / multiplicativeFactor;

            numSamples++;
            Mean += (d - Mean) / (double)numSamples;
            mean_square += ((d * d) - mean_square) / (double)numSamples;
            Std = Math.Sqrt(mean_square - (Mean * Mean));
        }

        public override string ToString()
        {
            return string.Format("mean {0}, std {1}, num {2}",
                                 Mean, Std, numSamples);
        }
    }

    public class StandAloneLogger
    {
        TextWriter logFile = null;
        public int LogIndent = 0;

        public void openLog(String fname)
        {
            logFile = new StreamWriter(fname, true);
        }

        public void openNewLog(String fname)
        {
            logFile = new StreamWriter(fname, false);
        }

        public void ReplaceLog(String fname)
        {
            logFile.Close();
            logFile = new StreamWriter(fname, false);
        }

        public void CloseLog()
        {
            logFile.Close();
        }

        public void log(String format, params object[] args)
        {
            log(false, format, args);
        }

        public void log(bool suppressTime, String format, params object[] args)
        {
            try
            {
                if (LogIndent > 0)
                {
                    System.Diagnostics.Debug.Assert(LogIndent >= 0);
                    for (int i = 0; i < LogIndent; ++i)
                    {
                        logFile.Write("  ");
                    }
                }
                if (!suppressTime)
                    logFile.Write("{0:u} ", DateTime.Now);
                logFile.WriteLine(format, args);
                logFile.Flush();
            }
            catch (Exception e)
            {
                // Console.WriteLine("Failed to log: " + e + e.StackTrace);
                Console.Error.WriteLine("Failed to log: format: " + format + " exception is " + e + " stack " + e.StackTrace);
            }
        }
    }

    public class Logger
    {
        public static StandAloneLogger globalLogger = null;
        public static void openLog(String fname)
        {
            globalLogger = new StandAloneLogger();
            globalLogger.openLog(fname);
        }

        public static void openNewLog(String fname)
        {
            globalLogger = new StandAloneLogger();
            globalLogger.openNewLog(fname);
        }

        public static void ReplaceLog(String fname)
        {
            globalLogger.ReplaceLog(fname);
        }

        public static void CloseLog()
        {
            globalLogger.CloseLog();
        }

        public static void log(String format, params object[] args)
        {
            globalLogger.log(false, format, args);
        }

        public static void log(bool suppressTime, String format, params object[] args)
        {
            globalLogger.log(suppressTime, format, args);
        }
    }

}
