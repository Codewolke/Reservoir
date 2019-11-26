using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.GeoProcessing;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reservoir
{
    public static class Global
    {
        public static string LogText;
        public static ReservoirOutputDockPaneViewModel LogVM;

    }
    public class ReservoirPair
    {
        public ReservoirPair()
        { }
        public long ObjectID { get; set; }
        public CandidateDam LowerDam { get; set; }
        public CandidateDam UpperDam { get; set; }
        public long LowerDamID { get; set; }
        public long UpperDamID { get; set; }
        public float CapacityInMWh { get; set; }
        public decimal Distance { get; set; }
        public int LowerHeight { get; set; }
        public int UpperHeight { get; set; }
        public int UsableHeightDifference { get; set; }
        public decimal CapacityDistanceRatio { get; set; }
        public float CapacityUtilization { get; set; }
        public Polyline Polyline { get; set; }
        public Coordinate3D LowerDamCenter { get; set; }
        public Coordinate3D UpperDamCenter { get; set; }
    }
    public class Contour
    {
        public Contour()
        { }
        public long ObjectID { get; set; }
        public int Height { get; set; }
        public Polyline Polyline { get; set; }
    }
    public class ReservoirSurface
    {
        public ReservoirSurface()
        { }
        public long ObjectID { get; set; }
        public long DamID { get; set; }
        public short ContourHeight { get; set; }        
        public Polygon Polygon { get; set; }
    }
    public class CandidateDam
    {
        public CandidateDam()
        { }
        public long ObjectID { get; set; }
        public long ContourID { get; set; }
        public decimal Length { get; set; }
        public MapPoint StartPoint { get; set; }
        public MapPoint EndPoint { get; set; }
        public Polyline Line { get; set; }
        public decimal DistanceOnLine { get; set; }
        public decimal Rating
        {
            get
            {
                return DistanceOnLine / Length;
            }
        }
        
        public long StartPointDistance { get; internal set; }
        public long EndPointDistance { get; internal set; }
        public long StartPointID { get; internal set; }
        public long EndPointID { get; internal set; }
        public int ContourHeight { get; internal set; }
        public decimal ZMin { get; internal set; }
        public long DamVolume { get; set; }
        public long ReservoirVolume { get; set; }
        public bool DamSpansContourStart { get; set; }
        public int DamHeight { get; internal set; }
    }

    public static class SharedFunctions
    {
        public static bool LayerExists(string layerName)
        {
            var layer = MapView.Active.Map.FindLayers(layerName).FirstOrDefault();
            if (layer == null)
                MessageBox.Show(string.Format("No \"{0}\" layer available.", layerName));
            return layer != null;
        }
        public async static void DeleteAllFeatures(BasicFeatureLayer layer)
        {
            var deleteOp = new EditOperation();
            deleteOp.Delete(layer, SharedFunctions.GetAllIdsFromLayer(layer));
            await deleteOp.ExecuteAsync();
        }
        public static List<long> GetAllIdsFromLayer(BasicFeatureLayer layer)
        {
            List<long> result = new List<long>();
            using (var cursor = layer.Search())
            {
                while (cursor.MoveNext())
                {
                    using (Row row = cursor.Current)
                    {
                        result.Add((int)row["ObjectID"]);
                    }
                }
            }
            return result;
        }
        public static void LoadDamCandidatesFromLayer(List<CandidateDam> candidates, BasicFeatureLayer damCandidatesLayer)
        {
            using (var cursor = damCandidatesLayer.Search())
            {
                while (cursor.MoveNext())
                {
                    using (Row row = cursor.Current)
                    {
                        CandidateDam candidate = new CandidateDam();
                        candidate.ObjectID = (int)row["ObjectID"];
                        candidate.ContourID = (int)row["ContourID"];
                        candidate.StartPointID = (int)row["StartPointID"];
                        candidate.EndPointID = (int)row["EndPointID"];
                        candidate.ContourHeight = Convert.ToInt32(row["ContourHeight"]);
                        candidate.DistanceOnLine = (int)row["DistanceOnLine"];
                        candidate.Length = Convert.ToInt32(row["Length"]);
                        candidate.StartPointDistance = Convert.ToInt32(row["StartPointDistance"]);
                        candidate.EndPointDistance = Convert.ToInt32(row["EndPointDistance"]);
                        candidate.DamSpansContourStart = Convert.ToInt32(row["DamSpansContourStart"]) == 1;
                        candidate.DamHeight = Convert.ToInt32(row["DamHeight"]);
                        candidate.Line = (row as Feature).GetShape() as Polyline;
                        candidate.StartPoint = candidate.Line.Points.First();
                        candidate.EndPoint = candidate.Line.Points.Last();
                        candidate.ReservoirVolume = Convert.ToInt64(row["ReservoirVolume"]);
                        candidates.Add(candidate);
                    }
                }
            }
        }
        public static void LoadContoursFromLayer(List<Contour> contours, BasicFeatureLayer contoursLayer)
        {
            using (var cursor = contoursLayer.Search())
            {
                while (cursor.MoveNext())
                {
                    using (Row row = cursor.Current)
                    {
                        Contour contour = new Contour();
                        contour.ObjectID = (int)row["ObjectID"];
                        contour.Polyline = (row as Feature).GetShape() as Polyline;
                        contour.Height = System.Convert.ToInt32(row["Contour"]);
                        contours.Add(contour);
                    }
                }
            }
        }
        public static void LoadReservoirSurfacesFromLayer(List<ReservoirSurface> surfaces, BasicFeatureLayer surfacesLayer)
        {
            using (var cursor = surfacesLayer.Search())
            {
                while (cursor.MoveNext())
                {
                    using (Row row = cursor.Current)
                    {
                        ReservoirSurface reservoirSurface = new ReservoirSurface();
                        reservoirSurface.ObjectID = (int)row["ObjectID"];
                        reservoirSurface.DamID = (int)row["DamID"];
                        reservoirSurface.Polygon = (row as Feature).GetShape() as Polygon;
                        surfaces.Add(reservoirSurface);
                    }
                }
            }
        }
        public static async Task<bool> ExecuteAddFieldTool(BasicFeatureLayer layer, string field, string fieldType, int? fieldLength = null, bool isNullable = true)
        {
            try
            {
                return await QueuedTask.Run(() =>
                {
                    var layerName = layer.Name;
                    var table = layer.GetTable();
                    var dataStore = table.GetDatastore();
                    var workspaceNameDef = dataStore.GetConnectionString();
                    var workspaceName = workspaceNameDef.Split('=')[1];

                    var fullSpec = System.IO.Path.Combine(workspaceName, layerName);
                    Log($@"{field} added -> {fullSpec}");

                    var parameters = Geoprocessing.MakeValueArray(fullSpec, field, fieldType.ToUpper(), null, null, fieldLength, null, isNullable ? "NULLABLE" : "NON_NULLABLE");
                    var results = Geoprocessing.ExecuteToolAsync("management.AddField", parameters, null, new CancellationTokenSource().Token,
                        (eventName, o) =>
                        {
                            //OnProgressPos
                        });
                    return true;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return false;
            }
        }
        public static decimal DistanceBetween(Coordinate3D pointA, Coordinate3D pointB)
        {
            return (decimal)Math.Sqrt(Math.Pow(pointA.X - pointB.X, 2) + Math.Pow(pointA.Y - pointB.Y, 2));
        }
        public static object lockingobject = new object();
        public static void Log(string logText)
        {
            try
            {
                lock (lockingobject)
                {
                    logText = DateTime.Now.ToString("HH:mm:ss: ") + logText;
                    Debug.WriteLine(logText);
                    Global.LogText = logText + Environment.NewLine + Global.LogText;
                    if (Global.LogVM != null)
                        Global.LogVM.LogText.Value = Global.LogText;
                }
            }
            catch (Exception)
            {
                
            }
        }
        public static async Task RunModel(IReadOnlyList<string> args, string ToolName)
        {
            Log("Tool " + ToolName + " started");
            DateTime startTime = DateTime.Now;
            try
            {
                await QueuedTask.Run(async () =>
                {
                    var reservoirToolbox = CoreModule.CurrentProject.Items.OfType<GeoprocessingProjectItem>().SingleOrDefault(c => c.Name == "Reservoir.tbx");
                    if (reservoirToolbox == null)
                    {
                        MessageBox.Show("Please add the Reservoir.tbx Toolbox to the project first!");
                        return;
                    }
                    var rucTool = reservoirToolbox.GetItems().SingleOrDefault(c => c.Name == ToolName);
                    if (rucTool == null)
                    {
                        MessageBox.Show("Please check the Reservoir.tbx Toolbox. Tool \"" + ToolName + "\" not found!");
                        return;
                    }

                    var progDlg = new ProgressDialog("Running Model "+ ToolName, "Cancel", 100, true);
                    progDlg.Show();

                    var progSrc = new CancelableProgressorSource(progDlg);

                    var result = await Geoprocessing.ExecuteToolAsync(rucTool.Path, args
                        , null, new CancelableProgressorSource(progDlg).Progressor, GPExecuteToolFlags.Default);
                    progDlg.Hide();

                    if (result.IsFailed)
                    {
                        foreach (var item in result.Messages.Where(c => c.ErrorCode != 0))
                        {
                            Log("ERROR:" + item.Text);
                        }
                    }
                });
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                DateTime endTime = DateTime.Now;
                Log("Tool " + ToolName + " finished in " + (endTime - startTime).TotalSeconds.ToString("N") + " seconds");
            }
        }

        internal static void ClearLog()
        {
            Global.LogText = "";
            Global.LogVM.LogText.Value = "";
        }
    }
    public class MyObservable<T> : INotifyPropertyChanged
    {
        private T _value;
        public T Value
        {
            get { return _value; }
            set { _value = value; NotifyPropertyChanged("Value"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void NotifyPropertyChanged(String propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
