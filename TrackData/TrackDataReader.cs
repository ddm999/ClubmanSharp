using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace ClubmanSharp.TrackData
{
    public class TrackDataFromFile : TrackDataBase
    {
        public new Segment[] initialsegments = Array.Empty<Segment>();
        public new Segment[] segments = Array.Empty<Segment>();
        public new Segment pitbox = new();
        public new double nos_speedlimit = 150;

        public new (double, double) GetTargets(float x, float z, int lap)
        {
            var ix = (int)x;
            var iz = (int)z;

            segmentNum = 0;

            if (ix >= pitbox.minX && ix <= pitbox.maxX &&
                iz >= pitbox.minZ && iz <= pitbox.maxZ)
            {
                pitboxCounter += 1;
                if (pitboxCounter >= 25)
                {
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
                        return (segment.mph, segment.heading);
                    }
                }

                segmentNum = 0;
                useInitialSegments = false;
            }

            foreach (Segment segment in segments)
            {
                segmentNum++;
                if (ix >= segment.minX && ix <= segment.maxX &&
                    iz >= segment.minZ && iz <= segment.maxZ)
                {
                    return (segment.mph, segment.heading);
                }
            }

            return (30.0, 360.0);
        }
    }

    public class TrackDataReader
    {
        public static TrackDataFromFile ReadFromFile(string filename)
        {
            var lines = File.ReadAllLines(filename);

            List<Segment> initialsegments = new();
            List<Segment> segments = new();
            Segment pitbox = new(0,0,0,0,0,0);
            double nos_speedlimit = 150;
            int lineno = 0;

            foreach (string line in lines)
            {
                var l = line.Trim();
                lineno++;

                // comment
                if (l.StartsWith("#") || l.Length == 0)
                {
                    continue;
                }

                var vals = l.Split(",");

                if (vals[0] == "initial")
                {
                    if (vals.Length != 7)
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} has bad entry count. Expected 7, got {vals.Length}.");
                    }

                    try
                    {
                        var seg = new Segment
                        {
                            minX = int.Parse(vals[1]),
                            minZ = int.Parse(vals[2]),
                            maxX = int.Parse(vals[3]),
                            maxZ = int.Parse(vals[4]),
                            heading = double.Parse(vals[5]),
                            mph = double.Parse(vals[6])
                        };

                        initialsegments.Add(seg);
                    }
                    catch
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} contains values that couldn't be parsed.");
                    }
                }
                else if (vals[0] == "main")
                {
                    if (vals.Length != 7)
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} has bad entry count. Expected 7, got {vals.Length}.");
                    }

                    try
                    {
                        var seg = new Segment
                        {
                            minX = int.Parse(vals[1]),
                            minZ = int.Parse(vals[2]),
                            maxX = int.Parse(vals[3]),
                            maxZ = int.Parse(vals[4]),
                            heading = double.Parse(vals[5]),
                            mph = double.Parse(vals[6])
                        };

                        segments.Add(seg);
                    }
                    catch
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} contains values that couldn't be parsed.");
                    }
                }
                else if (vals[0] == "pitbox")
                {
                    if (vals.Length != 7)
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} has bad entry count. Expected 7, got {vals.Length}.");
                    }

                    try
                    {
                        var seg = new Segment
                        {
                            minX = int.Parse(vals[1]),
                            minZ = int.Parse(vals[2]),
                            maxX = int.Parse(vals[3]),
                            maxZ = int.Parse(vals[4]),
                            heading = double.Parse(vals[5]),
                            mph = double.Parse(vals[6])
                        };

                        pitbox = seg;
                    }
                    catch
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} contains values that couldn't be parsed.");
                    }
                }
                else if (vals[0] == "nos")
                {
                    if (vals.Length != 2)
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} has bad entry count. Expected 7, got {vals.Length}.");
                    }

                    try
                    {
                        nos_speedlimit = double.Parse(vals[1]);
                    }
                    catch
                    {
                        throw new InvalidDataException($"TrackData file invalid: line {lineno} contains values that couldn't be parsed.");
                    }
                }
                else
                {
                    throw new InvalidDataException($"TrackData file invalid: line {lineno} is an unknown type.");
                }
            }

            TrackDataFromFile td = new()
            {
                segments = segments.ToArray(),
                initialsegments = initialsegments.ToArray(),
                pitbox = pitbox,
                nos_speedlimit = nos_speedlimit
            };
            return td;
        }
    }
}
