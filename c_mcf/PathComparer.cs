using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaxConcurrentFlow
{
    class PathComparer:IComparer<Path>
    {
        public int Compare(Path x, Path y)
        {
            if (x.length < y.length)
                return -1;
            else if (x.length > y.length)
                return 1;
            else
                return x.length.CompareTo(y.length);

        }
    }
}
