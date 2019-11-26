using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Core;

namespace Reservoir
{
    internal class ReservoirVolumeAndRankingButton : Button
    {
        protected override async void OnClick()
        {
            if(!SharedFunctions.LayerExists("TIN") || !SharedFunctions.LayerExists("DamCandidates") || !SharedFunctions.LayerExists("ReservoirSurfaces"))
                return;

            await Project.Current.SaveEditsAsync();

            string TINLayer = "TIN";
            string damCandidatesLayer = "DamCandidates";
            string reservoirSurfacesLayer = "ReservoirSurfaces";
            var args = Geoprocessing.MakeValueArray(reservoirSurfacesLayer, damCandidatesLayer, TINLayer, damCandidatesLayer);
            await SharedFunctions.RunModel(args, "Reservoir Volume");
        }
    }
}
