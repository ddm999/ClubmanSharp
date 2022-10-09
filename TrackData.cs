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
          new Segment(-115,  775,   66,  875,  -44.0, 300.0),
          new Segment(-240,  750, -115,  850,  -33.0, 300.0),
          new Segment(-344,  725, -240,  825,  -42.5, 300.0),
          new Segment(-430,  700, -344,  800,  -47.0, 300.0),
          new Segment(-450,  675, -430,  775,  -49.0, 300.0), // T1 entry
          new Segment(-556,  650, -450,  750,   -3.0, 300.0), // T1
          new Segment(-650,  546, -556,  725,   -3.5, 300.0),
          new Segment(-675,  460, -575,  546,    2.0, 300.0), // T2 entry | segment 10
          new Segment(-675,  365, -575,  460,    8.0, 300.0), // T2
          new Segment(-650,  200, -530,  365,   20.0, 128.0), // T3
          new Segment(-530,  160, -400,  300,    5.0, 140.0), // T4
          new Segment(-550,   93, -350,  160,    1.0, 300.0),
          new Segment(-425, -158, -325,   93,    5.8, 300.0),
          new Segment(-325, -270, -225,  -75,    3.0, 300.0),
          new Segment(-275, -372, -175, -270,    2.0, 300.0), // T5 entry
          new Segment(-250, -550,  -59, -372,   45.0, 300.0), // T5
          new Segment( -59, -550,  186, -450,   50.0, 300.0),
          new Segment( 186, -550,  324, -450,   55.5, 300.0), // segment 20
          new Segment( 324, -550,  507, -450,   62.5, 300.0),
          new Segment( 507, -550,  566, -450,   60.0, 300.0), // T6 entry
          new Segment( 566, -500,  700, -403,  136.0, 300.0), // T6
          new Segment( 650, -403,  750, -300,  150.0, 300.0),
          new Segment( 675, -300,  775, -241,  168.0, 300.0),
          new Segment( 650, -241,  750, -145, -100.0, 300.0), // T7
          new Segment( 650, -145,  691,  -75, -150.0,  90.0),
          new Segment( 575, -145,  650,  -43, -155.0,  90.0), // T8
          new Segment( 575,  -43,  650,    4, -110.0,  90.0), 
          new Segment( 475,    4,  650,   90,  -70.0, 300.0), // T9 & T10 | segment 30
          new Segment( 425,   90,  525,  120,  -85.0, 300.0),
          new Segment( 350,  120,  475,  160, -100.0, 300.0), // T11
          new Segment( 300,  160,  425,  222, -130.0, 300.0),
          new Segment( 175,  222,  375,  410, -127.5, 300.0),
          new Segment( 150,  410,  250,  430, -125.0, 300.0), // T12 entry
          new Segment( 150,  430,  250,  540,  135.0, 300.0), // T12
          new Segment( 150,  540,  250,  570,  135.0, 300.0),
          new Segment( 175,  570,  275,  675,  175.0, 300.0), // T13
          new Segment( 200,  675,  300,  700,  179.0, 300.0),
          new Segment( 200,  700,  300,  710, -171.0, 300.0), // T15 entry | segment 40
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
