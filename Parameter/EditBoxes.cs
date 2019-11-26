using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Reservoir
{
    public class PointIntervalBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public PointIntervalBox()
        {
            Parameter.PointIntervalBox = this;
            Text = "30";
        }

        protected override void OnEnter()
        {
            // TODO - add specific validation code here 
        }

        protected override void OnTextChange(string text)
        {
            // TODO - add specific validation code here
        }
        protected override void OnUpdate()
        {
            base.OnUpdate();    
        }
    }
    public class ContourIntervalBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public ContourIntervalBox()
        {
            Parameter.ContourIntervalBox = this;
            Text = "10";
        }

        protected override void OnEnter()
        {
            // TODO - add specific validation code here 
        }

        protected override void OnTextChange(string text)
        {
            // TODO - add specific validation code here
        }
    }
    public class MultiThreadingBox : ArcGIS.Desktop.Framework.Contracts.CheckBox
    {
        public MultiThreadingBox()
        {
            Parameter.MultiThreadingBox = this;
            //IsChecked = true;
        }
    }
}
