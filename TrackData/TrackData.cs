using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClubmanSharp.TrackData
{
    public struct Segment
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

    public abstract class TrackDataBase
    {
        public bool _useInitialSegments = true;
        public virtual Segment[] initialsegments { get; } = Array.Empty<Segment>();
        public virtual Segment[] segments { get; } = Array.Empty<Segment>();

        public void NewRace()
        {
            _useInitialSegments = true;
        }

        public(double, double) GetTargets(float x, float z, int lap)
        {
            var ix = (int)x;
            var iz = (int)z;
            var segmentNum = 0;

            if (_useInitialSegments)
            {
                foreach (Segment segment in initialsegments)
                {
                    segmentNum++;
                    if (ix >= segment.minX && ix <= segment.maxX &&
                    iz >= segment.minZ && iz <= segment.maxZ)
                    {
                        //Trace.WriteLine($"seg: {segmentNum}");
                        return (segment.mph, segment.heading);
                    }
                }

                segmentNum = 0;
                // we've left the initial segment area, so switch to regular segments
                _useInitialSegments = false;

                //Trace.WriteLine("Left initial segments.");
            }

            foreach (Segment segment in segments)
            {
                segmentNum++;
                if (ix >= segment.minX && ix <= segment.maxX &&
                    iz >= segment.minZ && iz <= segment.maxZ)
                {
                    //Trace.WriteLine($"seg: {segmentNum}");
                    return (segment.mph, segment.heading);
                }
            }

            return (30.0, 360.0);
        }
    }
}
