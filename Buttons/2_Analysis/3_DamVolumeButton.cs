using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Core;
using System.Threading.Tasks;
using System.Threading;
using ArcGIS.Desktop.Editing;

namespace Reservoir
{
    internal class DamVolumeButton : Button
    {
        private int IncomingCandidates = 0;
        private int OutgoingCandidates = 0;
        protected override async void OnClick()
        {
            SharedFunctions.Log("DamVolume Calculation started");
            DateTime startTime = DateTime.Now;
            await Project.Current.SaveEditsAsync();
            IncomingCandidates = 0;
            OutgoingCandidates = 0;
            List<CandidateDam> candidates = new List<CandidateDam>();
            List<CandidateDam> candidates2Delete = new List<CandidateDam>();
            try
            {
                await QueuedTask.Run(async () =>
                {
                    if (!SharedFunctions.LayerExists("DamCandidates") || !SharedFunctions.LayerExists("Contours"))
                        return;
                    var _damCandidatesLayer = MapView.Active.Map.FindLayers("DamCandidates").FirstOrDefault();

                    BasicFeatureLayer damCandidatesLayer = _damCandidatesLayer as BasicFeatureLayer;
                    SharedFunctions.LoadDamCandidatesFromLayer(candidates, damCandidatesLayer);
                    IncomingCandidates = candidates.Count();
                    OutgoingCandidates = IncomingCandidates;

                    //create stacked profile
                    string inputDEM = Parameter.DEMCombo.SelectedItem.ToString();
                    var valueArray = Geoprocessing.MakeValueArray(damCandidatesLayer.Name, inputDEM, "DamProfile", null);
                    var environments = Geoprocessing.MakeEnvironmentArray(overwriteoutput: true);
                    var cts = new CancellationTokenSource();
                    await Project.Current.SaveEditsAsync();
                    var res = await Geoprocessing.ExecuteToolAsync("StackProfile", valueArray, environments, cts.Token, null, GPExecuteToolFlags.Default);

                    //analyse profile and add data to line feature or delete line feature
                    //profile can be used to calculate frontal dam area and dam volume?!
                    var damProfileTable = MapView.Active.Map.FindStandaloneTables("DamProfile").FirstOrDefault();
                    if (damProfileTable == null)
                    {
                        SharedFunctions.Log("No DamProfile Table found!");
                    }

                    CandidateDam cand = null;
                    double prev_dist = 0;
                    int prev_lineID = -1;
                    using (var profileCursor = damProfileTable.Search())
                    {
                        while (profileCursor.MoveNext())
                        {
                            using (Row profileRow = profileCursor.Current)
                            {
                                var first_dist = (double)profileRow[1];
                                var first_z = (double)profileRow[2];
                                var line_ID = (int)profileRow[5];

                                if (prev_lineID != line_ID)
                                {
                                    prev_dist = 0;
                                    cand = candidates.SingleOrDefault(c => c.ObjectID == line_ID);
                                    // set Volume and ZMin to the default values
                                    cand.DamVolume = 0;
                                    cand.ZMin = cand.ContourHeight;
                                }
                                else if (candidates2Delete.Contains(cand))
                                    continue;
                                //set lowest point of dam to calculate max dam height later
                                if (cand.ZMin > (int)first_z)
                                    cand.ZMin = (int)first_z;

                                if ((int)first_z > (cand.ContourHeight + 5))
                                {
                                    candidates2Delete.Add(cand);
                                    continue;
                                }

                                //add volume of current block of dam... the assumption is a triangle dam shape (cross section) with alpha = 45°
                                //thus the area of the cross section is calculated by <height²> and the volume by <height² * length>
                                cand.DamVolume += (long)(Math.Pow((cand.ContourHeight - first_z), 2) * (first_dist - prev_dist));

                                prev_lineID = line_ID;
                                prev_dist = first_dist;
                            }
                        }
                    }

                    await DeleteCandidates(candidates, damCandidatesLayer, candidates2Delete);
                    //remove candidates with dam heights outside of the limits
                    await DeleteCandidates(candidates, damCandidatesLayer, candidates.Where(c => (c.ContourHeight - c.ZMin) < 10 || (c.ContourHeight - c.ZMin) > 300).ToList());

                    //update the new attributes to the feature
                    foreach (var candidate in candidates)
                    {
                        var editOp2 = new EditOperation();
                        Dictionary<string, object> attributes = new Dictionary<string, object>();
                        attributes.Add("DamHeight", (short)(candidate.ContourHeight - candidate.ZMin));
                        attributes.Add("DamVolume", (long)(candidate.DamVolume));
                        editOp2.Modify(damCandidatesLayer, candidate.ObjectID, attributes);
                        var resu = await editOp2.ExecuteAsync();
                    }

                    await Project.Current.SaveEditsAsync();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                DateTime endTime = DateTime.Now;
                SharedFunctions.Log("Analysed in " + (endTime - startTime).TotalSeconds.ToString("N") + " seconds completed (" + OutgoingCandidates.ToString("N0") + " of " + IncomingCandidates.ToString("N0") + " candidates left)");
            }
        }

        private async Task DeleteCandidates(List<CandidateDam> candidates, BasicFeatureLayer damCandidatesLayer, List<CandidateDam> candidatesToDelete)
        {
            foreach (var candidateToDelete in candidatesToDelete)
            {
                candidates.Remove(candidateToDelete);
                OutgoingCandidates--;
            }
            if (candidatesToDelete.Count > 0)
            {
                var deleteOp = new EditOperation();
                deleteOp.Delete(damCandidatesLayer, candidatesToDelete.Select(c => c.ObjectID).ToList());
                await deleteOp.ExecuteAsync();
            }
        }
    }
}