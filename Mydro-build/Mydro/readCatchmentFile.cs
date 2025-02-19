using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;

namespace Mydro
{
    class readCatchmentFile
    {
        public Dictionary<string, Subcatchment> subcats = new Dictionary<string, Subcatchment>();
        public readCatchmentFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File does not exist at path {filePath}.");
                Environment.Exit(1);
            }
            GdalConfiguration.ConfigureGdal();
            Gdal.AllRegister(); // Register all GDAL drivers
            OSGeo.OGR.Driver shpDriver = Ogr.GetDriverByName("ESRI Shapefile");
            DataSource dataSource = shpDriver.Open(filePath, 0);
            if (dataSource == null)
            {
                throw new Exception($"File '{filePath}' is not compatible with the GDAL ESRI Shapefile driver.");
            }
            Layer layer = dataSource.GetLayerByIndex(0);
            if (layer == null)
            {
                throw new Exception($"File '{filePath}' does not contain a valid layer.");
            }

            try
            {
                Feature feature;
                layer.ResetReading();

                while ((feature = layer.GetNextFeature()) != null)
                {
                    try
                    {
                        Dictionary<string, object> rowDict = new Dictionary<string, object>();

                        // Validate and get fields
                        if (!feature.IsFieldSet("ID")) throw new Exception("Missing 'ID' field.");
                        rowDict["ID"] = feature.GetFieldAsString("ID");

                        if (!feature.IsFieldSet("Area")) throw new Exception("Missing 'Area' field.");
                        rowDict["Area"] = feature.GetFieldAsDouble("Area");

                        if (!feature.IsFieldSet("HL")) throw new Exception("Missing 'HL' field.");
                        rowDict["HL"] = feature.GetFieldAsDouble("HL");

                        if (!feature.IsFieldSet("HS")) throw new Exception("Missing 'HS' field.");
                        rowDict["HS"] = feature.GetFieldAsDouble("HS");

                        if (!feature.IsFieldSet("I")) throw new Exception("Missing 'I' field.");
                        rowDict["I"] = feature.GetFieldAsDouble("I");

                        if (!feature.IsFieldSet("B")) throw new Exception("Missing 'B' field.");
                        rowDict["B"] = feature.GetFieldAsDouble("B");

                        if (!feature.IsFieldSet("m")) throw new Exception("Missing 'm' field.");
                        rowDict["m"] = feature.GetFieldAsDouble("m");

                        // Create Subcatchment and add to dictionary
                        Subcatchment subby = new Subcatchment(rowDict);
                        subcats[(string)subby.Properties["ID"]] = subby;
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Error processing feature: {innerEx.Message}");
                        Environment.Exit(-1);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical error while reading layer: {ex.Message}");
                Environment.Exit(-1);
            }
        }
    }
}
