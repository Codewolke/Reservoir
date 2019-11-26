using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Core;
using System.Threading.Tasks;
using ArcGIS.Desktop.Editing;

namespace Reservoir
{
    /// <summary>
    /// Loops through all dam candidates and selects other dams that match the following criteria:
    /// 1. dam center point within 10.000 m of the current dam center point
    /// 2. height difference between low point of upper reservoir (contourHeight - damheight) and high point of lower reservoir (contourHeight) >= 10 m
    /// 3. reservoir polygons of the two reservoirs must noch overlap (ALREADY COVERED WITH STEP 1!)
    /// </summary>
    internal class PairReservoirsButton : Button
    {
        private static SpatialReference SpatialReference;
        protected override async void OnClick()
        {
            SharedFunctions.Log("Starting To Pair Reservoirs");
            DateTime startTime = DateTime.Now;

            await Project.Current.SaveEditsAsync();

            try
            {
                await QueuedTask.Run(async () =>
                {
                    if (!SharedFunctions.LayerExists("DamCandidates") || !SharedFunctions.LayerExists("ReservoirSurfaces"))
                        return;
                    var damCandidatesLayer = MapView.Active.Map.FindLayers("DamCandidates").FirstOrDefault();
                    var reservoirSurfacesLayer = MapView.Active.Map.FindLayers("ReservoirSurfaces").FirstOrDefault();

                    SpatialReference = damCandidatesLayer.GetSpatialReference();
                    var reservoirPairsLayer = await CreateFeatureClass("ReservoirPairs");

                    await FindPairs(reservoirPairsLayer, reservoirSurfacesLayer as BasicFeatureLayer, damCandidatesLayer as BasicFeatureLayer);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                DateTime endTime = DateTime.Now;
                SharedFunctions.Log("Analysed in " + (endTime - startTime).TotalSeconds.ToString() + " seconds");
            }
        }
        public async static Task<BasicFeatureLayer> CreateFeatureClass(string name)
        {
            var existingLayer = MapView.Active.Map.FindLayers(name).FirstOrDefault();
            if (existingLayer != null)
                return existingLayer as BasicFeatureLayer;
            List<object> arguments = new List<object> { CoreModule.CurrentProject.DefaultGeodatabasePath, name, "POLYLINE", "", "DISABLED", "ENABLED" };
            arguments.Add(SpatialReference);
            IGPResult result = await Geoprocessing.ExecuteToolAsync("CreateFeatureclass_management", Geoprocessing.MakeValueArray(arguments.ToArray()));
            var layer = MapView.Active.Map.FindLayers(name).FirstOrDefault() as BasicFeatureLayer;
            await SharedFunctions.ExecuteAddFieldTool(layer, "LowerDamID", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "UpperDamID", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "CapacityInMWh", "FLOAT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "Distance", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "LowerHeight", "SHORT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "UpperHeight", "SHORT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "CapacityDistanceRatio", "FLOAT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "UsableHeightDifference", "SHORT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "CapacityUtilization", "FLOAT");

            return layer;
        }

        private async static Task<bool> FindPairs(BasicFeatureLayer reservoirPairsLayer, BasicFeatureLayer reservoirSurfacesLayer, BasicFeatureLayer damLayer)
        {
            List<ReservoirPair> reservoirPairs = new List<ReservoirPair>();
            List<CandidateDam> res1List = new List<CandidateDam>();
            SharedFunctions.LoadDamCandidatesFromLayer(res1List, damLayer);
            List<ReservoirSurface> reservoirSurfaceList = new List<ReservoirSurface>();
            SharedFunctions.LoadReservoirSurfacesFromLayer(reservoirSurfaceList, reservoirSurfacesLayer);
            List<CandidateDam> res2List = new List<CandidateDam>(res1List);
            int analyzedCounter = 0;
            int createdCounter = 0;
            foreach (var dam1 in res1List)
            {
                res2List.Remove(dam1);
                foreach (var dam2 in res2List)
                {
                    try
                    {
                        analyzedCounter++;
                        CandidateDam lowerDam = null;
                        CandidateDam upperDam = null;
                        if (dam1.ContourHeight > dam2.ContourHeight)
                        {
                            lowerDam = dam2;
                            upperDam = dam1;
                        }
                        else
                        {
                            lowerDam = dam1;
                            upperDam = dam2;
                        }

                        //check for height-difference:                        
                        int usableHeightDifference = upperDam.ContourHeight - upperDam.DamHeight - lowerDam.ContourHeight;
                        if (usableHeightDifference < 50)
                            continue;

                        //check for horizontal distance
                        Coordinate3D lowerDamCenter = new Coordinate3D((lowerDam.StartPoint.X + lowerDam.EndPoint.X) / 2, (lowerDam.StartPoint.Y + lowerDam.EndPoint.Y) / 2, (lowerDam.StartPoint.Z + lowerDam.EndPoint.Z) / 2);
                        Coordinate3D upperDamCenter = new Coordinate3D((upperDam.StartPoint.X + upperDam.EndPoint.X) / 2, (upperDam.StartPoint.Y + upperDam.EndPoint.Y) / 2, (upperDam.StartPoint.Z + upperDam.EndPoint.Z) / 2);
                        decimal distanceBetweenDamCenters = SharedFunctions.DistanceBetween(lowerDamCenter, upperDamCenter);
                        if (distanceBetweenDamCenters > 5000)
                            continue;

                        //check for reservoir overlap //NOT NECCESSARY due to height-difference check, where all corresponding cases are already filtered
                        //if (GeometryEngine.Instance.Overlaps(reservoirSurfaceList.Single(c => c.DamID == dam1.ObjectID).Polygon, reservoirSurfaceList.Single(c => c.DamID == dam1.ObjectID).Polygon))
                        //    continue;

                        //calculate CapacityInMWh:
                        long usableVolume = upperDam.ReservoirVolume;
                        if (lowerDam.ReservoirVolume < usableVolume)
                            usableVolume = lowerDam.ReservoirVolume;

                        //only assume 85% of the water as usable in reality
                        usableVolume = (long)(0.85 * usableVolume);
                        float capacityInMWh = (float)(1000 * 9.8 * usableHeightDifference * usableVolume * 0.9) / (float)((long)3600 * 1000000);

                        //calculate utilizationPercentage:
                        float capacityUtilization = 0;
                        if (upperDam.ReservoirVolume != 0)
                            capacityUtilization = (float)lowerDam.ReservoirVolume / upperDam.ReservoirVolume;
                        if (capacityUtilization > 1)
                            capacityUtilization = 1 / (float)capacityUtilization;

                        //check for Utilization of at least 75%
                        if (capacityUtilization < 0.75)
                            continue;

                        decimal capacityDistanceRatio = (decimal)capacityInMWh * 100 / distanceBetweenDamCenters;

                        List<Coordinate3D> coordinates = new List<Coordinate3D>() { lowerDamCenter, upperDamCenter };
                        Polyline polyline = PolylineBuilder.CreatePolyline(coordinates);

                        ReservoirPair reservoirPair = new ReservoirPair();
                        reservoirPair.LowerDam = lowerDam;
                        reservoirPair.UpperDam = upperDam;
                        reservoirPair.LowerDamCenter = lowerDamCenter;
                        reservoirPair.UpperDamCenter = upperDamCenter;
                        reservoirPair.Polyline = polyline;
                        reservoirPair.LowerDamID = lowerDam.ObjectID;
                        reservoirPair.UpperDamID = upperDam.ObjectID;
                        reservoirPair.CapacityInMWh = capacityInMWh;
                        reservoirPair.Distance = distanceBetweenDamCenters;
                        reservoirPair.LowerHeight = lowerDam.ContourHeight;
                        reservoirPair.UpperHeight = upperDam.ContourHeight;
                        reservoirPair.CapacityDistanceRatio = capacityDistanceRatio;
                        reservoirPair.UsableHeightDifference = usableHeightDifference;
                        reservoirPair.CapacityUtilization = capacityUtilization;

                        reservoirPairs.Add(reservoirPair);
                    }
                    catch (Exception ex)
                    {
                        SharedFunctions.Log("Error for attempted Pair with dam1: " + dam1.ObjectID + " and dam2: " + dam2.ObjectID + " (" + ex.Message + ")");
                    }
                }
            }
            //try to further minimize the reservoirPairs selection by only keeping
            //the best connection within a buffer of 100 m (around both dam center points)
            List<ReservoirPair> pairsToDelete = new List<ReservoirPair>();
            foreach (var reservoirPair in reservoirPairs.ToList())
            {
                if (pairsToDelete.Contains(reservoirPair))
                    continue;
                var similarPairs = reservoirPairs.Where(c => SharedFunctions.DistanceBetween(reservoirPair.LowerDamCenter, c.LowerDamCenter) <= 100
                                          && SharedFunctions.DistanceBetween(reservoirPair.UpperDamCenter, c.UpperDamCenter) <= 100).ToList();
                if (similarPairs.Count > 1)
                {
                    var bestPair = similarPairs.OrderByDescending(c => c.CapacityDistanceRatio * (decimal)c.CapacityUtilization).First();
                    similarPairs.Remove(bestPair);
                    pairsToDelete = pairsToDelete.Union(similarPairs).ToList();
                }
            }
            foreach (var pairToDelete in pairsToDelete)
            {
                reservoirPairs.Remove(pairToDelete);
            }

            //insert the remaining objects into the DB
            foreach (var reservoirPair in reservoirPairs)
            {
                var attributes = new Dictionary<string, object>
                                {
                                    { "Shape", reservoirPair.Polyline },
                                    { "LowerDamID", (long)reservoirPair.LowerDamID },
                                    { "UpperDamID", (long)reservoirPair.UpperDamID },
                                    { "CapacityInMWh", (float)reservoirPair.CapacityInMWh },
                                    { "Distance", (long)reservoirPair.Distance },
                                    { "LowerHeight", (short)reservoirPair.LowerHeight },
                                    { "UpperHeight", (short)reservoirPair.UpperHeight },
                                    { "CapacityDistanceRatio", (float)reservoirPair.CapacityDistanceRatio },
                                    { "UsableHeightDifference", (short)reservoirPair.UsableHeightDifference },
                                    { "CapacityUtilization", (float)reservoirPair.CapacityUtilization }
                                };
                var createOperation = new EditOperation() { Name = "Create reservoir pair", SelectNewFeatures = false };
                createOperation.Create(reservoirPairsLayer, attributes);
                await createOperation.ExecuteAsync();
                createdCounter++;
            }

            SharedFunctions.Log(analyzedCounter + " combinations analysed and " + createdCounter + " viable pairs found");

            await Project.Current.SaveEditsAsync();

            return true;
        }
    }
}