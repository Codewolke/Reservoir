using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Threading;

namespace Reservoir
{
    internal class DetectCandiateDamsButton : Button
    {
        private static int pointsIntervalOnContour;
        private static SpatialReference SpatialReference;
        private static long PotentialCandidates = 0;
        private static long TotalPointsCount = 0;
        private static long PointsAnalyzed = 0;
        private static List<int> HeightsToProcess = new List<int>();
        private static Dictionary<int, double> ContourLengths = new Dictionary<int, double>();
        private static List<CandidateDam> chosenCandidates;
        private static CancelableProgressorSource cps;
        private static object lockingObject = new object();

        protected override async void OnClick()
        {
            SharedFunctions.Log("Search for candidate dams started");
            pointsIntervalOnContour = Convert.ToInt32(Parameter.PointIntervalBox.Text);
            DateTime startTime = DateTime.Now;
            chosenCandidates = new List<CandidateDam>();
            PotentialCandidates = 0;

            var pd = new ProgressDialog("Search for candidate dams", "Canceled", 100, false);
            cps = new CancelableProgressorSource(pd);
            cps.Progressor.Max = 100;
            PointsAnalyzed = 0;
            TotalPointsCount = 0;

            try
            {
                await Project.Current.SaveEditsAsync();
                BasicFeatureLayer layer = null;

                await QueuedTask.Run(async () =>
                {
                    if (!SharedFunctions.LayerExists("ContourPoints"))
                        return;

                    CancellationToken ctoken = new CancellationToken();

                    //create line feature layer if it does not exist
                    BasicFeatureLayer damCandidatesLayer = await CreateDamFeatureClass("DamCandidates");

                    var contourPointsLayer = MapView.Active.Map.FindLayers("ContourPoints").FirstOrDefault();
                    layer = contourPointsLayer as BasicFeatureLayer;

                    // store the spatial reference of the current layer
                    SpatialReference = layer.GetSpatialReference();

                    //Cursor for selected points
                    RowCursor cursor = layer.GetSelection().Search();

                    //If no selection was set, use full points layer
                    if (layer.GetSelection().GetCount() == 0)
                        cursor = layer.Search();
                    
                    Dictionary<int, SortedDictionary<int, SortedDictionary<long, MapPoint>>> contourHeights = new Dictionary<int, SortedDictionary<int, SortedDictionary<long, MapPoint>>>();

                    cps.Progressor.Status = "Loading ContourPoints into memory";
                    SharedFunctions.Log("Loading all ContourPoints into memory");
                    while (cursor.MoveNext())
                    {
                        if (ctoken.IsCancellationRequested)
                        {
                            SharedFunctions.Log("Canceled");
                            return;
                        }
                        using (Row row = cursor.Current)
                        {
                            var point = row[1] as MapPoint;
                            var pointID = (int)row[0];
                            var contourHeight = (int)(double)row[4];
                            var contourID = (int)row[2];

                            if (!ContourLengths.ContainsKey(contourID))
                                ContourLengths.Add(contourID, (double)row["Shape_Length"]);
                            if (!contourHeights.ContainsKey((int)contourHeight))
                                contourHeights.Add((int)contourHeight, new SortedDictionary<int, SortedDictionary<long, MapPoint>>());
                            if (!contourHeights[contourHeight].ContainsKey((int)contourID))
                                contourHeights[contourHeight].Add((int)contourID, new SortedDictionary<long, MapPoint>());
                            contourHeights[contourHeight][(int)contourID].Add(pointID, point);
                            TotalPointsCount++;
                        }
                    }
                    cps.Progressor.Status = "Analyze Contours";
                    SharedFunctions.Log("Analyze Contours");
                    bool multiThreading = (Parameter.MultiThreadingBox == null || !Parameter.MultiThreadingBox.IsChecked.HasValue || Parameter.MultiThreadingBox.IsChecked.Value);
                    if (multiThreading)
                    {
                        HeightsToProcess = contourHeights.Keys.ToList();
                        int ThreadCount = Math.Min(HeightsToProcess.Count, Environment.ProcessorCount);
                        SharedFunctions.Log("Divided work into " + ThreadCount + " threads...");
                        await Task.WhenAll(Enumerable.Range(1, ThreadCount).Select(c => Task.Run(
                            () =>
                            {
                                while (HeightsToProcess.Count > 0)// && !ctoken.IsCancellationRequested)
                                {
                                    int height = -1;
                                    lock (HeightsToProcess)
                                    {
                                        height = HeightsToProcess.FirstOrDefault();
                                        if (height != 0)
                                            HeightsToProcess.Remove(height);
                                    }
                                    if (height != -1)
                                    {
                                        var calc = new Dictionary<int, SortedDictionary<int, SortedDictionary<long, MapPoint>>>();
                                        calc.Add(height, contourHeights[height]);
                                        AnalyseContourHeights(calc, ctoken);
                                    }
                                }
                            }
                            , ctoken)) 
                            );
                    }
                    else
                    {
                        //Single Thread:
                        AnalyseContourHeights(contourHeights, ctoken);
                    }
                    cps.Progressor.Status = "Saving all " + chosenCandidates.Count + " candidates";
                    SharedFunctions.Log("Saving all " + chosenCandidates.Count + " candidates");
                    foreach (var candidate in chosenCandidates)
                    {
                        if (ctoken.IsCancellationRequested)
                        {
                            SharedFunctions.Log("Canceled");
                            return;
                        }
                        //Create coordinates for Polyline Feature with height ContourHeight + 5 Meters!
                        List<Coordinate3D> coordinates = new List<Coordinate3D>() {
                            new Coordinate3D(candidate.StartPoint.X, candidate.StartPoint.Y, candidate.ContourHeight + 5),
                            new Coordinate3D(candidate.EndPoint.X, candidate.EndPoint.Y, candidate.ContourHeight + 5)};

                        //save all selected candidates into the db
                        var attributes = new Dictionary<string, object>
                                {
                                    { "Shape", PolylineBuilder.CreatePolyline(coordinates) },
                                    { "ContourID", (long)candidate.ContourID },
                                    { "StartPointID", (long)candidate.StartPointID },
                                    { "EndPointID", (long)candidate.EndPointID },
                                    { "ContourHeight", (short)candidate.ContourHeight },
                                    { "LengthRating", (float)candidate.Rating },
                                    { "DistanceOnLine", (long)candidate.DistanceOnLine },
                                    { "Length", (short)candidate.Length },
                                    { "StartPointDistance", (long)candidate.StartPointDistance },
                                    { "EndPointDistance", (long)candidate.EndPointDistance },
                                    { "DamSpansContourStart", (short)(candidate.DamSpansContourStart ? 1 : 0) }
                                };
                        var editOp = new EditOperation() { Name = "Create dam candidate", SelectNewFeatures = false };
                        editOp.Create(damCandidatesLayer, attributes);
                        ////Execute the operations
                        editOp.Execute();
                    }
                }, cps.Progressor);
                                
                //save all edits
                await Project.Current.SaveEditsAsync();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                DateTime endTime = DateTime.Now;
                SharedFunctions.Log("Analysed " + PotentialCandidates.ToString("N0") + " candidates ( " + chosenCandidates.Count.ToString("N0") + " selected) in " + (endTime - startTime).TotalSeconds.ToString("N") + " seconds");
            }
        }

        private static void AnalyseContourHeights(Dictionary<int, SortedDictionary<int, SortedDictionary<long, MapPoint>>> contourHeights, CancellationToken ctoken)
        {
            int selectedCandidates = 0;
            foreach (var contourHeight in contourHeights)
            {
                //Analyse lines of one ContourLine
                foreach (var contourID in contourHeight.Value)
                {
                    //skip contours with less than 2.000 m length
                    if (contourID.Value.Count < (int)2000 / pointsIntervalOnContour)
                        continue;
                    List<CandidateDam> candidates = AnalyseCountourPoints(contourID.Value, contourHeight.Key, contourID.Key, ctoken);
                    lock (lockingObject)
                    {
                        chosenCandidates.AddRange(candidates);
                        selectedCandidates += candidates.Count;
                    }
                }
                SharedFunctions.Log(selectedCandidates.ToString("N0") + " candidates selected for " + contourHeight.Key + "m contours (Total: " + chosenCandidates.Count.ToString("N0") + " of " + PotentialCandidates.ToString("N0") + " potentials)");
            }            
        }
        
        public async static Task<BasicFeatureLayer> CreateDamFeatureClass(string name)
        {
            var existingLayer = MapView.Active.Map.FindLayers(name).FirstOrDefault();
            if (existingLayer != null)
                return existingLayer as BasicFeatureLayer;

            SharedFunctions.Log("Creating DamCandidates layer");
            List<object> arguments = new List<object> { CoreModule.CurrentProject.DefaultGeodatabasePath, name, "POLYLINE", "", "DISABLED", "ENABLED" };
            arguments.Add(SpatialReference);
            IGPResult result = await Geoprocessing.ExecuteToolAsync("CreateFeatureclass_management", Geoprocessing.MakeValueArray(arguments.ToArray()));
            var layer = MapView.Active.Map.FindLayers(name).FirstOrDefault() as BasicFeatureLayer;
            await SharedFunctions.ExecuteAddFieldTool(layer, "ContourID", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "StartPointID", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "EndPointID", "LONG");            
            await SharedFunctions.ExecuteAddFieldTool(layer, "ContourHeight", "SHORT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "LengthRating", "FLOAT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "DistanceOnLine", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "Length", "SHORT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "StartPointDistance", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "EndPointDistance", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "DamHeight", "SHORT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "DamVolume", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "ReservoirVolume", "LONG");
            await SharedFunctions.ExecuteAddFieldTool(layer, "VolumeRating", "FLOAT");
            await SharedFunctions.ExecuteAddFieldTool(layer, "DamSpansContourStart", "SHORT");

            return layer;
        }

        private static List<CandidateDam> AnalyseCountourPoints(SortedDictionary<long, MapPoint> points, int contourHeight, int contourID, CancellationToken ctoken)
        {
            int minPointID = (int)points.Min(c => c.Key);
            var first = points.First();
            var last = points.Last();
            //find out if the contour is a closed loop
            bool closedLoop = (first.Value.X == last.Value.X && first.Value.Y == last.Value.Y);
            //remove the last point
            points.Remove((long)last.Key);
            List <CandidateDam> candidates = new List<CandidateDam>();
            SortedDictionary<long, MapPoint> endpoints = new SortedDictionary<long, MapPoint>(points);
            //remove the first x points from the endpoints, as there is no valid candidate for a dam within the first 1000m
            for (int i = 0; i < 1000 / pointsIntervalOnContour; i++)
            {
                lock(lockingObject)
                    PotentialCandidates++;
                if (endpoints.Keys.Count == 0)
                    break;
                endpoints.Remove(endpoints.Keys.First());
            }
            foreach (var start in points)
            {
                lock(lockingObject)
                {
                    PointsAnalyzed++;                    
                    if(PointsAnalyzed % 1000 == 0)
                    { 
                        cps.Progressor.Value = Convert.ToUInt32(PointsAnalyzed * 100 / TotalPointsCount);
                        cps.Progressor.Status = string.Format("{0} Points of {1} analyzed", PointsAnalyzed.ToString("n0"), TotalPointsCount.ToString("n0"));
                    }
                }
                List<CandidateDam> pointCandidates = new List<CandidateDam>();
                List<CandidateDam> chosenPointCandidates = new List<CandidateDam>();
                foreach (var end in endpoints)
                {
                    var DamSpansContourStart = false;
                    lock (lockingObject)
                        PotentialCandidates++;
                    //Manual Length calculation with pythagoras
                    var Length = (decimal)Math.Sqrt(Math.Pow(start.Value.X - end.Value.X, 2) + Math.Pow(start.Value.Y - end.Value.Y, 2));
                    if (Length > 1000)
                        continue;
                    var distanceOnLine = Math.Abs((start.Key - end.Key) * pointsIntervalOnContour);
                    //if the current contour is a closed loop, always take the shorter distance on either side 
                    if(closedLoop && distanceOnLine > (decimal)ContourLengths[contourID] / 2.0m)
                    {
                        distanceOnLine = Convert.ToInt64(ContourLengths[contourID]) - distanceOnLine;
                        DamSpansContourStart = true;
                    }
                    //if a candidate is detected that has less than 1.000 m reservoir circumference, skip it (only happens when DamSpansContourStart)
                    if (distanceOnLine < 1000)
                        continue;
                    //if the distanceOnLine grows bigger than 30.000 m we stop testing this startpoint, as the resulting reservoir would exceed our target magnitude
                    if (distanceOnLine > 30000 && !closedLoop)
                        break;
                    //only save candidates that have a length rating > 10
                    if (distanceOnLine / Length < 10)
                        continue;

                    CandidateDam cand = CreateCandidateDam(contourHeight, minPointID, start, end, Length, distanceOnLine, DamSpansContourStart, contourID);
                    pointCandidates.Add(cand);
                }
                //if closed loop and result was over 30.000 m then delete now
                if(closedLoop)
                {
                    foreach (var item in pointCandidates.Where(c => c.DistanceOnLine > 30000).ToList())
                    {
                        candidates.Remove(item);
                    }
                }
                if (pointCandidates.Count > 0)
                {
                    int pointCandidateLimit = 3;
                    decimal lastRating = 0;
                    //choose max 3 candidates per starting point and try to preserve real alternative dam variants (crossing of multiple branches)
                    foreach (var pointCandidate in pointCandidates.OrderByDescending(c => c.Rating))
                    {
                        if (pointCandidateLimit == 0)
                            break;
                        //if the new rating is more than 20% worse than the last rating, break the loop
                        if (pointCandidate.Rating < lastRating * 0.8m)
                            break;
                        //keep only the best candidate within a distance of 2.000 m on each side
                        if (chosenPointCandidates.Any(c => Math.Abs(c.DistanceOnLine - pointCandidate.DistanceOnLine) < 1000))
                            continue;
                        //if there is already an obviously better candidate (lower length AND more distanceOnLine, then skip as well
                        //if (chosenPointCandidates.Any(c => c.Length < pointCandidate.Length && c.DistanceOnLine > pointCandidate.DistanceOnLine))
                        //    continue;

                        chosenPointCandidates.Add(pointCandidate);
                        pointCandidateLimit--;
                        lastRating = pointCandidate.Rating;
                    }
                    candidates.AddRange(chosenPointCandidates);
                }
                //remove one more point from the endpoints, so the first one to check against will be at least 1000m ahead again
                if (endpoints.Keys.Count > 0)
                    endpoints.Remove(endpoints.Keys.First());
            }
            //now make sure the same applies for the endpoints:
            foreach (var endPointID in candidates.Select(c => c.EndPointID).Distinct().ToList())
            {
                var endpointCandidates = candidates.Where(c => c.EndPointID == endPointID || c.StartPointID == endPointID).ToList();
                foreach (var endPointCandidate in endpointCandidates)
                {
                    //keep only the best candidate of this endpoint within a range of 2.000 m on each side
                    if (endpointCandidates.Any(c => c.Rating > endPointCandidate.Rating && Math.Abs(c.DistanceOnLine - endPointCandidate.DistanceOnLine) < 2000))
                        candidates.Remove(endPointCandidate);
                    //remove, if there are already obviously better candidates (lower length AND more distanceOnLine)
                    //This also removes potentially good candidates further away... this could be restricted by a distance limit...
                    else if (candidates.Any(c => c.Length < endPointCandidate.Length && c.DistanceOnLine > endPointCandidate.DistanceOnLine && c.StartPointID <= endPointCandidate.StartPointID && c.EndPointID >= endPointCandidate.EndPointID))
                        candidates.Remove(endPointCandidate);
                }
            }
            int vicinity = 1000 / pointsIntervalOnContour;
            //select all candidates that are in the vicinity of another candidate on the same contour but are lower ranked
            //this can only be selected for now that we are sure to have true dam candidates in the list
            var candidatesToDelete = candidates.Where(c => candidates.Any(d => d.ContourID == c.ContourID && d != c && c.Rating < d.Rating &&
                                                                            ( (Math.Abs(c.StartPointID - d.StartPointID) < vicinity && Math.Abs(c.EndPointID - d.EndPointID) < vicinity)
                                                                             || (Math.Abs(c.StartPointID - d.EndPointID) < vicinity && Math.Abs(c.EndPointID - d.StartPointID) < vicinity)
                                                                             )
                                                                          )
                                                        ).ToList();
            foreach (var candidateToDelete in candidatesToDelete)
            {
                candidates.Remove(candidateToDelete);
            }
            //if the contour is a closed loop, we have to have another look at neighbors across the start/end of the contour
            if(closedLoop)
            {                
                var candidatesToDelete2 = candidates.Where(c => candidates.Any(d => d.ContourID == c.ContourID && d != c && c.Rating < d.Rating &&
                                                                (((points.Count - Math.Abs(c.StartPointID - d.StartPointID)) < vicinity && Math.Abs(c.EndPointID - d.EndPointID) < vicinity)
                                                                 ||
                                                                 (Math.Abs(c.StartPointID - d.StartPointID) < vicinity && (points.Count - Math.Abs(c.EndPointID - d.EndPointID)) < vicinity)
                                                                 ||
                                                                 ((points.Count - Math.Abs(c.StartPointID - d.EndPointID)) < vicinity && Math.Abs(c.EndPointID - d.StartPointID) < vicinity)
                                                                 ||
                                                                 (Math.Abs(c.StartPointID - d.EndPointID) < vicinity && (points.Count - Math.Abs(c.EndPointID - d.StartPointID)) < vicinity)
                                                                 )
                                                              )
                                            ).ToList();
                foreach (var candidateToDelete in candidatesToDelete2)
                {
                    candidates.Remove(candidateToDelete);
                }
            }
            return candidates;
        }

        private static CandidateDam CreateCandidateDam(int contourHeight, int minPointID,  KeyValuePair<long, MapPoint> start, KeyValuePair<long, MapPoint> end, decimal Length, long distanceOnLine, bool damSpansContourStart, int contourID)
        {
            CandidateDam cand = new CandidateDam();
            cand.ContourID = contourID;
            cand.ContourHeight = contourHeight;
            cand.StartPoint = start.Value;
            cand.StartPointID = start.Key;
            cand.StartPointDistance = (start.Key - minPointID) * pointsIntervalOnContour;
            cand.EndPointID = end.Key;
            cand.EndPoint = end.Value;
            cand.EndPointDistance = (end.Key - minPointID) * pointsIntervalOnContour;
            cand.Length = Length;
            cand.DistanceOnLine = distanceOnLine;
            cand.DamSpansContourStart = damSpansContourStart;
            return cand;
        }
    }
}