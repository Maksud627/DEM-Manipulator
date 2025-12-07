using System;
using System.Collections.Generic;
using System.IO;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;

namespace DEMModifier;

public class DemProcessor
{
    public class LayerConfig
    {
        public string ShapefilePath { get; set; }
        public string AttributeColumn { get; set; }       

        public string FilterColumn { get; set; }           
        public List<string> FilterValues { get; set; }      
    }

    public static void ConfigureGdal()
    {
        try { MaxRev.Gdal.Core.GdalBase.ConfigureAll(); }
        catch (Exception ex) { throw new Exception("Failed to initialize GDAL.", ex); }
    }

    public List<string> GetShapefileAttributes(string shpPath)
    {
        var attributes = new List<string>();
        using (DataSource ds = Ogr.Open(shpPath, 0))
        {
            if (ds == null) throw new Exception("Could not open shapefile.");
            FeatureDefn defn = ds.GetLayerByIndex(0).GetLayerDefn();
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                attributes.Add(defn.GetFieldDefn(i).GetName());
            }
        }
        return attributes;
    }

    public List<string> GetUniqueColumnValues(string shpPath, string columnName)
    {
        var values = new HashSet<string>();     
        using (DataSource ds = Ogr.Open(shpPath, 0))
        {
            Layer layer = ds.GetLayerByIndex(0);
            Feature feat;
            while ((feat = layer.GetNextFeature()) != null)
            {
                string val = feat.GetFieldAsString(columnName);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    values.Add(val);
                }
                feat.Dispose();
            }
        }
        return new List<string>(values);
    }

    public void ProcessDem(string baseDemPath, List<LayerConfig> layers, string outputPath)
    {
        using (Dataset srcDem = Gdal.Open(baseDemPath, Access.GA_ReadOnly))
        {
            if (srcDem == null) throw new FileNotFoundException("Could not open Base DEM.");

            int width = srcDem.RasterXSize;
            int height = srcDem.RasterYSize;
            double[] geoTransform = new double[6];
            srcDem.GetGeoTransform(geoTransform);
            string projection = srcDem.GetProjection();

            float[] demData = new float[width * height];
            Band demBand = srcDem.GetRasterBand(1);
            demBand.ReadRaster(0, 0, width, height, demData, width, height, 0, 0);

            double noDataVal;
            int hasNoData;
            demBand.GetNoDataValue(out noDataVal, out hasNoData);

            OSGeo.GDAL.Driver memDriver = Gdal.GetDriverByName("MEM");

            foreach (var layerConfig in layers)
            {
                using (Dataset tempLayerRaster = memDriver.Create("", width, height, 1, DataType.GDT_Float32, null))
                {
                    tempLayerRaster.SetGeoTransform(geoTransform);
                    tempLayerRaster.SetProjection(projection);

                    double[] zeroBuffer = new double[width * height];
                    tempLayerRaster.GetRasterBand(1).WriteRaster(0, 0, width, height, zeroBuffer, width, height, 0, 0);

                    using (DataSource shpDs = Ogr.Open(layerConfig.ShapefilePath, 0))
                    {
                        Layer ogrLayer = shpDs.GetLayerByIndex(0);

                        if (!string.IsNullOrEmpty(layerConfig.FilterColumn) &&
                            layerConfig.FilterValues != null &&
                            layerConfig.FilterValues.Count > 0)
                        {
                            var sanitizedValues = new List<string>();
                            foreach (var v in layerConfig.FilterValues) sanitizedValues.Add(v.Replace("'", "''"));

                            string valueList = string.Join("', '", sanitizedValues);
                            string filterSql = $"\"{layerConfig.FilterColumn}\" IN ('{valueList}')";

                            ogrLayer.SetAttributeFilter(filterSql);
                        }
                        string[] options = new string[]
                        {
                            $"ATTRIBUTE={layerConfig.AttributeColumn}",
                            "ALL_TOUCHED=TRUE"
                        };

                        Gdal.RasterizeLayer(tempLayerRaster, 1, new int[] { 1 }, ogrLayer, IntPtr.Zero, IntPtr.Zero, 0, new double[] { }, options, null, null);

                        ogrLayer.SetAttributeFilter(null);

                        float[] layerData = new float[width * height];
                        tempLayerRaster.GetRasterBand(1).ReadRaster(0, 0, width, height, layerData, width, height, 0, 0);

                        for (int i = 0; i < demData.Length; i++)
                        {
                            if (hasNoData == 1 && Math.Abs(demData[i] - noDataVal) < 0.00001) continue;
                            if (layerData[i] != 0) demData[i] += layerData[i];
                        }
                    }
                }
            }

            OSGeo.GDAL.Driver gtiffDriver = Gdal.GetDriverByName("GTiff");
            using (Dataset outDs = gtiffDriver.CreateCopy(outputPath, srcDem, 0, null, null, null))
            {
                outDs.GetRasterBand(1).WriteRaster(0, 0, width, height, demData, width, height, 0, 0);
            }
        }
    }
}