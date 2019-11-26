using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using System.Windows.Input;

namespace Reservoir
{
    public class ReservoirOutputDockPaneViewModel : DockPane
    {
        private const string _dockPaneID = "Reservoir_ReservoirOutputDockPane";
        
        private MyObservable<string> _logText = new MyObservable<string>();
        public MyObservable<string> LogText => _logText;
        private ICommand _clearLogCommand;
        public ICommand ClearLogCommand => _clearLogCommand;

        protected ReservoirOutputDockPaneViewModel() {
            Global.LogVM = this;
            _clearLogCommand = new RelayCommand(() => ClearLog(), () => true);
        }
        private void ClearLog()
        {
            SharedFunctions.ClearLog();
        }
            /// <summary>
            /// Show the DockPane.
            /// </summary>
        internal static void Show()
        {
            DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
            if (pane == null)
                return;

            pane.Activate();
        }

        /// <summary>
        /// Text shown near the top of the DockPane.
        /// </summary>
        private string _heading = "Reservoir Output";
        public string Heading
        {
            get { return _heading; }
            set
            {
                SetProperty(ref _heading, value, () => Heading);
            }
        }
    }

    /// <summary>
    /// Button implementation to show the DockPane.
    /// </summary>
    internal class ReservoirOutputDockPane_ShowButton : Button
    {
        protected override void OnClick()
        {
            ReservoirOutputDockPaneViewModel.Show();
        }
    }
}
