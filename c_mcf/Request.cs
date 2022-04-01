using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace MaxConcurrentFlow
{
    public class Request
    {
        public int src;
        public int dest;
        public int arrival;
        public int deadline;
        public double demand;
        public int awareTime;

        public Request(int s, int t, int a, int d, double dem, int aware)
        {
            src = s;
            dest = t;
            arrival = a;
            deadline = d;
            demand = dem;
            awareTime = aware;
        }
        //ctor
        public Request(int s, int t, int a, int d, double dem)
        {
            src = s;
            dest = t;
            arrival = a;
            deadline = d;
            demand = dem;
            awareTime = a;
        }
        public override string ToString()
        {
            return String.Format("R nodes {0} -> {1} times {2} : {3} dem {4} aware@ {5}",
                src,
                dest,
                arrival,
                deadline,
                demand,
                awareTime);
        }
        public Request(string s)
        {
            Regex whitespaces = new Regex(@"\s+");
            string[] vals = whitespaces.Split(s);

            try
            {
                Debug.Assert(vals.Length >= 11);
                src = int.Parse(vals[2]);
                dest = int.Parse(vals[4]);
                arrival = int.Parse(vals[6]);
                deadline = int.Parse(vals[8]);
                demand = double.Parse(vals[10]);

                awareTime = (vals.Length >= 13) ? int.Parse(vals[12]) : arrival;
            }
            catch (Exception e)
            {
                Console.WriteLine("Err when parsing string [{0}] = {1}", s, e);
            }

        }
    }
    public class RequestComparerOnAwareTime : IComparer<Request>
    {
        public int Compare(Request a, Request b)
        {
            return Math.Sign(a.awareTime - b.awareTime);
        }
    }
}
