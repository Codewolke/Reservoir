using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Core.Geoprocessing;

namespace Reservoir
{
    internal class VisualizeDamButton : Button
    {
        private static List<IDisposable> _overlayObjectList = new List<IDisposable>();
        private static SpatialReference SpatialReference;
        protected override async void OnClick()
        {
            SharedFunctions.Log("Starting To Create 3D Visualization");
            DateTime startTime = DateTime.Now;

            CIMLineSymbol symbol = SymbolFactory.Instance.ConstructLineSymbol(ColorFactory.Instance.GreyRGB, 5.0, SimpleLineStyle.Solid);
            CIMSymbolReference symbolReference = symbol.MakeSymbolReference();

            try
            {
                await QueuedTask.Run(async () =>
                {
                    if (!SharedFunctions.LayerExists("DamCandidates") || !SharedFunctions.LayerExists("ReservoirSurfaces"))
                        return;
                    var damCandidatesLayer = MapView.Active.Map.FindLayers("DamCandidates").FirstOrDefault();
                    var reservoirSurfacesLayer = MapView.Active.Map.FindLayers("ReservoirSurfaces").FirstOrDefault();

                    SpatialReference = damCandidatesLayer.GetSpatialReference();
                    var damVisLayer = await CreateMultiPatchFeatureClass("DamVisualization");
                    var reservoirVisLayer = await CreatePolygonFeatureClass("ReservoirVisualization");

                    Visualize(damVisLayer, reservoirVisLayer, reservoirSurfacesLayer as BasicFeatureLayer, damCandidatesLayer as BasicFeatureLayer);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                DateTime endTime = DateTime.Now;
                SharedFunctions.Log("Visualized in " + (endTime - startTime).TotalSeconds.ToString() + " seconds");
            }
        }
        public async static Task<BasicFeatureLayer> CreateMultiPatchFeatureClass(string name)
        {
            var existingLayer = MapView.Active.Map.FindLayers(name).FirstOrDefault();
            if (existingLayer != null)
                return existingLayer as BasicFeatureLayer;
            List<object> arguments = new List<object> { CoreModule.CurrentProject.DefaultGeodatabasePath, name, "MULTIPATCH", "", "DISABLED", "ENABLED" };
            arguments.Add(SpatialReference);
            IGPResult result = await Geoprocessing.ExecuteToolAsync("CreateFeatureclass_management", Geoprocessing.MakeValueArray(arguments.ToArray()));
            var layer = MapView.Active.Map.FindLayers(name).FirstOrDefault() as BasicFeatureLayer;
            await SharedFunctions.ExecuteAddFieldTool(layer, "DamID", "LONG");

            return layer;
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

            return layer;
        }
        private async static void Visualize(BasicFeatureLayer dam3dLayer, BasicFeatureLayer reservoirVisLayer, BasicFeatureLayer reservoirSurfacesLayer, BasicFeatureLayer damLayer)
        {
            SharedFunctions.DeleteAllFeatures(reservoirVisLayer);
            SharedFunctions.DeleteAllFeatures(dam3dLayer);

            List<long> damIDs = new List<long>();
            var reservoirPairsLayer = MapView.Active.Map.FindLayers("ReservoirPairs").FirstOrDefault();
            if (reservoirPairsLayer != null && ((BasicFeatureLayer)reservoirPairsLayer).SelectionCount > 0)
            {
                var reservoirPairsCursor = ((BasicFeatureLayer)reservoirPairsLayer).GetSelection().Search();
                while (reservoirPairsCursor.MoveNext())
                {
                    using (Row row = reservoirPairsCursor.Current)
                    {
                        int damId = (int)row["LowerDamId"];
                        if (!damIDs.Contains(damId))
                            damIDs.Add(damId);
                        damId = (int)row["UpperDamId"];
                        if (!damIDs.Contains(damId))
                            damIDs.Add(damId);
                    }
                }
            }
            if (damLayer.SelectionCount > 0)
            {
                var damCursor = damLayer.GetSelection().Search();
                while (damCursor.MoveNext())
                {
                    using (Row row = damCursor.Current)
                    {
                        int damId = (int)row["ObjectID"];
                        if (!damIDs.Contains(damId))
                            damIDs.Add(damId);
                    }
                }
            }
            List<CandidateDam> candidates = new List<CandidateDam>();
            SharedFunctions.LoadDamCandidatesFromLayer(candidates, damLayer);
            foreach (var dam in candidates.Where(c => damIDs.Contains(c.ObjectID)).ToList())
            {
                double contourHeight = dam.ContourHeight;

                try
                {
                    MapPoint startPoint = dam.StartPoint;
                    MapPoint endPoint = dam.EndPoint;
                    double damHeight = (double)dam.DamHeight;
                    double damLength = (double)dam.Length;

                    Coordinate3D coord1 = startPoint.Coordinate3D;
                    int factor = ((startPoint.Y < endPoint.Y && startPoint.X > endPoint.X)
                                    || (startPoint.Y > endPoint.Y && startPoint.X < endPoint.X)
                                    ) ? 1 : -1;
                    Coordinate3D coord2 = new Coordinate3D(coord1.X + factor * damHeight / damLength * Math.Abs(startPoint.Y - endPoint.Y) //X
                                                            , coord1.Y + damHeight / damLength * Math.Abs(startPoint.X - endPoint.X) //Y
                                                            , coord1.Z - damHeight); //Z
                    Coordinate3D coord3 = new Coordinate3D(coord1.X - factor * damHeight / damLength * Math.Abs(startPoint.Y - endPoint.Y) //X
                                                            , coord1.Y - damHeight / damLength * Math.Abs(startPoint.X - endPoint.X) //Y
                                                            , coord1.Z - damHeight); //Z

                    //Workaround for Bug in ArcGIS Pro 2.4.1: if values are equal, extrusion will fail
                    coord2.X += 0.1;
                    coord2.Y += 0.1;
                    coord3.X += 0.1;
                    coord3.Y += 0.1;
                    List<Coordinate3D> coords = new List<Coordinate3D>();
                    coords.Add(coord1);
                    coords.Add(coord2);
                    coords.Add(coord3);

                    var newPolygon3D = PolygonBuilder.CreatePolygon(coords, SpatialReference);
                    Coordinate3D coord = new Coordinate3D(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y, 0.1);
                    var multipatch = GeometryEngine.Instance.ConstructMultipatchExtrudeAlongVector3D(newPolygon3D, coord);
                    var attributes2 = new Dictionary<string, object>
                            {
                                { "Shape", multipatch },
                                { "DamID", (long)dam.ObjectID }
                            };
                    var createOperation2 = new EditOperation() { Name = "Create multipatch", SelectNewFeatures = false };
                    createOperation2.Create(dam3dLayer, attributes2);
                    await createOperation2.ExecuteAsync();

                    //add SurfacePolygon to Visualization:
                    var queryFilter = new QueryFilter { WhereClause = string.Format("DamID = {0}", dam.ObjectID) };
                    var surfaceCursor = reservoirSurfacesLayer.Select(queryFilter).Search();
                    if (surfaceCursor.MoveNext())
                    {
                        using (Row row = surfaceCursor.Current)
                        {
                            var polygon = (row as Feature).GetShape() as Polygon;
                            attributes2 = new Dictionary<string, object>
                            {
                                { "Shape", polygon },
                                { "DamID", (long)dam.ObjectID }
                            };
                            var createOperationSurface = new EditOperation() { Name = "Create surface", SelectNewFeatures = false };
                            createOperationSurface.Create(reservoirVisLayer, attributes2);
                            await createOperationSurface.ExecuteAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    SharedFunctions.Log("Error for 3D Dam with DamID " + dam.ObjectID + ": " + ex.Message);
                }

                SharedFunctions.Log("3D Dam created for Dam " + dam.ObjectID);
            }
            await Project.Current.SaveEditsAsync();
        }
    }
}
