using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Core;

namespace Reservoir
{
    internal class PrepareContoursButton : Button
    {
        protected override async void OnClick()
        {
            string inputDEM = Parameter.DEMCombo.SelectedItem.ToString();
            string contourInterval = Parameter.ContourIntervalBox.Text;
            string PointInterval = Parameter.PointIntervalBox.Text + " meters";
            string Workspace = Project.Current.DefaultGeodatabasePath;
            var args = Geoprocessing.MakeValueArray(contourInterval, PointInterval, Workspace, inputDEM);
            await SharedFunctions.RunModel(args, "Prepare Contours");
        }        
    }        
}