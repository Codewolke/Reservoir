using System;
using System.Linq;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;

namespace Reservoir
{
    /// <summary>
    /// Represents the ComboBox
    /// </summary>
    public class DEMCombo : ComboBox
    {        
        /// <summary>
        /// Combo Box constructor
        /// </summary>
        public DEMCombo()
        {
            Parameter.DEMCombo = this;
            LayersAddedEvent.Subscribe(UpdateCombo);
            LayersMovedEvent.Subscribe(UpdateCombo);
            LayersRemovedEvent.Subscribe(UpdateCombo);
            ActiveMapViewChangedEvent.Subscribe(UpdateCombo);

            UpdateCombo(null);
        }

        /// <summary>
        /// Updates the combo box with all the items.
        /// </summary>

        private void UpdateCombo(object obj)
        {
            try
            {
                if (MapView.Active == null)
                    return;
                Clear();

                var existingLayers = MapView.Active.Map.Layers;
                foreach (var layer in existingLayers)
                {
                    if (layer is RasterLayer)
                        Add(new ComboBoxItem(layer.Name));
                }
                Enabled = true;
                SelectedItem = ItemCollection.FirstOrDefault();
            }
            catch (Exception)
            {
                
            }
        }

        /// <summary>
        /// The on comboBox selection change event. 
        /// </summary>
        /// <param name="item">The newly selected combo box item</param>
        protected override void OnSelectionChange(ComboBoxItem item)
        {

            if (item == null)
                return;

            if (string.IsNullOrEmpty(item.Text))
                return;

            // TODO  Code behavior when selection changes.    
        }

    }
}
