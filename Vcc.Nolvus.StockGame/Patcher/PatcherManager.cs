﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Vcc.Nolvus.Core.Events;
using Vcc.Nolvus.Core.Services;
using Vcc.Nolvus.Core.Enums;
using Vcc.Nolvus.StockGame.Core;
using Vcc.Nolvus.StockGame.Meta;

namespace Vcc.Nolvus.StockGame.Patcher
{
    public class PatcherManager
    {
        private const string xdeltaUrl = "https://github.com/jmacd/xdelta-gpl/releases/download/v3.1.0/xdelta3-3.1.0-x86_64.exe.zip";
        private const string xdeltaBin = "xdelta3-3.1.0-x86_64.exe";

        #region Fields

        string _WorkingDir = string.Empty;
        string _LibDir = string.Empty;
        string _PatchDir = string.Empty;

        #endregion

        #region Events

        public event DownloadProgressChangedHandler OnDownload;
        public event ExtractProgressChangedHandler OnExtract;

        event OnItemProcessedHandler OnItemProcessedEvent;

        public event OnItemProcessedHandler OnItemProcessed
        {
            add
            {
                if (OnItemProcessedEvent != null)
                {
                    lock (OnItemProcessedEvent)
                    {
                        OnItemProcessedEvent += value;
                    }
                }
                else
                {
                    OnItemProcessedEvent = value;
                }
            }
            remove
            {
                if (OnItemProcessedEvent != null)
                {
                    lock (OnItemProcessedEvent)
                    {
                        OnItemProcessedEvent -= value;
                    }
                }
            }
        }

        event OnStepProcessedHandler OnStepProcessedEvent;

        public event OnStepProcessedHandler OnStepProcessed
        {
            add
            {
                if (OnStepProcessedEvent != null)
                {
                    lock (OnStepProcessedEvent)
                    {
                        OnStepProcessedEvent += value;
                    }
                }
                else
                {
                    OnStepProcessedEvent = value;
                }
            }
            remove
            {
                if (OnStepProcessedEvent != null)
                {
                    lock (OnStepProcessedEvent)
                    {
                        OnStepProcessedEvent -= value;
                    }
                }
            }
        }

        #endregion

        public PatcherManager(string WorkingDir, string LibDir, string PatchDir)
        {
            _WorkingDir = WorkingDir;
            _LibDir = LibDir;
            _PatchDir = PatchDir;
        }

        #region Methods

        private void Downloading(object sender, DownloadProgress e)
        {
            if (OnDownload != null)
            {
                OnDownload(this, e);
            }
        }

        private void Extracting(object sender, ExtractProgress e)
        {
            if (OnExtract != null)
            {
                OnExtract(this, e);
            }
        }

        private void ElementProcessed(int Value, int Total, StockGameProcessStep Step, string ItemName)
        {
            OnItemProcessedHandler Handler = this.OnItemProcessedEvent;
            ItemProcessedEventArgs Event = new ItemProcessedEventArgs(Value, Total, Step, ItemName);
            if (Handler != null) Handler(this, Event);
        }

        private void StepProcessed(string Step)
        {
            OnStepProcessedHandler Handler = this.OnStepProcessedEvent;
            StepProcessedEventArgs Event = new StepProcessedEventArgs(0, 0, Step);
            if (Handler != null) Handler(this, Event);
        }

        private async Task DoDownloadBinaries()
        {
            var Tsk = Task.Run(async () => 
            {
                if (!File.Exists(Path.Combine(_LibDir, "xdelta3.exe")))
                {
                    var DownloadedFile = Path.Combine(_WorkingDir, "xdelta3.zip");

                    try
                    {
                        try
                        {

                            this.StepProcessed("Downloading patcher binary file");

                            await ServiceSingleton.Files.DownloadFile(xdeltaUrl, DownloadedFile, Downloading);

                            this.StepProcessed("Patcher bynary downloaded");

                            await ServiceSingleton.Files.ExtractFile(DownloadedFile, _WorkingDir, Extracting);

                            this.StepProcessed("Patching Binaries extracted");

                            File.Copy(Path.Combine(_WorkingDir, xdeltaBin), Path.Combine(_LibDir, "xdelta3.exe"), true);
                        }
                        catch(Exception ex)
                        {
                            ServiceSingleton.Logger.Log(string.Format("Error during patcher binary file download message {0}", ex.Message));
                            throw ex;
                        }
                    }
                    finally
                    {
                        File.Delete(DownloadedFile);
                        File.Delete(Path.Combine(_WorkingDir, xdeltaBin));
                    }
                }                
            });

            await Tsk;            
        }

        private async Task DoDownloadPatchFile(PatchingInstruction Instruction)
        {            
            var Tsk = Task.Run(async () => 
            {
                try
                {
                    if (!File.Exists(Path.Combine(_PatchDir, Instruction.PatchFile)))
                    {
                        this.StepProcessed("Downloading patch file " + Instruction.PatchFile);

                        string DownloadedFile = Path.Combine(_PatchDir, Instruction.PatchFile);

                        await ServiceSingleton.Files.DownloadFile(Instruction.DownLoadLink, DownloadedFile, Downloading);

                        this.StepProcessed("Patching file downloaded");
                    }
                }
                catch(Exception ex)
                {
                    ServiceSingleton.Logger.Log(string.Format("Error during patch file download with message {0}", ex.Message));
                    throw ex;
                }              
            });

            await Tsk;                              
        }      

        private async Task DoPatchFile(PatchingInstruction Instruction, string SourceDir, string DestDir, bool KeepPatches)
        {            
            var Tsk = Task.Run(async () => 
            {
                try
                {
                    await DoDownloadPatchFile(Instruction);

                    StepProcessed("Patching game file : " + Instruction.DestFile.Name);
                    ElementProcessed(0, 1, StockGameProcessStep.PatchGameFile, Instruction.DestFile.Name);

                    string SourceFileName = Instruction.SourceFile.GetFullName(SourceDir);
                    string DestinationFileName = Instruction.DestFile.GetFullName(DestDir);
                    string PatchFileName = Path.Combine(_PatchDir, Instruction.PatchFile);

                    Process PatchingProcess = new Process();

                    PatchingProcess.StartInfo.WorkingDirectory = DestDir;
                    PatchingProcess.StartInfo.FileName = "cmd.exe";
                    PatchingProcess.StartInfo.CreateNoWindow = true;
                    PatchingProcess.StartInfo.UseShellExecute = false;
                    PatchingProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    string CommandLine = string.Format("\"" + Path.Combine(_LibDir, "xdelta3.exe") + "\" -d -f -s \"{0}\" \"{1}\" \"{2}\"", SourceFileName, PatchFileName, DestinationFileName);

                    ServiceSingleton.Logger.Log(string.Format("Executing command {0}", CommandLine));                 

                    PatchingProcess.StartInfo.Arguments = "/c \"" + CommandLine + "\"";

                    PatchingProcess.StartInfo.RedirectStandardOutput = true;
                    PatchingProcess.StartInfo.RedirectStandardError = true;

                    List<String> Output = new List<string>();

                    PatchingProcess.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
                    {
                        if (e.Data != null)
                        {
                            Output.Add((string)e.Data);
                        }
                    });
                    PatchingProcess.ErrorDataReceived += new DataReceivedEventHandler((s, e) =>
                    {
                        if (e.Data != null)
                        {
                            Output.Add((String)e.Data);
                        }
                    });

                    PatchingProcess.Start();
                    PatchingProcess.BeginOutputReadLine();
                    PatchingProcess.BeginErrorReadLine();

                    PatchingProcess.WaitForExit();

                    if (PatchingProcess.ExitCode == 0)
                    {
                        var CommandOutput = string.Join(Environment.NewLine, Output.ToArray());

                        ServiceSingleton.Logger.Log(string.Format("Exit code {0}", PatchingProcess.ExitCode));
                        ServiceSingleton.Logger.Log(string.Format("Command output [{0}]", CommandOutput));

                        if (CommandOutput.Contains("The screen cannot be set to the number of lines and columns specified"))
                        {
                            throw new GameFilePatchingException("Failed to patch game file [CMD Error]: " + Instruction.DestFile.Name, string.Join(Environment.NewLine, Output.ToArray()));
                        }
                        
                        if (!KeepPatches)
                        {
                            File.Delete(PatchFileName);
                        }
                        
                        StepProcessed("Game file : " + Instruction.DestFile.Name + " patched");
                        ElementProcessed(1, 1, StockGameProcessStep.PatchGameFile, Instruction.DestFile.Name);
                    }
                    else
                    {
                        ServiceSingleton.Logger.Log(string.Format("Exit code {0}", PatchingProcess.ExitCode));
                        ServiceSingleton.Logger.Log(string.Format("Command output [{0}]", string.Join(Environment.NewLine, Output.ToArray())));
                        throw new GameFilePatchingException("Failed to patch game file : " + Instruction.DestFile.Name, String.Join(Environment.NewLine, Output.ToArray()));
                    }
                }
                catch(Exception ex)
                {
                    ServiceSingleton.Logger.Log(string.Format("Error during game file patching with message {0}", ex.Message));
                    throw ex;
                }
            });

            await Tsk;           
        }        

        private void CheckPatchedFile(PatchingInstruction Instruction, string DestDir)
        {
            string FileName = Instruction.DestFile.GetFullName(DestDir);

            StepProcessed("Checking integrity for patched game file " + Instruction.DestFile.Name);
            ElementProcessed(0, 1, StockGameProcessStep.CheckPatchedGameFile, Instruction.DestFile.Name);            

            string FileHash = ServiceSingleton.Files.GetHash(FileName);

            if (FileHash != Instruction.DestFile.Hash)
            {
               Console.WriteLine("Hash for game file : " + FileName + " does not match!");
            }

            this.StepProcessed("Patched game file " + Instruction.DestFile.Name + " integrity ok");
            ElementProcessed(1, 1, StockGameProcessStep.CheckPatchedGameFile, Instruction.DestFile.Name);
        }

        public async Task PatchFile(string SourceFile, string DestinationFile, string PatchFileName)
        {            
            var Tsk = Task.Run(async () =>
            {
                try
                {
                    await DoDownloadBinaries();
                                     
                    Process PatchingProcess = new Process();

                    PatchingProcess.StartInfo.WorkingDirectory = new FileInfo(DestinationFile).DirectoryName;
                    PatchingProcess.StartInfo.FileName = "cmd.exe";
                    PatchingProcess.StartInfo.CreateNoWindow = true;
                    PatchingProcess.StartInfo.UseShellExecute = false;
                    PatchingProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    string CommandLine = string.Format("\"" + Path.Combine(_LibDir, "xdelta3.exe") + "\" -d -f -s \"{0}\" \"{1}\" \"{2}\"", SourceFile, PatchFileName, DestinationFile);

                    PatchingProcess.StartInfo.Arguments = "/c \"" + CommandLine + "\"";

                    PatchingProcess.StartInfo.RedirectStandardOutput = true;
                    PatchingProcess.StartInfo.RedirectStandardError = true;

                    List<String> Output = new List<string>();

                    PatchingProcess.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
                    {
                        if (e.Data != null)
                        {
                            Output.Add((string)e.Data);
                        }
                    });
                    PatchingProcess.ErrorDataReceived += new DataReceivedEventHandler((s, e) =>
                    {
                        if (e.Data != null)
                        {
                            Output.Add((String)e.Data);
                        }
                    });

                    PatchingProcess.Start();
                    PatchingProcess.BeginOutputReadLine();
                    PatchingProcess.BeginErrorReadLine();

                    PatchingProcess.WaitForExit();

                    if (PatchingProcess.ExitCode == 0)
                    {
                        var CommandOutput = string.Join(Environment.NewLine, Output.ToArray());

                        ServiceSingleton.Logger.Log(string.Format("Exit code {0}", PatchingProcess.ExitCode));
                        ServiceSingleton.Logger.Log(string.Format("Command output [{0}]", CommandOutput));

                        if (CommandOutput.Contains("The screen cannot be set to the number of lines and columns specified"))
                        {
                            throw new GameFilePatchingException("Failed to patch game file [CMD Error]: " + new FileInfo(DestinationFile).Name + " (" + String.Join(Environment.NewLine, Output.ToArray()) + ")", string.Join(Environment.NewLine, Output.ToArray()));
                        }                                             
                    }
                    else
                    {
                        ServiceSingleton.Logger.Log(string.Format("Exit code {0}", PatchingProcess.ExitCode));
                        ServiceSingleton.Logger.Log(string.Format("Command output [{0}]", string.Join(Environment.NewLine, Output.ToArray())));

                        throw new GameFilePatchingException("Failed to patch game file : " + new FileInfo(DestinationFile).Name + " (" + String.Join(Environment.NewLine, Output.ToArray()) + ")", String.Join(Environment.NewLine, Output.ToArray()));
                    }

                }
                catch (Exception ex)
                {
                    ServiceSingleton.Logger.Log(string.Format("Error during game file patching with message {0}", ex.Message));
                    throw ex;
                }
            });

            await Tsk;
        }

        private void DeleteFile(PatchingInstruction Instruction, string DestDir)
        {
            switch (Instruction.SourceFile.Location)
            {
                case FileLocation.Data:
                    File.Delete(Path.Combine(DestDir, "Data", Instruction.SourceFile.Name));
                    break;

                default:
                    File.Delete(Path.Combine(DestDir, Instruction.SourceFile.Name));
                    break;
            }            
        }

        public async Task PatchFile(PatchingInstruction Instruction, string SourceDir, string DestDir, bool KeepPatches)
        {            
            var Tsk = Task.Run(async ()=>
            {
                try
                {
                    await DoDownloadBinaries();

                    StepProcessed("About to patch game file : " + Instruction.DestFile.Name);

                    switch (Instruction.Action)
                    {
                        case PatcherAction.Delete:
                            DeleteFile(Instruction, DestDir);
                            StepProcessed("Game file : " + Instruction.DestFile.Name + " deleted");
                            break;
                        case PatcherAction.Patch:                            
                            await DoPatchFile(Instruction, SourceDir, DestDir, KeepPatches);                                                        
                            CheckPatchedFile(Instruction, DestDir);                            
                            break;
                    }                    
                }
                catch(Exception ex)
                {
                    throw ex;
                }
            });

            await Tsk;            
        }

        #endregion

    }
}
