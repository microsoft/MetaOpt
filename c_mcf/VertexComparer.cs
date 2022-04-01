using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaxConcurrentFlow
{
    class VertexComparer:IComparer<int>
    {
        //Private member
        private Dictionary<int, double> dist;


        //Ctor
        public VertexComparer(Dictionary<int, double> dist)
        {
            this.dist = dist;
        }

        public double getDistance(int a)
        {
            return dist[a];
        }
        public int Compare(int x, int y)
        {
            double distX = dist[x];
            double distY = dist[y];

            if (distX < distY) return -1;
            if (distX > distY) return 1;

            return x.CompareTo(y);
           
        }
    }
}
