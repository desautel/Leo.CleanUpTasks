﻿namespace Leo.CleanUpTasks
{
    using Sdl.Desktop.IntegrationApi;
    using System;
    using System.ComponentModel;
    using System.Diagnostics.Contracts;
    using System.Windows.Forms;

    public partial class CleanUpSourceSettingsControl : UserControl, ISettingsAware<CleanUpSourceSettings>
    {
        private CleanUpSourceSettings settings = null;

        public CleanUpSourceSettingsControl()
        {
            InitializeComponent();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public CleanUpSourceSettings Settings
        {
            get
            {
                Contract.Ensures(Contract.Result<CleanUpSourceSettings>() != null);
                return settings;
            }
            set
            {
                settings = value;
            }
        }

        protected override void OnLeave(EventArgs e)
        {
            segmentLockerControl.SaveSettings();
            tagsSettingsControl.SaveSettings();
            conversionsSettingsControl.SaveSettings();
        }

        protected override void OnLoad(EventArgs e)
        {
            // Set Settings Here!!
            Settings.Settings = Settings;
            
            // Make sure to set the settings first!
            // SegmentLockerControl
            segmentLockerControl.SetSettings(Settings);
            segmentLockerControl.SetPresenter(new SegmentLockerPresenter(segmentLockerControl));
            segmentLockerControl.InitializeUI();

            // ConversionSettingsControl
            conversionsSettingsControl.SetSettings(Settings, BatchTaskMode.Source);
            conversionsSettingsControl.SetPresenter(new ConversionSettingsPresenter(conversionsSettingsControl, new Dialogs.FileDialog()));
            conversionsSettingsControl.InitializeUI();

            // TagSettingsControl
            tagsSettingsControl.SetSettings(Settings);
            tagsSettingsControl.SetPresenter(new TagSettingsPresenter(tagsSettingsControl));
            tagsSettingsControl.InitializeUI();
        }
    }
}