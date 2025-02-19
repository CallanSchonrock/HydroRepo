using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata.Ecma335;

namespace Mydro
{
    public class GlobalVariables
    {
        public static double dt = 900; // Seconds
        public static double IL; public static double CL;
        public static Dictionary<string, Reach> Reaches = new Dictionary<string, Reach>();
    }
    
    public class TimeArea
    {
        public double depth;
        public double remainingFrac = 1;
    }

    class Subcatchment
    {
        public Dictionary<string, object> Properties { get; set; }
        public List<string> Reaches = new List<string>();
        public List<TimeArea> TimeAreas = new List<TimeArea>();
        public double HillVelocity;
        public double catchStorage = 0;
        public double groundWater = 0;
        public double catchOutflow = 0;
        double totalReachLength = 0;
        int totalOther = 0; // Dams

        public Subcatchment(Dictionary<string, object> properties)
        {
            Properties = properties;
            double[] slopes = { 0, 0.015, 0.04, 0.08, 0.15, 1 };
            double[] velocities = { 1.1 / 3.6, 1.1 / 3.6, 2.5 / 3.6, 3.2 / 3.6, 5.2 / 3.6, 10.8 / 3.6 };

            // Handle cases outside the slope range
            if ((double)Properties["HS"] <= slopes[0])
            {
                HillVelocity = velocities[0]; // Below minimum slope, use minimum velocity
            }
            else if ((double)Properties["HS"] >= slopes[^1]) // ^1 is the last index in the array (C# 8.0+)
            {
                HillVelocity = velocities[^1]; // Above maximum slope, use maximum velocity
            }
            else
            {
                // Find the correct interval for interpolation
                for (int i = 1; i < slopes.Length; i++) // Start at 1 to avoid accessing slopes[-1]
                {
                    if ((double)Properties["HS"] <= slopes[i])
                    {
                        HillVelocity = Interpolate((double)Properties["HS"], slopes[i - 1], slopes[i], velocities[i - 1], velocities[i]);
                        break;
                    }
                }
            }

            try
            {
                Properties["initialLoss_mm"] = GlobalVariables.IL;
                Properties["continuingLoss_mmhr"] = GlobalVariables.CL;
            }
            catch
            {
                Console.WriteLine("WARNING: INITIAL AND CONTINUING LOSSES NOT DEFINED. ASSUMING NIL STORM LOSSES");
                Properties["initialLoss_mm"] = 0;
                Properties["continuingLoss_mmhr"] = 0;
            }

        }

        public void getTotalReachLength()
        {
            foreach (string reach in Reaches)
            {
                if ((string)GlobalVariables.Reaches[reach].Properties["TYPE"] == "REACH")
                {
                    totalReachLength += (double)GlobalVariables.Reaches[reach].Properties["L"];
                }
                else
                {
                    totalOther++;
                }
            }
        }

        double Interpolate(double x, double x0, double x1, double y0, double y1)
        {
            return y0 + (x - x0) * (y1 - y0) / (x1 - x0);
        }

        public double addRain(double rainDepth_mm)
        {
            if (rainDepth_mm == 0)
            {
                return 0;
            }
            double excessRain_mm = 0;

            excessRain_mm = rainDepth_mm * (double)Properties["I"]; // Impervious Rainfall
            double perviousRain_mm = rainDepth_mm * (1 - ((double)Properties["I"])); // Impervious Rainfall
            perviousRain_mm -= (double)Properties["initialLoss_mm"]; // Remove Initial Loss
            Properties["initialLoss_mm"] = Math.Max((double)Properties["initialLoss_mm"] - rainDepth_mm, 0); // Subtract rainfall Depth from Initial Loss
            perviousRain_mm -= (double)Properties["continuingLoss_mmhr"] * GlobalVariables.dt / 3600;
            perviousRain_mm = Math.Max(perviousRain_mm, 0); // Non-Negative Pervious Rainfall
            excessRain_mm += perviousRain_mm; // Total Rainfall = Impervious Rainfall + Pervious Rainfall
            if (excessRain_mm != 0)
            {
                TimeArea timeArea = new TimeArea();
                timeArea.depth = excessRain_mm;
                TimeAreas.Add(timeArea);
            }
            return excessRain_mm;

        }

        public void routeUpperCatchment()
        {
            for (int i = TimeAreas.Count - 1; i >= 0; i--)
            {
                double fraction = HillVelocity * GlobalVariables.dt / ((double)Properties["HL"] * 1000);
                if (TimeAreas[i].remainingFrac - fraction <= 0)
                {
                    catchStorage += TimeAreas[i].remainingFrac * TimeAreas[i].depth / 1000 * (double)Properties["Area"] * 1000000;
                    TimeAreas[i].remainingFrac = 0;
                }
                else
                {
                    if (fraction * TimeAreas[i].depth / 1000 * (double)Properties["Area"] * 1000000 < 0)
                    {
                        Console.WriteLine($"HillVelocity: {HillVelocity}");
                    }
                    catchStorage += fraction * TimeAreas[i].depth / 1000 * (double)Properties["Area"] * 1000000;
                    TimeAreas[i].remainingFrac -= fraction;
                }

                if (TimeAreas[i].remainingFrac <= 0)
                {
                    TimeAreas.RemoveAt(i);
                }
            }
        }

        public void routeLowerCatchment()
        {
            catchOutflow = Math.Pow(catchStorage * (double)Properties["HS"] / ((double)Properties["HL"]*1000 * (double)Properties["B"]),
                1 / (double)Properties["m"]);

            double maxCatchOutflow = catchStorage / GlobalVariables.dt;
            catchOutflow = Math.Min(catchOutflow, maxCatchOutflow);
            catchStorage -= catchOutflow * GlobalVariables.dt;
            catchStorage = Math.Max(catchStorage, 0);
            if (totalOther > 0)
            {
                foreach (string reach in Reaches)
                {
                    if ((string)GlobalVariables.Reaches[reach].Properties["Type"] == "Dam")
                    {
                        GlobalVariables.Reaches[reach].storage += catchOutflow * GlobalVariables.dt / totalOther;
                    }
                }
            }
            else
            {
                foreach (string reach in Reaches)
                {
                    GlobalVariables.Reaches[reach].storage += catchOutflow * GlobalVariables.dt * ((double)GlobalVariables.Reaches[reach].Properties["L"] / totalReachLength);
                }
            }
        }
    }

    

    public class Reach
    {
        public Dictionary<string, object> Properties { get; set; }
        public string subcat;
        public double storage = 0;
        public string downstreamReach = null;
        public Reach(Dictionary<string, object> properties)
        {
            Properties = properties;
        }

        public double routeDownstream()
        {
            double discharge = 0;
            if ((string) Properties["TYPE"] == "REACH")
            {
                discharge = channelRouting();
            }
          
            storage -= discharge * GlobalVariables.dt;
            storage = Math.Max(storage, 0);
            if (downstreamReach != null)
            {
                GlobalVariables.Reaches[downstreamReach].storage += discharge * GlobalVariables.dt;
            }
            return discharge;
        }

        public double channelRouting()
        {

            double conveyanceArea = storage / ((double)Properties["L"] * 1000);

            double discharge = conveyanceArea * Math.Sqrt((double)Properties["SC"]) *
                (0.3 * Math.Pow(conveyanceArea, 1 / 3) + 0.1) / (double)Properties["N"];

            double maxDischarge = storage / GlobalVariables.dt;
            discharge = Math.Min(discharge, maxDischarge);

            return discharge;

        }

        public double DamRouting()
        {
            if ((string)Properties["Model"] == "SQ")
            {
                // Implement Defined S-Q Curve
                return 0;
            }
            else
            {
                return 0;
            }
        }

    }
}