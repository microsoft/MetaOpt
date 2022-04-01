using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MaxConcurrentFlow
{
    class TPComparer : IComparer<Tuple<int, double>>
    {
        public int Compare(Tuple<int, double> a, Tuple<int, double> b)
        {
            
            if (a.Item2 < b.Item2)
                return -1;
            else if (a.Item2 > b.Item2)
                return 1;
            else
                return a.Item1.CompareTo(b.Item1);
        
        }
    }
}
