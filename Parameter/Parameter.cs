using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Reservoir
{
    public static class Parameter
    {
        public static DEMCombo DEMCombo { get; set; }
        public static PointIntervalBox PointIntervalBox { get; set; }
        public static ContourIntervalBox ContourIntervalBox { get; set; }
        public static MultiThreadingBox MultiThreadingBox { get; set; }

        public static string ContourLayerName = "Contours";
    }
}
