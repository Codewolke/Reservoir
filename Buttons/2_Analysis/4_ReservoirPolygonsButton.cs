using System;
using System.Collections.Generic;
using System.Linq;
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
    internal class CreateReservoirPolygonsButton : Button
    {
        private static SpatialReference SpatialReference;
        private static List<ReservoirSurface> surfaces;
        private static BasicFeatureLayer reservoirSurfacesLayer;
        private static BasicFeatureLayer contourLayer;
        private static BasicFeatureLayer damLayer;
        private static List<long> ContoursToProcess = new List<long>();
        protected override async void OnClick()
        {
            SharedFunctions.Log("Starting To Create Reservoir Polygons");
            DateTime startTime = DateTime.Now;
            surfaces = new List<ReservoirSurface>();

            try
            {
                await Project.Current.SaveEditsAsync();
                await QueuedTask.Run(async () =>
                {
                    if (!SharedFunctions.LayerExists("DamCandidates") || !SharedFunctions.LayerExists("Contours"))
                        return;
                    damLayer = MapView.Active.Map.FindLayers("DamCandidates").FirstOrDefault() as BasicFeatureLayer;
                    contourLayer = MapView.Active.Map.FindLayers("Contours").FirstOrDefault() as BasicFeatureLayer;

                    SpatialReference = damLayer.GetSpatialReference();
                    reservoirSurfacesLayer = await CreatePolygonFeatureClass("ReservoirSurfaces");

                    ConstructPolygons();
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
        public async static Task<BasicFeatureLayer> CreatePolygonFeatureClass(string name)
        {
            var existingLayer = MapView.Active.Map.FindLayers(name).FirstOrDefault();
            if (existingLayer != null)
                return existingLayer as BasicFeatureLayer;
            List<object> arguments = new List<object> { CoreModule.CurrentProject.DefaultGeodatabasePath, name, "POLYGON", "", "DISABLED", "ENABLED" };
            arguments.Add(SpatialReference);
            IGPResult result = await Geoprocessing.ExecuteToolAsync("CreateFeatureclass_management", Geoprocessing.MakeValueArray(arguments.ToArray()));
            var layer = MapView.Active.Map.FindLayers(name).FirstOrDefault() as BasicFeatureLayer;
            await SharedFunctions.ExecuteAddFieldTool(layer, "DamID", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "ContourHeight", "SHORT");

            return layer;
        }

        private async static void ConstructPolygons()
        {
            List<CandidateDam> candidates = new List<CandidateDam>();
            SharedFunctions.LoadDamCandidatesFromLayer(candidates, damLayer);
            List<Contour> contours = new List<Contour>();
            SharedFunctions.LoadContoursFromLayer(contours, contourLayer);
            //select only contours, that actually have candidate dams on it
            contours = contours.Where(c => candidates.Any(d => d.ContourID == c.ObjectID)).ToList();
            bool multiThreading = (Parameter.MultiThreadingBox == null || !Parameter.MultiThreadingBox.IsChecked.HasValue || Parameter.MultiThreadingBox.IsChecked.Value);
            //ArcGIS.Core.Geometry Tools currently don't seem to support multithreading. 
            //Question https://community.esri.com/thread/243147-multithreading-parallel-processing-in-arcgis-pro-addin
            //received no answer so far. Until a solution is found, multithreading logic has to be deactivated
            multiThreading = false;

            if (multiThreading)
            {
                List<PolylineBuilder> polylineBuilders = new List<PolylineBuilder>();
                for (int i = 0; i < Environment.ProcessorCount; i++)
                {
                    polylineBuilders.Add(new PolylineBuilder(SpatialReference));
                }

                ContoursToProcess = contours.Select(c => c.ObjectID).ToList();
                SharedFunctions.Log("Divided work into " + Environment.ProcessorCount + " threads for all logical Processors...");
                await Task.WhenAll(polylineBuilders.Select(c => Task.Run(//Enumerable.Range(1, Environment.ProcessorCount).Select(c => Task.Run(
                    async () =>
                    {
                        while (ContoursToProcess.Count > 0)
                        {
                            long contourID = -1;
                            lock (ContoursToProcess)
                            {
                                contourID = ContoursToProcess.FirstOrDefault();
                                if (contourID != 0)
                                    ContoursToProcess.Remove(contourID);
                            }
                            if (contourID != -1)
                            {
                                var calc = new List<Contour>() { contours.Single(d => d.ObjectID == contourID) };
                                await PolygonsForContours(candidates, calc, c);
                            }
                        }
                    }
                    ))
                    );
            }
            else await PolygonsForContours(candidates, contours, new PolylineBuilder(SpatialReference));

            SharedFunctions.Log("Save all " + surfaces.Count + " surfaces");
            foreach (var surface in surfaces)
            {
                var attributes = new Dictionary<string, object>
                                {
                                    { "Shape", surface.Polygon },
                                    { "DamID", (long)surface.DamID },
                                    { "ContourHeight", (short)surface.ContourHeight }
                                };
                var createOperation = new EditOperation() { Name = "Create reservoir polygon", SelectNewFeatures = false };
                createOperation.Create(reservoirSurfacesLayer, attributes);
                await createOperation.ExecuteAsync();
            }
            await Project.Current.SaveEditsAsync();
        }

        private static async Task PolygonsForContours(List<CandidateDam> candidates, List<Contour> contours, PolylineBuilder polylineBuilder)
        {
            foreach (var contour in contours)
            {
                var contourGeometry = contour.Polyline;
                int counter = 0;
                int contourHeight = 0;
                foreach (var candidate in candidates.Where(c => c.ContourID == contour.ObjectID).ToList())
                {
                    try
                    {
                        while (polylineBuilder.CountParts > 0)
                        {
                            polylineBuilder.RemovePart(0);
                        }

                        //add the full contour
                        polylineBuilder.AddParts(contourGeometry.Parts);
                        //split at the endpoint
                        polylineBuilder.SplitAtDistance(candidate.EndPointDistance, false, true);

                        if (candidate.DamSpansContourStart)
                        {
                            //remove the part of the contour after the endpoint
                            //split at the startpoint
                            if (candidate.StartPointDistance != 0)
                            {
                                polylineBuilder.SplitAtDistance(candidate.StartPointDistance, false, true);
                                //remove the part of the polyline before the startpoint
                                polylineBuilder.RemovePart(1);
                            }
                            //Handle the situation, when the startpoint is on the very beginning of the contour line
                            else
                            {
                                polylineBuilder.RemovePart(0);
                            }
                        }
                        else
                        {
                            //remove the part of the contour after the endpoint
                            polylineBuilder.RemovePart(1);
                            //split at the startpoint
                            if (candidate.StartPointDistance != 0)
                            {
                                polylineBuilder.SplitAtDistance(candidate.StartPointDistance, false, true);
                                //remove the part of the polyline before the startpoint
                                polylineBuilder.RemovePart(0);
                            }
                        }
                        var newPolygon3D = PolygonBuilder.CreatePolygon(polylineBuilder.ToGeometry().Copy3DCoordinatesToList(), SpatialReference);

                        ReservoirSurface surface = new ReservoirSurface();
                        surface.Polygon = GeometryEngine.Instance.Move(newPolygon3D, 0, 0, candidate.ContourHeight) as Polygon;
                        surface.DamID = (long)candidate.ObjectID;
                        surface.ContourHeight = (short)candidate.ContourHeight;
                        surfaces.Add(surface);

                        counter++;
                        contourHeight = candidate.ContourHeight;
                    }
                    catch (Exception ex)
                    {
                        SharedFunctions.Log("Error for DamID " + candidate.ObjectID + " for ContourHeight " + candidate.ContourHeight + ": (" + ex.Message + ")");
                    }
                }

                SharedFunctions.Log("Created " + surfaces.Count.ToString("N0") + " Polygons ... " + counter.ToString("N0") + " for Contour " + contour.ObjectID + " (" + contour.Height + " m)");
            }
        }
    }
}