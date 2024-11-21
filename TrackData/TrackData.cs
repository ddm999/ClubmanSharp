using System;

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
        public bool useInitialSegments = true;
        public int segmentNum = 0;
        public int pitboxCounter = 0;
        public virtual Segment[] initialsegments { get; } = Array.Empty<Segment>();
        public virtual Segment[] segments { get; } = Array.Empty<Segment>();
        public virtual Segment pitbox { get; } = new();

        public void NewRace()
        {
            DebugLog.Log($"TrackData NewRace", LogType.Driv);
            useInitialSegments = true;
        }

        public(double, double) GetTargets(float x, float z, int lap)
        {
            DebugLog.Log($"TrackData GetTargets(x={x}, z={z}, lap={lap})", LogType.Driv);
            var ix = (int)x;
            var iz = (int)z;

            segmentNum = 0;

            if (ix >= pitbox.minX && ix <= pitbox.maxX &&
                iz >= pitbox.minZ && iz <= pitbox.maxZ)
            {
                DebugLog.Log($"TrackData GetTargets in pitbox region!!", LogType.Driv);
                pitboxCounter += 1;
                if (pitboxCounter >= 25)
                {
                    DebugLog.Log($"TrackData GetTargets PITBOX COUNTER HIT!!", LogType.Driv);
                    segmentNum = -1;
                    return (-1.0, -1.0);
                }
            }
            else
            {
                pitboxCounter = 0;
            }

            if (useInitialSegments)
            {
                foreach (Segment segment in initialsegments)
                {
                    segmentNum++;
                    if (ix >= segment.minX && ix <= segment.maxX &&
                    iz >= segment.minZ && iz <= segment.maxZ)
                    {
                        DebugLog.Log($"TrackData INITIAL segment={segmentNum}", LogType.Driv);
                        return (segment.mph, segment.heading);
                    }
                }

                segmentNum = 0;
                // we've left the initial segment area, so switch to regular segments
                useInitialSegments = false;

                DebugLog.Log($"TrackData Left initial segments", LogType.Driv);
            }

            foreach (Segment segment in segments)
            {
                segmentNum++;
                if (ix >= segment.minX && ix <= segment.maxX &&
                    iz >= segment.minZ && iz <= segment.maxZ)
                {
                    DebugLog.Log($"TrackData segment={segmentNum}", LogType.Driv);
                    return (segment.mph, segment.heading);
                }
            }

            DebugLog.Log($"TrackData NO SEGMENT FOUND!!", LogType.Driv);
            return (30.0, 360.0);
        }
    }
}
