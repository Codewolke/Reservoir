using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Core.Geoprocessing;

namespace Reservoir
{
    internal class CreateTINButton : Button
    {
        protected override async void OnClick()
        {
            string inputDEM = Parameter.DEMCombo.SelectedItem.ToString();
            string tinLayer = "TIN";
            var args = Geoprocessing.MakeValueArray(inputDEM, tinLayer);
            await SharedFunctions.RunModel(args, "CreateTIN");
        }
    }
}
