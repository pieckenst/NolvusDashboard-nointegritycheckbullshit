﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Syncfusion.WinForms.Controls;
using Syncfusion.Windows.Forms;
using Vcc.Nolvus.Api.Installer.Services;
using Vcc.Nolvus.StockGame.Core;
using Vcc.Nolvus.StockGame.Patcher;
using Vcc.Nolvus.Core.Services;
using Vcc.Nolvus.Core.Enums;
using Vcc.Nolvus.Services.Files;
using Vcc.Nolvus.Services.Game;

namespace Vcc.Nolvus.Downgrader
{
    public partial class Main : SfForm
    {
        private int DefaultDpi = 96;        

        public double ScalingFactor
        {
            get
            {
                return CreateGraphics().DpiX / DefaultDpi;
            }
        }

        public Main()
        {
            InitializeComponent();

            SkinManager.SetVisualStyle(this, "Office2016Black");
            Style.TitleBar.MaximizeButtonHoverBackColor = Color.DarkOrange;
            Style.TitleBar.MinimizeButtonHoverBackColor = Color.DarkOrange;
            Style.TitleBar.HelpButtonHoverBackColor = Color.DarkOrange;
            Style.TitleBar.CloseButtonHoverBackColor = Color.DarkOrange;
            Style.TitleBar.MaximizeButtonPressedBackColor = Color.DarkOrange;
            Style.TitleBar.MinimizeButtonPressedBackColor = Color.DarkOrange;
            Style.TitleBar.HelpButtonPressedBackColor = Color.DarkOrange;
            Style.TitleBar.CloseButtonPressedBackColor = Color.DarkOrange;

            Style.TitleBar.BackColor = Color.FromArgb(54, 54, 54);
            Style.TitleBar.IconBackColor = Color.FromArgb(54, 54, 54);                        
            Style.BackColor = Color.FromArgb(54, 54, 54);

            Padding = new Padding(15, 15, 15, 15);
            
            TxtBxSkyrimDir.Text = ServiceSingleton.Game.GetSkyrimSEDirectory();
        }

        private void LstBxOutput_DrawItem(object sender, DrawItemEventArgs e)
        {
            Font Font = e.Font;

            if (ScalingFactor > 1)
            {
                Font = new Font(e.Font.FontFamily, (float)(e.Font.Size * ScalingFactor), GraphicsUnit.Pixel);
            }

            if (e.Index < 0) return;
            if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
                e = new DrawItemEventArgs(e.Graphics,
                                          e.Font,
                                          e.Bounds,
                                          e.Index,
                                          e.State ^ DrawItemState.Selected,
                                          Color.FromArgb(54, 54, 54),
                                          Color.Orange);

            e.DrawBackground();
            e.Graphics.DrawString(LstBxOutput.Items[e.Index].ToString(), Font, Brushes.White, e.Bounds, StringFormat.GenericDefault);
            e.DrawFocusRectangle();
        }

        private Task Downgrade()
        {
            BtnBrowseSkyrimPath.Enabled = false;
            BtnBrowseOutputPath.Enabled = false;
            BtnDowngrade.Enabled = false;

            return Task.Run(async () => 
            {
                ApiManager.Init("https://www.nolvus.net/rest/", "v1");

                LstBxOutput.ItemHeight = (int)Math.Round(LstBxOutput.ItemHeight * ScalingFactor);              

                var StockGameManager = new StockGameManager(
                    Path.GetTempPath(), 
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "Patches"),
                    TxtBxSkyrimDir.Text, 
                    TxtBxOutputDir.Text, 
                    "English", 
                    "EN", 
                    await ApiManager.Service.Installer.GetLatestGamePackage(),
                    true);

                StockGameManager.OnDownload += StockGameManager_OnDownload;
                StockGameManager.OnExtract += StockGameManager_OnExtract;
                StockGameManager.OnItemProcessed += StockGameManager_OnItemProcessed;
                StockGameManager.OnStepProcessed += StockGameManager_OnStepProcessed;                

                try
                {
                    await StockGameManager.Load();
                    await StockGameManager.CheckIntegrity();
                    await StockGameManager.CopyGameFiles();
                    await StockGameManager.PatchGameFiles();

                    AddItemToList("Downgrade completed, you can close this application.");

                    HideProgress();
                }
                catch (Exception ex)
                {                    
                                        

                    var Error = string.Empty;

                    if (ex is GameFileMissingException)
                    {
                        RollBack();
                        Error = "Error during game file checking, Skyrim Anniversary Edition is not installed (" + ex.Message + ")";
                        AddItemToList(Error);

                        Status(Error, true);
                    }
                    else if (ex is GameFileIntegrityException)
                    {
                        Error = "Error during game integrity checking. " + ex.Message + ". Possible fix is to do an integrity check for Skyrim in Steam";
                    }
                    else if (ex is GameFilePatchingException)
                    {
                        RollBack();
                        Error = "Error during game files patching (" + ex.Message + ")";
                        AddItemToList(Error);

                        Status(Error, true);
                    }
                    else
                    {
                        RollBack();
                        Error = "Error during stock game creation with message : " + ex.Message;
                        AddItemToList(Error);

                        Status(Error, true);
                    }

                    
                }
            });            
        }

        public void HideProgress()
        {
            if (InvokeRequired)
            {
                Invoke((System.Action)HideProgress);
                return;
            }

            LblStatus.Visible = false;
            ProgressBar.Visible = false;
        }

        public void Status(string Text, bool Error)
        {
            if (InvokeRequired)
            {
                Invoke((System.Action<string, bool>)Status, Text, Error);
                return;
            }

            LblStatus.Visible = true;
            LblStatus.Text = Text;

            if (Error)
            {
                LblStatus.ForeColor = Color.Red;
            }
        }

        public void Progress(int Value)
        {
            if (InvokeRequired)
            {
                Invoke((System.Action<int>)Progress, Value);
                return;
            }

            ProgressBar.Visible = true;
            ProgressBar.Value = Value;
        }

        public void AddItemToList(string Item)
        {
            if (InvokeRequired)
            {
                Invoke((System.Action<string>)AddItemToList, Item);
                return;
            }

            LstBxOutput.Items.Add(Item);

            int VisibleItems = LstBxOutput.ClientSize.Height / LstBxOutput.ItemHeight;
            LstBxOutput.TopIndex = Math.Max(LstBxOutput.Items.Count - VisibleItems + 1, 0);
            ServiceSingleton.Logger.Log(Item);
        }

        private void StockGameManager_OnStepProcessed(object sender, StepProcessedEventArgs e)
        {
            AddItemToList(e.Step);
        }

        private void StockGameManager_OnItemProcessed(object sender, ItemProcessedEventArgs e)
        {
            //double Percent = ((double)e.Value / (double)e.Total) * 100;

            //Percent = Math.Round(Percent, 0);

            //Status(e.Step + " (" + Percent.ToString() + "%)...", false);
            //Progress(System.Convert.ToInt16(Percent));

            double Percent = ((double)e.Value / (double)e.Total) * 100;

            Percent = Math.Round(Percent, 0);

            switch (e.Step)
            {
                case StockGameProcessStep.GameFileInfoLoading:
                    Status(string.Format("Loading game files info for {0}...", e.ItemName), false);                    
                    Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.PatchingInfoLoading:
                    Status(string.Format("Loading patching info for {0}...", e.ItemName), false);
                    Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.GameFilesChecking:
                    Status(string.Format("Checking game file {0}...", e.ItemName), false);
                    Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.GameFilesCopy:
                    Status(string.Format("Copying game file {0}...", e.ItemName), false);
                    Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.GameFilesPatching:
                    Status("Awaiting game file to patch...", false);                    
                    break;
                case StockGameProcessStep.PatchGameFile:
                    Status(string.Format("Patching game files {0}...", e.ItemName), false);
                    Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.CheckPatchedGameFile:
                    Status(string.Format("Checking patched game files {0}...", e.ItemName), false);
                    Progress(System.Convert.ToInt16(Percent));
                    break;

            }
        }

        private void StockGameManager_OnExtract(object sender, Core.Events.ExtractProgress e)
        {
            Status("Extracting game meta (" + e.ProgressPercentage + "%)...", false);
            Progress(e.ProgressPercentage);
        }

        private void StockGameManager_OnDownload(object sender, Core.Events.DownloadProgress e)
        {
            Status("Downloading file (" + e.ProgressPercentage + "%)...", false);
            Progress(e.ProgressPercentage);
        }

        private void BtnDowngrade_Click(object sender, EventArgs e)
        {
            if (TxtBxSkyrimDir.Text != string.Empty && TxtBxOutputDir.Text != string.Empty)
            {
                if (TxtBxSkyrimDir.Text == TxtBxOutputDir.Text)
                {
                    MessageBox.Show("Skyrim directory is equal to output directory!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    if (ServiceSingleton.Files.IsDirectoryEmpty(TxtBxOutputDir.Text))
                    {
                        Downgrade();
                    }
                    else
                    {
                        MessageBox.Show("The output directory is not empty! Please select an empty directory!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }                    
                }                
            }
            else
            {
                MessageBox.Show("Skyrim directory and/or output directory are missing!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }            
        }

        private void RollBack()
        {
            HideProgress();
            this.AddItemToList("Error detected, rollbacking changes...");            
            ServiceSingleton.Files.RemoveDirectory(TxtBxOutputDir.Text, false);
        }

        private void BtnBrowseSkyrimPath_Click(object sender, EventArgs e)
        {
            DialogResult result = folderBrowserDialog1.ShowDialog();

            if (result == DialogResult.OK)
            {
                TxtBxSkyrimDir.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        private void BtnBrowseOutputPath_Click(object sender, EventArgs e)
        {
            DialogResult result = folderBrowserDialog1.ShowDialog();

            if (result == DialogResult.OK)
            {
                if (ServiceSingleton.Files.IsDirectoryEmpty(folderBrowserDialog1.SelectedPath))
                {
                    TxtBxOutputDir.Text = folderBrowserDialog1.SelectedPath;
                }
                else
                {
                    MessageBox.Show("The specified directory is not empty! Please select an empty directory!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
