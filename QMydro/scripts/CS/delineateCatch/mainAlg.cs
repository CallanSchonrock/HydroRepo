using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;
using OSGeo.OGR;
using OSGeo.OSR;
using OSGeo.GDAL;
using System.Threading.Tasks.Dataflow;


namespace mainAlg
{

    class Subcatchment
    {
        public int id;
        public List<Subcatchment> upstreamCatchments = new List<Subcatchment>() { };
        public int dsCatchment = -1;
        public int numUS = 0;
        public bool rooted = false;
        public double area = 0;
        public double us_area = 0;
        public double ToC = 0;
        public double channelLength = 0;
        public Subcatchment(int idToAssign)
        {
            id = idToAssign;
        }
    }
    class mainAlgorithm
    {
        // static Dictionary<(int, int), int> dd = new Dictionary<(int, int), int>() { { (-1, -1), 1 }, { (-1, 0), 2 }, { (-1, 1) , 3}, { (0, -1), 4 }, { (0, 1), 5 }, { (1, -1), 6 }, { (1, 0), 7 }, { (1, 1), 8 } };
        static sbyte[,] d8 = new sbyte[,]
        {
        {1, 2, 3 },
        {4, 0, 5 },
        {6, 7, 8 }
        };

        static Dictionary<sbyte, (sbyte, sbyte)> reverseD8 = new Dictionary<sbyte, (sbyte, sbyte)>() { { 1, (-1, -1) }, { 2, (-1, 0) }, { 3, (-1, 1) }, { 4, (0, -1) }, { 5, (0, 1) }, { 6, (1, -1) }, { 7, (1, 0) }, { 8, (1, 1) } };
        public static string? model;
        public static string? outputDir;
        public static int[,]? catchments;
        public static int[,]? outletCatchments;
        public static sbyte[,]? drainageMap;
        public static sbyte[,]? cellNeighbours;
        public static int[,]? accumulation;
        public static int[,]? subcatchments;
        public static int[,]? channel;
        public static Dictionary<int, Subcatchment> subcatchmentsInfo = new Dictionary<int, Subcatchment>();
        public static List<float> channelSlopes = new List<float>();
        public static List<float> channelLengths = new List<float>();


        static void writeUrbsfile()
        {
            foreach (Subcatchment subcatchment in subcatchmentsInfo.Values)
            {
                subcatchment.numUS = subcatchment.upstreamCatchments.Count;
            }

            using (StreamWriter writer = new StreamWriter(Path.Combine(outputDir, "_RoutingFile.vec")))
            {
                writer.WriteLine("MODELNAME");
                writer.WriteLine("Model: SPLIT");
                writer.WriteLine("USES: L , CS , Sc , U, I ");
                writer.WriteLine("DEFAULT PARAMETERS: alpha = 0.005 m = 0.8 beta = 2.5 n = 1 x = 0 IL = 0 Cl = 0.0");
                writer.WriteLine("CATCHMENT DATA FILE = _SubcatFile.csv");

                bool upstreamCatch = true;
                int outlets = 0;
                int indentCount = 0;
                bool error = false;
                while (upstreamCatch)
                {
                    List<Subcatchment> queue = new List<Subcatchment>();
                    upstreamCatch = false;
                    foreach (Subcatchment subcatchment in subcatchmentsInfo.Values.Reverse())
                    {
                        if (subcatchment.upstreamCatchments.Count == 0 && subcatchment.rooted == false)
                        {
                            queue.Add(subcatchment);
                            break;
                        }

                    }

                    while (queue.Count > 0)
                    {
                        // if no. upstream > 0, insert subcatchment at 0, and continue else ADD RAIN or RAIN, then ROUTE THRU deleting DS US catchment

                        if (queue[0].upstreamCatchments.Count > 0)
                        {
                            int maxIndex = 0;
                            double maxCount = -1;
                            for (int i = 0; i < queue[0].upstreamCatchments.Count; i++)
                            {
                                if (queue[0].upstreamCatchments[i].us_area > maxCount)
                                {
                                    maxIndex = i;
                                    maxCount = queue[0].upstreamCatchments[i].us_area;
                                }
                            }
                            queue.Add(queue[0].upstreamCatchments[maxIndex]); queue.RemoveAt(0); continue;
                        }
                        string indentation = "";
                        for (int i = 0; i < indentCount; i++)
                        {
                            indentation += "\t";
                        }
                        queue[0].rooted = true;
                        string extension = $" L = {Math.Round(queue[0].channelLength / 2, 5)} Sc = {Math.Round(Math.Max(channelSlopes[queue[0].id - 1], 0.0005), 5)} ";

                        if (queue[0].numUS == 0) { writer.WriteLine($"{indentation}RAIN #{queue[0].id}" + extension); }
                        else { writer.WriteLine($"{indentation}ADD RAIN #{queue[0].id}" + extension); }

                        if (queue[0].dsCatchment != -1)
                        {

                            queue.Add(subcatchmentsInfo[queue[0].dsCatchment]);
                            int indexToRemove = 0;

                            foreach (Subcatchment subCat in subcatchmentsInfo[queue[0].dsCatchment].upstreamCatchments)
                            {
                                if (subCat.id == queue[0].id) { break; }
                                indexToRemove++;
                            }
                            try
                            {
                                subcatchmentsInfo[queue[0].dsCatchment].upstreamCatchments.RemoveAt(indexToRemove);
                            }
                            catch
                            {
                                Console.WriteLine($"ERROR DETERMINING UPSTREAM / DOWNSTREAM SUBCATCHMENTS");
                                Console.WriteLine($"CHECK ROUTING FILE FOR ERRORS - IF ENABLED, DISABLE AUTOBREAKUP");
                                queue.Clear();
                                upstreamCatch = false;
                                error = true;
                                break;
                            }

                        }

                        queue.RemoveAt(0);
                        if (queue.Count > 0)
                        {
                            if (queue[0].upstreamCatchments.Count > 0)
                            {
                                writer.WriteLine($"{indentation}STORE.");
                                indentCount++;
                            }
                            else
                            {
                                for (int usSubCats = 1; usSubCats < queue[0].numUS; usSubCats++)
                                {
                                    indentCount--;
                                    indentation = "";
                                    for (int i = 0; i < indentCount; i++)
                                    {
                                        indentation += "\t";
                                    }
                                    writer.WriteLine($"{indentation}GET.");
                                }

                                extension = $" L = {Math.Round(queue[0].channelLength / 2, 5)} Sc = {Math.Round(Math.Max(channelSlopes[queue[0].id - 1], 0.0005), 5)} ";
                                writer.WriteLine($"{indentation}ROUTE THRU #{queue[0].id}" + extension);
                            }
                        }
                    }
                    if (error) { break; }
                    foreach (Subcatchment subcatchment in subcatchmentsInfo.Values) // ADD DONE SUBCATCHMENTS
                    {
                        if (subcatchment.rooted == false)
                        {
                            writer.WriteLine($"STORE.");
                            outlets += 1;
                            upstreamCatch = true;
                            break;
                        }
                    }
                }

                for (int i = 0; i < outlets; i++)
                {
                    writer.WriteLine("GET.");
                }

                writer.WriteLine("END OF CATCHMENT DATA.");
            }
        }

        static void writeMydrofile()
        {
            foreach (Subcatchment subcatchment in subcatchmentsInfo.Values)
            {
                subcatchment.numUS = subcatchment.upstreamCatchments.Count;
            }

            using (StreamWriter writer = new StreamWriter(Path.Combine(outputDir, "_RoutingFile.vec")))
            {
                writer.WriteLine("! MODELNAME");

                bool upstreamCatch = true;
                int outlets = 0;
                int indentCount = 0;
                bool error = false;
                while (upstreamCatch)
                {
                    List<Subcatchment> queue = new List<Subcatchment>();
                    upstreamCatch = false;
                    foreach (Subcatchment subcatchment in subcatchmentsInfo.Values.Reverse())
                    {
                        if (subcatchment.upstreamCatchments.Count == 0 && subcatchment.rooted == false)
                        {
                            queue.Add(subcatchment);
                            break;
                        }

                    }

                    while (queue.Count > 0)
                    {
                        // if no. upstream > 0, insert subcatchment at 0, and continue else ADD RAIN or RAIN, then ROUTE THRU deleting DS US catchment

                        if (queue[0].upstreamCatchments.Count > 0)
                        {
                            int maxIndex = 0;
                            double maxCount = -1;
                            for (int i = 0; i < queue[0].upstreamCatchments.Count; i++)
                            {
                                if (queue[0].upstreamCatchments[i].us_area > maxCount)
                                {
                                    maxIndex = i;
                                    maxCount = queue[0].upstreamCatchments[i].us_area;
                                }
                            }
                            queue.Add(queue[0].upstreamCatchments[maxIndex]); queue.RemoveAt(0); continue;
                        }
                        string indentation = "";
                        for (int i = 0; i < indentCount; i++)
                        {
                            indentation += "\t";
                        }
                        queue[0].rooted = true;
                        string extension = $" L = {Math.Round(queue[0].channelLength / 2, 5)} Sc = {Math.Round(Math.Max(channelSlopes[queue[0].id - 1], 0.0005), 5)} N = 0.03 k = 0.3 d = -0.3";

                        writer.WriteLine($"{indentation}#{queue[0].id} REACH" + extension);

                        if (queue[0].dsCatchment != -1)
                        {

                            queue.Add(subcatchmentsInfo[queue[0].dsCatchment]);
                            int indexToRemove = 0;

                            foreach (Subcatchment subCat in subcatchmentsInfo[queue[0].dsCatchment].upstreamCatchments)
                            {
                                if (subCat.id == queue[0].id) { break; }
                                indexToRemove++;
                            }
                            try
                            {
                                subcatchmentsInfo[queue[0].dsCatchment].upstreamCatchments.RemoveAt(indexToRemove);
                            }
                            catch
                            {
                                Console.WriteLine($"ERROR DETERMINING UPSTREAM / DOWNSTREAM SUBCATCHMENTS");
                                Console.WriteLine($"CHECK ROUTING FILE FOR ERRORS - IF ENABLED, DISABLE AUTOBREAKUP");
                                queue.Clear();
                                upstreamCatch = false;
                                error = true;
                                break;
                            }
                        }

                        queue.RemoveAt(0);
                        if (queue.Count > 0)
                        {
                            if (queue[0].upstreamCatchments.Count > 0)
                            {
                                writer.WriteLine($"{indentation}{"{"}");
                                indentCount++;
                            }
                            else
                            {
                                for (int usSubCats = 1; usSubCats < queue[0].numUS; usSubCats++)
                                {
                                    indentCount--;
                                    indentation = "";
                                    for (int i = 0; i < indentCount; i++)
                                    {
                                        indentation += "\t";
                                    }
                                    writer.WriteLine($"{indentation}{"}"}");
                                }
                            }
                        }
                    }
                    if (error) { break; }
                    foreach (Subcatchment subcatchment in subcatchmentsInfo.Values) // ADD DONE SUBCATCHMENTS
                    {
                        if (subcatchment.rooted == false)
                        {
                            writer.WriteLine("{");
                            outlets += 1;
                            upstreamCatch = true;
                            break;
                        }
                    }
                }

                for (int i = 0; i < outlets; i++)
                {
                    writer.WriteLine("}");
                }

            }
        }
            


        static List<(int, int)> dEightFlowAlg(float[,] elev, int numRows, int numCols, float noData_val)
        {
            List<(int, int)> outletList = new List<(int, int)>();

            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {

                    if (elev[i, j] == noData_val)
                    {
                        continue;
                    }

                    float minElev = 99999;
                    bool noDataNeighbour = false;

                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = i + di;
                        if (ni < 0 || ni >= numRows) { noDataNeighbour = true; continue; }
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            if (di == 0 && dj == 0) { continue; }
                            int nj = j + dj;
                            if (nj < 0 || nj >= numCols) { noDataNeighbour = true; continue; }
                            if (elev[ni, nj] == noData_val) { noDataNeighbour = true; continue; }
                            if (elev[ni, nj] < minElev)
                            {
                                minElev = elev[ni, nj];
                            }
                        }
                    }

                    if (noDataNeighbour)
                    {
                        if (elev[i, j] <= minElev)
                        {
                            outletList.Add((i, j));
                        }
                    }

                }
            }
            return outletList;
        }

        private static readonly Random rand = new Random();
        static void Shuffle<T>(List<T> list)
        {
            int n = list.Count;

            for (int i = n - 1; i > 0; i--)
            {
                // Pick a random index from 0 to i
                int randIndex = rand.Next(0, i + 1);

                // Swap list[i] with the element at randIndex
                T temp = list[i];
                list[i] = list[randIndex];
                list[randIndex] = temp;
            }
        }

        public static (double, double) GetSRS_XY(int cell_x, int cell_y, double[] geotransform)
        {
            double srsX = (cell_x + 0.5) * geotransform[1] + geotransform[0];
            double srsY = (cell_y + 0.5) * geotransform[5] + geotransform[3];
            return (srsX, srsY);
        }


        public static void writeRaster(string outputDir, string fileName, int[,] array, int numCols, int numRows, double[] geotransform, SpatialReference srs)
        {
            OSGeo.GDAL.Driver driver = Gdal.GetDriverByName("GTiff");
            string[] creationOptions = new string[]
            {
            "COMPRESS=LZW",    // Example: Use LZW compression
            "TILED=YES",       // Example: Create tiled GeoTIFF
            "BIGTIFF=YES"      // Example: Enable BigTIFF format for large files
                               // Add more options as needed based on your requirements
            };
            Dataset Dataset = driver.Create(Path.Combine(outputDir, fileName), numCols, numRows, 1, DataType.GDT_UInt32, creationOptions);
            Dataset.SetGeoTransform(geotransform);
            Dataset.SetSpatialRef(srs);
            Band dataBand = Dataset.GetRasterBand(1);
            dataBand.SetNoDataValue(0);
            int[] dataBuffer = new int[numCols * numRows];
            int index = 0;
            for (int row = 0; row < numRows; row++)
            {
                for (int col = 0; col < numCols; col++)
                {
                    dataBuffer[index++] = array[row, col];
                }
            }
            dataBand.WriteRaster(0, 0, numCols, numRows, dataBuffer, numCols, numRows, 0, 0);
            Dataset.FlushCache();
            Dataset.Dispose();
        }
        
        public static void processingAlg(float[,] elev, float noData_val, List<List<(int, int)>> outletCells,
            float dx, float dy, float targetCatchSize, string modelType, string outputDirectory, SpatialReference srs, double[] geotransform)
        {
            outputDir = outputDirectory;
            model = modelType;
            int numRows = elev.GetLength(0);
            int numCols = elev.GetLength(1);

            Console.WriteLine($"Target Subcatchment Cells: {targetCatchSize}");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            // Iterate over each cell in the elevation array
            List<(int, int)> outletsList = dEightFlowAlg(elev, numRows, numCols, noData_val);
            stopwatch.Stop();
            Console.WriteLine($"Raster Outflows: {outletsList.Count}, Time: {stopwatch.ElapsedMilliseconds}");

            PriorityQueue<(int, int), float> lowestCells = new PriorityQueue<(int, int), float>();
            PriorityQueue<(int, int), float> subCells = new PriorityQueue<(int, int), float>();

            catchments = new int[numRows, numCols];
            int[,] outletCatchments = new int[numRows, numCols];

            int catchmentID = 1;
            foreach ((int, int) outletPixel in outletsList)
            {
                outletCatchments[outletPixel.Item1, outletPixel.Item2] = catchmentID;
                lowestCells.Enqueue(outletPixel, elev[outletPixel.Item1, outletPixel.Item2]);
                catchmentID++;
            }
            Console.WriteLine($"Outlet Pixels: {lowestCells.Count}");

            stopwatch.Start();
            catchmentID = 1;

            
            foreach (List<(int, int)> userOutlet in outletCells)
            {
                int lastx = userOutlet[0].Item1;
                int lasty = userOutlet[0].Item2;
                foreach ((int x, int y) in userOutlet)
                {
                    catchments[x, y] = catchmentID;
                    if (lastx != x && lasty != y)
                    {
                        catchments[lastx, y] = catchmentID;
                        catchments[x, lasty] = catchmentID;
                    }
                }
                subcatchmentsInfo.Add(catchmentID, new Subcatchment(catchmentID));
                catchmentID++;
            }
            sbyte[,] drainageMap = new sbyte[numRows, numCols];
            sbyte[,] cellNeighbours = new sbyte[numRows, numCols];
            sbyte[,] cellInflows = new sbyte[numRows, numCols];
            int[,] accumulation = new int[numRows, numCols];

            List<(int, int, float)> cellsToEnqueue = new List<(int, int, float)>();
            Random random = new Random();
            while (lowestCells.Count > 0)
            {
                cellsToEnqueue.Clear();
                sbyte neighbouringCells = 0;
                (int x, int y) = lowestCells.Dequeue();
                float cell_elev = elev[x, y];
                int xOffset = x - 1;
                int yOffset = y - 1;
                int catchment = catchments[x, y];
                int outletCatchment = outletCatchments[x, y];
                for (int i = x - 1; i <= x + 1; i++)
                {
                    if (i < 0 || i > numRows - 1) continue;
                    for (int j = y - 1; j <= y + 1; j++)
                    {
                        if (j < 0 || j > numCols - 1) { continue; }
                        if (outletCatchments[i, j] != default) { continue; }
                        if (catchment != default) { catchments[i, j] = catchment; }
                        float elevation = elev[i, j];
                        if (elevation == noData_val) { continue; }
                        outletCatchments[i, j] = outletCatchment;
                        if (catchment != default) { catchments[i, j] = catchment; }
                        // drainageMap[i, j] = dd[(i - x, j - y)]
                        drainageMap[i, j] = d8[i - xOffset, j - yOffset];

                        if (elevation == elev[i, j]) { cellsToEnqueue.Add((i, j, elevation + (float) (random.NextDouble() * 0.01))); } else { cellsToEnqueue.Add((i, j, elevation)); }
                        
                        neighbouringCells += 1;
                    }
                }
                Shuffle(cellsToEnqueue);
                foreach ((ushort, ushort, float) cell in cellsToEnqueue)
                {
                    lowestCells.Enqueue((cell.Item1, cell.Item2), cell.Item3);
                }

                cellNeighbours[x, y] = neighbouringCells;
                if (neighbouringCells == 0)
                {
                    (int, int) dsCell = (x, y);
                    while (dsCell != (-1, -1))
                    {                        
                        (int currentx, int currenty) = dsCell;
                        dsCell = (-1, -1);
                        accumulation[currentx, currenty] += 1;
                        for (int i = currentx - 1; i <= currentx + 1; i++)
                        {
                            if (i < 0 || i > numRows - 1) { continue; }
                            for (int j = currenty - 1; j <= currenty + 1; j++)
                            {
                                if (i == currentx && j == currenty) { continue; }
                                if (j < 0 || j > numCols - 1) { continue; }
                                if (elev[i, j] == noData_val) { continue; }

                                //if (dd[(x - i, y - j)] == drainageMap[x, y])
                                if (d8[currentx - i + 1, currenty - j + 1] == drainageMap[currentx, currenty])
                                {
                                    accumulation[i, j] += accumulation[currentx, currenty];
                                    cellInflows[i, j] += 1;
                                    if (cellNeighbours[i, j] == cellInflows[i,j])
                                    {
                                        dsCell = (i, j);
                                    }
                                }
                            }
                        }
                    }
                }

            }
            writeRaster(outputDir, "QMydro_Accumulation.tif", accumulation, numCols, numRows, geotransform, srs);
            stopwatch.Stop();
            Console.WriteLine($"Accumulation Calculated. Time: {stopwatch.ElapsedMilliseconds}");

            


            int[,] subcatchments = new int[numRows, numCols];
            List<int> catchAccs = new List<int>();
            List<int> catchMaxAccs = new List<int>();
            List<(double, double)> catchHydraulicParameters = new List<(double, double)>();
            stopwatch.Start();
            catchmentID = 1;
            foreach (List<(int, int)> userOutlet in outletCells)
            {
                int lastx = userOutlet[0].Item1;
                int lasty = userOutlet[0].Item2;
                catchAccs.Add(0);
                catchMaxAccs.Add(0);
                List<(int, int, int, float)> subCatOutletCells = new List<(int, int, int, float)>();
                List<int> indexesToSkip = new List<int>();
                foreach ((int x, int y) in userOutlet)
                {
                    subCatOutletCells.Add((x, y, accumulation[x, y], elev[x, y]));
                    if (lastx != x && lasty != y)
                    {
                        subCatOutletCells.Add((lastx, y, accumulation[lastx, y], elev[lastx, y]));
                        indexesToSkip.Add(subCatOutletCells.Count - 1);
                        subCatOutletCells.Add((x, lasty, accumulation[x, lasty], elev[x, lasty]));
                        indexesToSkip.Add(subCatOutletCells.Count - 1);
                    }
                    lastx = x;
                    lasty = y;
                }

                List<int> xsX = subCatOutletCells.Select(tuple => tuple.Item1).ToList();
                List<int> xsY = subCatOutletCells.Select(tuple => tuple.Item2).ToList();
                List<float> xsElev = subCatOutletCells.Select(tuple => tuple.Item4).ToList();

                float xsMin = xsElev.Min();
                int xsMinIndex = xsElev.IndexOf(xsMin);

                float leftMaxElev = xsElev.Take(xsMinIndex + 1).Max();
                float rightMaxElev = xsElev.Skip(Math.Max(0,xsElev.Count - (xsMinIndex+1))).Max();

                float minMaxElev = Math.Min(leftMaxElev, rightMaxElev);

                float stepSize = (float) Math.Min((minMaxElev - xsMin) / 10, 1.0);
                double slope = 0; double yIntercept = 0;
                if (minMaxElev - xsMin > 0.5 && xsX.Count >= 5 && dx + dy <= 20)
                {
                    List<double> conveyanceAreaLN = new List<double>();
                    List<double> hydraulicR = new List<double>();
                    for (int i = 2; i <= 10; i++)
                    {
                        float waterLevel = xsMin + i * stepSize;
                        float wettedPerimeter = 0;
                        float conveyanceArea = 0;
                        int lastIndex = 0;
                        for (int indexOffset = 0; indexOffset < xsX.Count(); indexOffset++)
                        {
                            if (indexesToSkip.Contains(indexOffset)) { continue; }
                            if (xsElev[indexOffset] >= waterLevel) { continue; }

                            float distance = (float) Math.Sqrt(Math.Pow((xsX[indexOffset] - xsX[lastIndex]) * dx, 2) + Math.Pow((xsY[indexOffset] - xsY[lastIndex]) * dy, 2));
                            wettedPerimeter += (float) Math.Sqrt(Math.Pow(distance,2) + Math.Pow(xsElev[indexOffset] - xsElev[lastIndex],2));
                            conveyanceArea += distance * (waterLevel - xsElev[indexOffset]);
                            lastIndex = indexOffset;
                        }
                        if (conveyanceArea == 0 || wettedPerimeter == 0) { continue; }
                        
                        conveyanceAreaLN.Add(Math.Log(conveyanceArea));
                        hydraulicR.Add(Math.Pow(conveyanceArea / wettedPerimeter, (2.0 / 3.0)));
                    }

                    try
                    {
                        // Calculate the mean of x and y values
                        double meanX = conveyanceAreaLN.Average();
                        double meanY = hydraulicR.Average();

                        // Calculate the slope (m)
                        double numerator = 0;
                        double denominator = 0;

                        for (int i = 0; i < conveyanceAreaLN.Count; i++)
                        {
                            numerator += (conveyanceAreaLN[i] - meanX) * (hydraulicR[i] - meanY);
                            denominator += Math.Pow(conveyanceAreaLN[i] - meanX, 2);
                        }

                        slope = numerator / denominator;

                        // Calculate the y-intercept (b)
                        yIntercept = meanY - slope * meanX;
                    }
                    catch
                    {

                    }
                }
                catchHydraulicParameters.Add((slope, yIntercept));

                subCatOutletCells = subCatOutletCells.OrderByDescending(tuple => tuple.Item3).ToList();
                subCatOutletCells = subCatOutletCells.Where(tuple => tuple.Item3 > 100).ToList();
                foreach((int x, int y, int acc, float cellElev) in subCatOutletCells)
                {
                    subCells.Enqueue((x, y), accumulation[x, y]);
                    subcatchments[x, y] = (ushort) catchmentID;
                    catchAccs[catchmentID - 1] += 1;
                    catchMaxAccs[catchmentID - 1] = Math.Max(catchMaxAccs[catchmentID - 1], accumulation[x, y]);
                }
                catchmentID++;
            }


            while (subCells.Count > 0)
            {
                (int x, int y) = subCells.Dequeue();
                int thisSubCat = subcatchments[x, y];
                int thisAcc = accumulation[x, y];
                for (int i = x - 1; i <= x + 1; i++)
                {
                    if (i < 0 || i > numRows - 1) continue;
                    for (int j = y - 1; j <= y + 1; j++)
                    {
                        if (i == x && j == y) { continue; }
                        if (j < 0 || j > numCols - 1) { continue; }
                        if (elev[i, j] == noData_val) { continue; }

                        //if (dd[(i - x, j - y)] == drainageMap[i, j])
                        if (d8[i - x + 1, j - y + 1] == drainageMap[i, j])
                        {
                            int nextSubCat = subcatchments[i, j];
                            if (nextSubCat == thisSubCat)
                            {
                                continue;
                            }
                            if (nextSubCat != default) // VECFILE ROUTING
                            {
                                if (!subcatchmentsInfo[thisSubCat].upstreamCatchments.Contains(subcatchmentsInfo[nextSubCat]) && !subcatchmentsInfo[nextSubCat].upstreamCatchments.Contains(subcatchmentsInfo[thisSubCat]) && catchMaxAccs[thisSubCat - 1] > catchMaxAccs[nextSubCat - 1])
                                {
                                    subcatchmentsInfo[thisSubCat].upstreamCatchments.Add(subcatchmentsInfo[nextSubCat]);
                                    subcatchmentsInfo[nextSubCat].dsCatchment = thisSubCat;

                                }
                                continue;
                            }
                            subCells.Enqueue((i, j), accumulation[i, j]);
                            if (targetCatchSize > 0 && accumulation[i, j] > targetCatchSize * 0.5 && catchAccs[thisSubCat - 1] > targetCatchSize * 0.01 && thisAcc - accumulation[i,j] > targetCatchSize * 0.5)
                            {
                                subcatchmentsInfo.Add(catchmentID, new Subcatchment(catchmentID));
                                catchAccs.Add(1);
                                catchMaxAccs.Add(accumulation[i, j]);
                                subcatchments[i, j] = (ushort)catchmentID;
                                catchHydraulicParameters.Add((0, 0));
                                outletCells.Add(new List<(int, int)> { (i, j) });
                                subcatchmentsInfo[thisSubCat].upstreamCatchments.Add(subcatchmentsInfo[catchmentID]);
                                subcatchmentsInfo[catchmentID].dsCatchment = thisSubCat;
                                catchmentID++;
                            }
                            else
                            {
                                subcatchments[i, j] = thisSubCat;
                                catchAccs[thisSubCat - 1] += 1;
                            }
                        }
                    }
                }
            }
            

            writeRaster(outputDir, "QMydro_Subcats.tif", subcatchments, numCols, numRows, geotransform, srs);
            Dataset subcats_Dataset = Gdal.Open(Path.Combine(outputDir, "QMydro_SubCats.tif"), Access.GA_ReadOnly);

            // Open the raster band to polygonize
            Band subcats_rasterBand = subcats_Dataset.GetRasterBand(1); // Use the first band (1-indexed)

            OSGeo.OGR.Driver shpDriver = Ogr.GetDriverByName("ESRI Shapefile");
            DataSource dataSource = shpDriver.CreateDataSource(Path.Combine(outputDir, "QMydro_SubCats.shp"), null);
            Layer polygonLayer = dataSource.CreateLayer("QMydro_SubCats", srs, wkbGeometryType.wkbPolygon, null);
            FieldDefn idField = new FieldDefn("ID", FieldType.OFTInteger);
            polygonLayer.CreateField(idField, 1);
            FieldDefn areaField = new FieldDefn("Area", FieldType.OFTReal);
            polygonLayer.CreateField(areaField, 1);
            if (model == "Mydro")
            {
                FieldDefn hillLengthField = new FieldDefn("HL", FieldType.OFTReal);
                polygonLayer.CreateField(hillLengthField, 1);
                FieldDefn hillSlopeField = new FieldDefn("HS", FieldType.OFTReal);
                polygonLayer.CreateField(hillSlopeField, 1);
            }
            else
            {
                FieldDefn hillSlopeField = new FieldDefn("CS", FieldType.OFTReal);
                polygonLayer.CreateField(hillSlopeField, 1);
            }
            FieldDefn usAreaField = new FieldDefn("US_Area", FieldType.OFTReal);
            polygonLayer.CreateField(usAreaField, 1);
            FieldDefn ToCField = new FieldDefn("ToC_min", FieldType.OFTReal);
            polygonLayer.CreateField(ToCField, 1);
            FieldDefn IField = new FieldDefn("I", FieldType.OFTReal);
            polygonLayer.CreateField(IField, 1);
            if (model == "Mydro")
            {
                FieldDefn bField = new FieldDefn("B", FieldType.OFTReal);
                polygonLayer.CreateField(bField, 1);
                FieldDefn mField = new FieldDefn("m", FieldType.OFTReal);
                polygonLayer.CreateField(mField, 1);
            }
            FieldDefn DSField = new FieldDefn("DS", FieldType.OFTInteger);
            polygonLayer.CreateField(DSField, 1);
            Band maskBand = subcats_rasterBand.GetMaskBand(); // No mask band (null means use all pixels)
            string[] options = new string[] { "8CONNECTED=8", "8CONNECTED", "8CONNECTED=TRUE" }; // Specify 8-connectedness
            int fieldIndex = polygonLayer.GetLayerDefn().GetFieldIndex("ID");
            int result = Gdal.FPolygonize(subcats_rasterBand, maskBand, polygonLayer, fieldIndex, options, null, null);

            dataSource.FlushCache();
            dataSource.Dispose();


            DataSource subcats_dataSource = Ogr.Open(Path.Combine(outputDir, "QMydro_SubCats.shp"), 1); // Open with write access
            Layer subcatLayer = subcats_dataSource.GetLayerByIndex(0);
            SpatialReference utmSRS = new SpatialReference("");
            Feature subCatFeature;
            subcatLayer.ResetReading();
            while ((subCatFeature = subcatLayer.GetNextFeature()) != null)
            {
                // Get the value of the "ID" field
                int id = subCatFeature.GetFieldAsInteger("ID");

                // Check if the "ID" field matches the target index i
                if (id > 0 && id <= subcatchmentsInfo.Count) // Check if ID is within the range of the areas array
                {
                    Geometry originalPolygonGeometry = subCatFeature.GetGeometryRef();
                    Geometry wgs84Geometry = originalPolygonGeometry.Clone();
                    SpatialReference wgs84 = new SpatialReference("");
                    
                    wgs84.ImportFromEPSG(4326); // EPSG 4326: WGS84 geographic coordinates
                    if (0 == srs.IsSame(wgs84, new string[] { }))
                    {
                        wgs84Geometry.TransformTo(wgs84);
                    }
                    Geometry centroid = wgs84Geometry.Centroid();
                    double centerX = centroid.GetX(0); // Longitude
                    double centerY = centroid.GetY(0); // Latitude
                    int utmZone = (int)Math.Floor((centerX + 180) / 6) + 1;

                    
                    if (centerY >= 0) // Northern Hemisphere
                    {
                        utmSRS.ImportFromEPSG(32600 + utmZone); // EPSG:32600 is UTM zone 1N
                    }
                    else // Southern Hemisphere
                    {
                        utmSRS.ImportFromEPSG(32700 + utmZone); // EPSG:32700 is UTM zone 1S
                    }
                    Geometry utmGeometry = originalPolygonGeometry.Clone();

                    utmGeometry.TransformTo(utmSRS);
                    double area = utmGeometry.GetArea() / 1000000;
                    subcatchmentsInfo[id].area = area;
                    // Set the "Area" field to the corresponding value from the areas array
                    subCatFeature.SetField("Area", area); // Assuming "ID" starts from 1
                    subCatFeature.SetField("I", 0);
                    if (model == "Mydro")
                    {
                        subCatFeature.SetField("B", 1.0);
                        subCatFeature.SetField("m", 0.8);
                    }
                    // Update the feature in the layer with the modified field value
                    subcatLayer.SetFeature(subCatFeature);
                }
            }

            for (int i = 1; i < catchmentID; i++)
            {
                double subbyArea = subcatchmentsInfo[i].area;
                int dsSubby = i; 
                while (dsSubby != -1)
                {
                    subcatchmentsInfo[dsSubby].us_area += subbyArea;
                    dsSubby = subcatchmentsInfo[dsSubby].dsCatchment;
                }
            }

            subcatLayer.ResetReading();
            while ((subCatFeature = subcatLayer.GetNextFeature()) != null)
            {
                // Get the value of the "ID" field
                int id = subCatFeature.GetFieldAsInteger("ID");

                // Check if the "ID" field matches the target index i
                if (id > 0 && id <= subcatchmentsInfo.Count) // Check if ID is within the range of the areas array
                {
                    // Set the "Area" field to the corresponding value from the areas array
                    subCatFeature.SetField("US_Area", subcatchmentsInfo[id].us_area); // Assuming "ID" starts from 1

                    // Update the feature in the layer with the modified field value
                    subcatLayer.SetFeature(subCatFeature);
                }
            }
            // Cleanup and close the dataset

            stopwatch.Stop();
            Console.WriteLine($"Catchments Calculated. Time: {stopwatch.ElapsedMilliseconds}");


            
            stopwatch.Start();
            

            List<float> channelCatchSlopes = new List<float>();
            List<float> channelCatchLengths = new List<float>();
            List<Geometry> channelLines = new List<Geometry>();
            
            int[,] channel = new int[numRows, numCols];
            OSGeo.OGR.Driver streams_shpDriver = Ogr.GetDriverByName("ESRI Shapefile");
            string shpFilePath = @$"{outputDir}\QMydro_Streams.shp";
            DataSource streams_dataSource = shpDriver.CreateDataSource(shpFilePath, null);
            Layer streams_layer = streams_dataSource.CreateLayer("lines", srs, wkbGeometryType.wkbLineString, null);
            FieldDefn streams_idField = new FieldDefn("ID", FieldType.OFTInteger);
            streams_layer.CreateField(streams_idField, 1);
            FieldDefn lengthField = new FieldDefn("Length", FieldType.OFTReal);
            streams_layer.CreateField(lengthField, 1);
            FieldDefn slopeField = new FieldDefn("Slope", FieldType.OFTReal);
            streams_layer.CreateField(slopeField, 1);
            for (int i = 1; i < catchmentID; i++)
            {
                channelSlopes.Add(0);
                channelLengths.Add(0);
                channelCatchSlopes.Add(0);
                channelCatchLengths.Add(0);
                Geometry geom = new Geometry(wkbGeometryType.wkbLineString);
                geom.AssignSpatialReference(srs);
                channelLines.Add(geom);
            }

            foreach (List<(int, int)> userOutlet in outletCells)
            {
                (int, (int, int)) mostAccumulation = (-1, (0, 0)); // (Accumulation, (x,y))
                int lastx = userOutlet[0].Item1;
                int lasty = userOutlet[0].Item2;
                
                foreach ((int ix, int iy) in userOutlet)
                {
                    
                    if (accumulation[ix, iy] > mostAccumulation.Item1)
                    {
                        if (subcatchments[ix, iy] != default) { mostAccumulation = (accumulation[ix, iy], (ix, iy)); }
                        
                    }
                    if (lastx != ix && lasty != iy)
                    {
                        if (accumulation[lastx, iy] > mostAccumulation.Item1)
                        {
                            if (subcatchments[lastx, iy] != default) { mostAccumulation = (accumulation[lastx, iy], (lastx, iy)); }
                        }
                        else if (accumulation[ix, lasty] > mostAccumulation.Item1)
                        {
                            if (subcatchments[ix, lasty] != default) { mostAccumulation = (accumulation[ix, lasty], (ix, lasty)); }
                        }
                    }
                    lastx = ix; lasty = iy;
                }

                int startAcc = mostAccumulation.Item1;
                int startX = mostAccumulation.Item2.Item1;
                int startY = mostAccumulation.Item2.Item2;
                int thisSubcat = subcatchments[mostAccumulation.Item2.Item1, mostAccumulation.Item2.Item2];
                float minElev = 99999;
                float accElev = 0;
                float accDistance = 0;
                bool keepIterating = true;
                while (keepIterating)
                {
                    keepIterating = false;
                    int x = mostAccumulation.Item2.Item1;
                    int y = mostAccumulation.Item2.Item2;


                    mostAccumulation = (-999, (startX, startY)); // (Accumulation, (x,y))
                    for (int i = x - 1; i <= x + 1; i++)
                    {
                        if (i < 0 || i > numRows - 1) { continue; }
                        for (int j = y - 1; j <= y + 1; j++)
                        {
                            if (i == x && j == y) { continue; }
                            if (j < 0 || j > numCols - 1) { continue; }
                            if (elev[i, j] == noData_val) { continue; }
                            if (subcatchments[i, j] == default) { continue; }

                            //if (drainageMap[i, j] == dd[(i - x, j - y)])
                            if (drainageMap[i, j] == d8[i - x + 1, j - y + 1])
                            {
                                if (accumulation[i, j] > mostAccumulation.Item1 && accumulation[i, j] < accumulation[x, y])
                                {
                                    if (model == "Mydro")
                                    {
                                        if (subcatchments[i, j] == subcatchments[x, y] && accumulation[i, j] > catchMaxAccs[subcatchments[x, y] - 1] * 0.125)
                                        {
                                            mostAccumulation = (accumulation[i, j], (i, j));
                                            keepIterating = true;
                                        }
                                        else
                                        {
                                            mostAccumulation = (accumulation[i, j], (i, j));
                                            keepIterating = false;
                                        }
                                    }
                                    else if (model == "URBS")
                                    {
                                        if (subcatchments[i, j] == subcatchments[x, y])
                                        {
                                            mostAccumulation = (accumulation[i, j], (i, j));
                                            keepIterating = true;
                                        }
                                        else
                                        {
                                            mostAccumulation = (accumulation[i, j], (i, j));
                                            keepIterating = false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    double srs_y; double srs_x;
                    (srs_x, srs_y) = GetSRS_XY(y, x, geotransform);
                    try
                    {
                        channelLines[subcatchments[x, y] - 1].AddPoint_2D(srs_x, srs_y);
                    }
                    catch
                    {
                        Console.WriteLine($"ERROR WITH OUTLET AT CELL X:{userOutlet[0].Item1}, Y:{userOutlet[0].Item2}");
                        Console.WriteLine($"Check Outlets span a well defined channel - Check Accumulation Raster against Outlets Layer");
                        Environment.Exit(-1);
                    }
                    
                    channel[x, y] = subcatchments[x, y];
                    float distance;
                    if (mostAccumulation.Item2.Item1 != x && mostAccumulation.Item2.Item2 != y)
                    {
                        distance = (float)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) / 1.08);
                        // distance = (float)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) / 1.17586);
                    }
                    else
                    {
                        distance = (float)(Math.Sqrt(Math.Pow((mostAccumulation.Item2.Item1 - x) * dx, 2) + Math.Pow((mostAccumulation.Item2.Item2 - y) * dy, 2)) * 1.01);
                        // distance = (float)(Math.Sqrt(Math.Pow((mostAccumulation.Item2.Item1 - x) * dx, 2) + Math.Pow((mostAccumulation.Item2.Item2 - y) * dy, 2)) * 1.01959);
                    }
                    float elevation = elev[mostAccumulation.Item2.Item1, mostAccumulation.Item2.Item2];
                    minElev = Math.Min(minElev, elevation);
                    accDistance += distance;
                    accElev += distance * elevation;

                }
                channelSlopes[thisSubcat - 1] = ((accElev / accDistance) - minElev) / (accDistance / 2);
                channelLengths[thisSubcat - 1] = accDistance;
                // Calculate catchment equal area slope
                minElev = 99999;
                accElev = 0;
                accDistance = 0;
                keepIterating = true;


                mostAccumulation = (startAcc, (startX, startY));
                // Calculate Subcatchment equal area slope
                while (keepIterating)
                {
                    keepIterating = false;
                    int x = mostAccumulation.Item2.Item1;
                    int y = mostAccumulation.Item2.Item2;


                    mostAccumulation = (-999, (0, 0)); // (Accumulation, (x,y))
                    for (int i = x - 1; i <= x + 1; i++)
                    {
                        if (i < 0 || i > numRows - 1) { continue; }
                        for (int j = y - 1; j <= y + 1; j++)
                        {
                            if (i == x && j == y) { continue; }
                            if (j < 0 || j > numCols - 1) { continue; }
                            if (elev[i, j] == noData_val) { continue; }
                            if (catchments[i, j] == default) { continue; }

                            //if (drainageMap[i, j] == dd[(i - x, j - y)])
                            if (drainageMap[i, j] == d8[i - x + 1, j - y + 1])
                            {
                                if (accumulation[i, j] > mostAccumulation.Item1 && accumulation[i, j] < accumulation[x, y])
                                {

                                    mostAccumulation = (accumulation[i, j], (i, j));
                                    keepIterating = true;

                                }
                            }
                        }
                    }
                    if (keepIterating)
                    {
                        float distance;
                        if (mostAccumulation.Item2.Item1 != x && mostAccumulation.Item2.Item2 != y)
                        {
                            distance = (float)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) / 1.08);
                            // distance = (float)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) / 1.17586);
                        }
                        else
                        {
                            distance = (float)(Math.Sqrt(Math.Pow((mostAccumulation.Item2.Item1 - x) * dx, 2) + Math.Pow((mostAccumulation.Item2.Item2 - y) * dy, 2)) * 1.01);
                            // distance = (float)(Math.Sqrt(Math.Pow((mostAccumulation.Item2.Item1 - x) * dx, 2) + Math.Pow((mostAccumulation.Item2.Item2 - y) * dy, 2)) * 1.01959);
                        }
                        float elevation = elev[mostAccumulation.Item2.Item1, mostAccumulation.Item2.Item2];
                        minElev = Math.Min(minElev, elevation);
                        accDistance += distance;
                        accElev += distance * elevation;
                    }

                }
                channelCatchSlopes[thisSubcat - 1] = ((accElev / accDistance) - minElev) / (accDistance / 2);
                channelCatchLengths[thisSubcat - 1] = accDistance;
            }

            for (int i = 0; i < channelLines.Count; i++)
            {
                FeatureDefn featureDefn = streams_layer.GetLayerDefn();
                Feature feature = new Feature(featureDefn);

                feature.SetField("ID", i + 1);  // Set feature ID
                feature.SetField("Slope", Math.Round(Math.Max(channelSlopes[i], 0.0005), 5));  // Set feature ID
                Geometry originalLineGeometry = channelLines[i];
                Geometry utmGeometry = originalLineGeometry.Clone();
                try
                {
                    utmGeometry.TransformTo(utmSRS);
                }
                catch
                {
                    Console.WriteLine("WARNING: USE A LOCAL PROJECTION CALCULATIONS OF LENGTH AND SLOPE WILL BE MALFORMED");
                }
                double lengthInMeters = utmGeometry.Length();
                subcatchmentsInfo[i + 1].channelLength = lengthInMeters / 1000;
                feature.SetField("Length", lengthInMeters / 1000);
                feature.SetGeometry(channelLines[i]);
                streams_layer.CreateFeature(feature);
                feature.Dispose();
            }
            streams_layer.Dispose();
            streams_dataSource.Dispose();

            Console.WriteLine("Stream Paths Written");

            for (int i = 0; i < channelLengths.Count; i++)
            {
                subcatchmentsInfo[i + 1].ToC = ((float)Math.Round(58 * (channelCatchLengths[i] / 1000) / ((float)Math.Pow(subcatchmentsInfo[i + 1].us_area, 0.1f) * (float)Math.Pow(channelCatchSlopes[i] * 1000f, 0.2f)), 5));
            }

            subcatLayer.ResetReading();
            while ((subCatFeature = subcatLayer.GetNextFeature()) != null)
            {
                // Get the value of the "ID" field
                int id = subCatFeature.GetFieldAsInteger("ID");

                // Check if the "ID" field matches the target index i
                if (id > 0 && id <= subcatchmentsInfo.Count) // Check if ID is within the range of the areas array
                {

                    subCatFeature.SetField("ToC_min", subcatchmentsInfo[id].ToC); // Assuming "ID" starts from 1
                    subcatLayer.SetFeature(subCatFeature);
                }
            }
            stopwatch.Stop();
            Console.WriteLine($"Time of concentration. Time: {stopwatch.ElapsedMilliseconds}");

            

            Dictionary<int, List<(float, float)>> catchmentSlopes = new Dictionary<int, List<(float, float)>>(); // Slope, Distance for distance weighted average
            for (int i = 1; i < catchmentID; i++)
            {
                catchmentSlopes.Add(i, new List<(float, float)>());
            }

            List<int> xIndicies = new List<int>();
            for (int i = 0; i < numRows; i++)
            {
                xIndicies.Add(i);
            }
            Shuffle(xIndicies);

            List<int> yIndicies = new List<int>();
            for (int i = 0; i < numCols; i++)
            {
                yIndicies.Add(i);
            }
            Shuffle(yIndicies);

            foreach (int topX in xIndicies) {
                foreach (int topY in yIndicies)
                {
                    int slopeCatch = subcatchments[topX, topY];
                    if (subcatchments[topX, topY] == default)
                    {
                        continue;
                    }
                    if (cellNeighbours[topX, topY] > 0)
                    {
                        continue;
                    }
                    if (catchmentSlopes[slopeCatch].Count >= 100)
                    {
                        continue;
                    }
                    float distance = 0;
                    float minElev = 99999;
                    float sumElevDist = 0;
                    int currentCellX = topX;
                    int currentCellY = topY;

                    while (true)
                    {
                        if (drainageMap[currentCellX, currentCellY] == default) { break; }
                        (sbyte, sbyte) cellDrainageDirection = reverseD8[drainageMap[currentCellX, currentCellY]];

                        int nextCellX = currentCellX - cellDrainageDirection.Item1;
                        int nextCellY = currentCellY - cellDrainageDirection.Item2;
                        if (model == "Mydro")
                        {
                            if (channel[nextCellX, nextCellY] == default || accumulation[nextCellX, nextCellY] < catchMaxAccs[slopeCatch - 1] * 0.125)
                            {
                                float cellDistance;
                                if (currentCellX != nextCellX && currentCellY != nextCellY)
                                {
                                    cellDistance = (float)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) / 1.08);
                                    // cellDistance = (float)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) / 1.17586);
                                }
                                else
                                {
                                    cellDistance = (float)(Math.Sqrt(Math.Pow((nextCellX - currentCellX) * dx, 2) + Math.Pow((nextCellY - currentCellY) * dy, 2)) * 1.01);
                                    // cellDistance = (float)(Math.Sqrt(Math.Pow((nextCellX - currentCellX) * dx, 2) + Math.Pow((nextCellY - currentCellY) * dy, 2)) * 1.01959);
                                }
                                distance += cellDistance;
                                minElev = Math.Min(minElev, elev[nextCellX, nextCellY]);
                                sumElevDist += cellDistance * elev[nextCellX, nextCellY];
                                currentCellX = nextCellX;
                                currentCellY = nextCellY;
                            }
                            else
                            {
                                break;
                            }
                        }
                        if (model == "URBS")
                        {
                            if (subcatchments[nextCellX, nextCellY] == slopeCatch)
                            {
                                float cellDistance = (float)Math.Sqrt(Math.Pow((currentCellX - nextCellX) * dx, 2) + Math.Pow((currentCellY - nextCellY) * dy, 2));
                                distance += cellDistance;
                                minElev = Math.Min(minElev, elev[nextCellX, nextCellY]);
                                sumElevDist += cellDistance * elev[nextCellX, nextCellY];
                                currentCellX = nextCellX;
                                currentCellY = nextCellY;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    if (distance > 0)
                    {
                        catchmentSlopes[slopeCatch].Add((((sumElevDist / distance) - minElev) / (distance / 2), distance));
                    }
                }
            }

            // VEC FILE WRITING
            stopwatch.Start();


            if (model == "URBS") { writeUrbsfile(); }
            else if (model == "Mydro") { writeMydrofile(); }
            else { Console.WriteLine($"INVALID MODEL: {model}, Use URBS, or MYDRO scripts to convert URBS to RORB and WBNM are available in model package"); }

            stopwatch.Stop();
            Console.WriteLine($"Vec File Written. Time: {stopwatch.ElapsedMilliseconds}");
            List<float> catchmentAverageSlopes = new List<float>();
            List<float> catchmentAverageLengths = new List<float>();
            if (model == "Mydro")
            {

                for (int i = 1; i < catchmentID; i++)
                {
                    List<(float, float)> weightedSlopes = catchmentSlopes[i];
                    float weightedSum = weightedSlopes.Sum(item => item.Item1 * item.Item2);
                    float totalLength = weightedSlopes.Sum(item => item.Item2);
                    float hillLength = totalLength / weightedSlopes.Count;
                    float weightedAverage = (float)Math.Round(weightedSum / totalLength, 5);
                    catchmentAverageSlopes.Add(weightedAverage);
                    catchmentAverageLengths.Add(hillLength);
                }

            }

            else if (model == "URBS")
            {
                using (StreamWriter writer = new StreamWriter(Path.Combine(outputDir, "_SubcatFile.csv")))
                {
                    writer.WriteLine("Index,Area,U,UF,CS,I");
                    for (int i = 1; i < catchmentID; i++)
                    {
                        List<(float, float)> weightedSlopes = catchmentSlopes[i];
                        float weightedSum = weightedSlopes.Sum(item => item.Item1 * item.Item2);
                        float totalLength = weightedSlopes.Sum(item => item.Item2);
                        float hillLength = totalLength / weightedSlopes.Count;
                        float weightedAverage = weightedSum / totalLength;
                        float catchmentSlope = (float)Math.Round(Math.Max(weightedAverage, 0.001), 4);
                        catchmentAverageSlopes.Add(catchmentSlope);
                        catchmentAverageLengths.Add(hillLength);
                        writer.WriteLine($"{i},{Math.Round(subcatchmentsInfo[i].area, 5)},0,0," + $"{catchmentSlope},0.0");
                    }

                }

            }
            subcatLayer.ResetReading();
            while ((subCatFeature = subcatLayer.GetNextFeature()) != null)
            {
                // Get the value of the "ID" field
                int id = subCatFeature.GetFieldAsInteger("ID");

                // Check if the "ID" field matches the target index i
                if (id > 0 && id <= subcatchmentsInfo.Count) // Check if ID is within the range of the areas array
                {
                    subCatFeature.SetField("DS", subcatchmentsInfo[id].dsCatchment); // Assuming "ID" starts from 1
                    if (model == "Mydro")
                    {
                        subCatFeature.SetField("HL", catchmentAverageLengths[id - 1] / 1000); // Assuming "ID" starts from 1
                        subCatFeature.SetField("HS", catchmentAverageSlopes[id - 1]); // Assuming "ID" starts from 1
                    }else
                    {
                        subCatFeature.SetField("CS", catchmentAverageSlopes[id - 1]); // Assuming "ID" starts from 1
                    }
                    subcatLayer.SetFeature(subCatFeature);
                }
            }
            Console.WriteLine($"Determined Catchment Slope");

            subcats_dataSource.Dispose();
            return;
        }
    }
}
