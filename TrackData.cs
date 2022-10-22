using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClubmanSharp
{
    struct Segment
    {
        public int minX;
        public int minZ;
        public int maxX;
        public int maxZ;
        public double heading;
        public double mph;

        public Segment(int minX, int minZ, int maxX, int maxZ, double heading, double mph)
        {
            this.minX = minX;
            this.minZ = minZ;
            this.maxX = maxX;
            this.maxZ = maxZ;
            this.heading = heading;
            this.mph = mph;
        }
    }

    public class TrackData
    {
        static readonly Segment[] segments =
        {
          new Segment( 160,  775,  195,  875,  -70.0, 300.0),
          new Segment(  66,  775,  160,  875,  -46.0, 300.0),
          new Segment(-120,  775,   66,  875,  -44.0, 300.0),
          new Segment(-240,  750, -120,  850,  -33.0, 300.0),
          new Segment(-344,  725, -240,  825,  -42.5, 300.0),
          new Segment(-430,  700, -344,  800,  -47.0, 300.0),
          new Segment(-450,  675, -430,  775,  -49.0, 300.0), // T1 entry
          new Segment(-556,  650, -450,  750,   -3.2, 300.0), // T1
          new Segment(-650,  560, -556,  725,   -3.5, 300.0),
          new Segment(-675,  485, -575,  560,   18.0, 300.0), // T2 entry
          new Segment(-675,  400, -575,  485,   40.0, 300.0), // T2
          new Segment(-650,  385, -545,  400,    5.0, 300.0),
          new Segment(-650,  200, -545,  385,   25.0, 155.0), // T3
          new Segment(-545,  200, -517,  330,   15.0, 145.0),
          new Segment(-517,  160, -400,  300,    2.0, 155.0), // T4
          new Segment(-550,   93, -350,  160,    1.0, 300.0),
          new Segment(-425, -158, -325,   93,    5.9, 300.0),
          new Segment(-325, -270, -225,  -75,    2.8, 300.0),
          new Segment(-275, -372, -175, -270,    2.5, 300.0), // T5 entry
          new Segment(-250, -550,  -59, -372,   45.0, 300.0), // T5
          new Segment( -59, -550,  200, -450,   50.0, 300.0),
          new Segment( 200, -550,  324, -450,   55.0, 300.0),
          new Segment( 324, -550,  507, -450,   62.0, 300.0),
          new Segment( 507, -550,  566, -450,   62.0, 300.0), // T6 entry
          new Segment( 566, -500,  700, -403,  136.0, 300.0), // T6
          new Segment( 650, -403,  750, -320,  150.0, 300.0),
          new Segment( 675, -320,  775, -261,  168.0, 300.0),
          new Segment( 650, -261,  750, -150, -100.0, 300.0), // T7
          new Segment( 650, -150,  750,  -75, -155.0, 105.0),
          new Segment( 575, -145,  650,  -52, -155.0, 105.0), // T8
          new Segment( 575,  -52,  650,    4, -110.0, 105.0), 
          new Segment( 475,    4,  650,   95,  -75.0, 300.0), // T9 & T10
          new Segment( 425,   95,  525,  110,  -85.0, 300.0),
          new Segment( 350,  110,  475,  160, -150.0, 300.0), // T11
          new Segment( 300,  160,  425,  222, -130.0, 300.0),
          new Segment( 150,  222,  375,  430, -127.5, 300.0),
          new Segment( 150,  430,  250,  540,  135.0, 300.0), // T12
          new Segment( 150,  540,  250,  575,  135.0, 300.0),
          new Segment( 175,  575,  275,  660,  175.0, 300.0), // T13
          new Segment( 200,  660,  300,  680,  179.0, 300.0),
          new Segment( 200,  680,  300,  710, -171.0, 300.0), // T15 entry
          new Segment( 195,  710,  275,  850,  -95.0, 300.0), // T15
        };

        internal static (double, double) GetTargets(float x, float z)
        {
            var ix = (int)x;
            var iz = (int)z;
            var segmentnum = 0;
            foreach (Segment segment in segments)
            {
                segmentnum++;
                if (ix >= segment.minX && ix <= segment.maxX &&
                    iz >= segment.minZ && iz <= segment.maxZ)
                {
                    //Trace.WriteLine($"In segment {segmentnum}: ({segment.heading}, {segment.mph})");

                    return (segment.mph, segment.heading);
                }
            }

            return (30.0, 360.0);
        }
    }
}
