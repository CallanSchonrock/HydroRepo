using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mydro
{
    class readRoutingFile
    {
        public List<Reach> Reaches = new List<Reach>();
        public readRoutingFile(string filePath)
        {
            List<Object> US_reaches = new List<Object>();
            Dictionary<string, List<Reach>> catchmentReaches = new Dictionary<string, List<Reach>>();
            int row = 0;
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File does not exist at path {filePath}.");
                Environment.Exit(1);
            }
            foreach (string line in File.ReadLines(filePath))
            {
                row++;
                string trimmed_line = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed_line) || trimmed_line.StartsWith("!"))
                    continue;

                // Remove any comments that come after "!"
                string[] parts = trimmed_line.Split('!');
                string lineWithoutComment = parts[0].Trim();

                if(lineWithoutComment.ToUpper().Contains("}"))
                {
                    List<object> branch = new List<object>();
                    if (US_reaches[US_reaches.Count - 1].GetType() == typeof(List<object>))
                    {
                        branch = (List<object>) US_reaches[US_reaches.Count - 1];
                    }
                    else
                    {
                        Console.WriteLine($"Mismatch of Branches/Junctions at row: {row}");
                        Environment.Exit(-1);
                    }
                    US_reaches.RemoveAt(US_reaches.Count - 1);  // Remove the last item from the list
                    US_reaches.AddRange(branch);
                    continue;
                }
                if (lineWithoutComment.ToUpper().Contains("{"))
                {
                    US_reaches = new List<object>() { US_reaches };
                    continue;
                }

                // Extract the ID and type
                string[] tokens = lineWithoutComment.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string subcat = tokens[0].TrimStart('#'); // Extract ID (e.g., 12)
                string type = tokens[1].ToUpper(); // Extract type (e.g., "Reach", "Dam")

                // Now, get the remaining part of the line starting after the type
                string remaining = lineWithoutComment.Substring(lineWithoutComment.IndexOf(type) + type.Length).Trim();

                // Create dictionary for the parameters
                var vecDict = new Dictionary<string, object>();
                vecDict["ID"] = subcat;
                vecDict["TYPE"] = type;

                // Regex pattern to match key-value pairs with possible spaces around `=`
                string pattern = @"(\w+)\s*=\s*([^\s]+)";
                foreach (Match match in Regex.Matches(remaining, pattern))
                {
                    string key = match.Groups[1].Value.ToUpper();   // Extract the key (e.g., L, Sc, N)
                    if (! double.TryParse(match.Groups[2].Value, out double value) || double.IsNaN(value))
                    {
                        Console.WriteLine($"Cannot Parse Value for {key} at row {row}.");
                        Environment.Exit(-1);
                    }
                    vecDict[key] = value;
                }
                Reach reach = new Reach(vecDict);
                reach.subcat = subcat;
                if (US_reaches.Count > 0)
                {
                    for (int i = US_reaches.Count - 1; i >= 0; i--)
                    {
                        if (US_reaches[i].GetType() == typeof(Reach))
                        {
                            Reach upstream_reach = (Reach)US_reaches[i];
                            upstream_reach.downstreamReach = subcat;
                            Reaches.Add(upstream_reach);
                            US_reaches.RemoveAt(i);
                        }
                    }
                }
                US_reaches.Insert(0,reach);
            }
            for (int i = 0; i < US_reaches.Count; i++)
            {
                Reaches.Add((Reach)US_reaches[i]);
            }
        }
    }
}
