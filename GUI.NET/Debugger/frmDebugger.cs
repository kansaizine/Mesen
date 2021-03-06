﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mesen.GUI.Config;
using Mesen.GUI.Forms;

namespace Mesen.GUI.Debugger
{
	public partial class frmDebugger : BaseForm
	{
		private List<Form> _childForms = new List<Form>();
		private bool _debuggerInitialized = false;

		private InteropEmu.NotificationListener _notifListener;
		private ctrlDebuggerCode _lastCodeWindow;
		private frmTraceLogger _traceLogger;
		private DebugWorkspace _workspace;

		public frmDebugger()
		{
			InitializeComponent();
		}

		protected override void OnLoad(EventArgs e)
		{
 			base.OnLoad(e);

			ctrlConsoleStatus.OnStateChanged += ctrlConsoleStatus_OnStateChanged;
			LabelManager.OnLabelUpdated += LabelManager_OnLabelUpdated;
			BreakpointManager.BreakpointsChanged += BreakpointManager_BreakpointsChanged;

			this.UpdateWorkspace();
			this.AutoLoadDbgFile(true);

			this.mnuSplitView.Checked = ConfigManager.Config.DebugInfo.SplitView;
			this.mnuPpuPartialDraw.Checked = ConfigManager.Config.DebugInfo.PpuPartialDraw;
			this.mnuShowEffectiveAddresses.Checked = ConfigManager.Config.DebugInfo.ShowEffectiveAddresses;
			this.mnuShowCpuMemoryMapping.Checked = ConfigManager.Config.DebugInfo.ShowCpuMemoryMapping;
			this.mnuShowPpuMemoryMapping.Checked = ConfigManager.Config.DebugInfo.ShowPpuMemoryMapping;
			this.mnuShowOnlyDisassembledCode.Checked = ConfigManager.Config.DebugInfo.ShowOnlyDisassembledCode;
			this.mnuShowFunctionLabelLists.Checked = ConfigManager.Config.DebugInfo.ShowFunctionLabelLists;
			this.mnuHighlightUnexecutedCode.Checked = ConfigManager.Config.DebugInfo.HighlightUnexecutedCode;
			this.mnuAutoLoadDbgFiles.Checked = ConfigManager.Config.DebugInfo.AutoLoadDbgFiles;

			this.Width = ConfigManager.Config.DebugInfo.WindowWidth;
			this.Height = ConfigManager.Config.DebugInfo.WindowHeight;
			if(ConfigManager.Config.DebugInfo.BottomPanelHeight > 0) {
				this.splitContainer.SplitterDistance = ConfigManager.Config.DebugInfo.BottomPanelHeight;
			}

			_lastCodeWindow = ctrlDebuggerCode;

			this.ctrlDebuggerCode.SetConfig(ConfigManager.Config.DebugInfo.LeftView);
			this.ctrlDebuggerCodeSplit.SetConfig(ConfigManager.Config.DebugInfo.RightView);

			this.toolTip.SetToolTip(this.picWatchHelp,
				"Most expressions/operators are accepted (C++ syntax)." + Environment.NewLine +
				"Note: Use the $ prefix to denote hexadecimal values." + Environment.NewLine + 
				"Note 2: Labels assigned to the code can be used (their value will match the label's address in CPU memory)." + Environment.NewLine + Environment.NewLine +
				"A/X/Y/PS/SP: Value of registers" + Environment.NewLine +
				"Irq/Nmi: True if the Irq/Nmi flags are set" + Environment.NewLine +
				"Cycle/Scanline: Current cycle (0-340)/scanline(-1 to 260) of the PPU" + Environment.NewLine +
				"Value: Current value being read/written from/to memory" + Environment.NewLine +
				"[<address>]: (Byte) Memory value at <address> (CPU)" + Environment.NewLine +
				"{<address>}: (Word) Memory value at <address> (CPU)" + Environment.NewLine + Environment.NewLine +

				"Examples:" + Environment.NewLine +
				"a == 10 || x == $23" + Environment.NewLine +
				"scanline == 10 && (cycle >= 55 && cycle <= 100)" + Environment.NewLine +
				"x == [$150] || y == [10]" + Environment.NewLine +
				"[[$15] + y]   -> Reads the value at address $15, adds Y to it and reads the value at the resulting address." +
				"{$FFFA}  -> Returns the NMI handler's address."
			);

			_notifListener = new InteropEmu.NotificationListener();
			_notifListener.OnNotification += _notifListener_OnNotification;

			InteropEmu.DebugInitialize();

			//Pause a few frames later to give the debugger a chance to disassemble some code
			_debuggerInitialized = true;
			InteropEmu.DebugStep(30000);

			UpdateCdlRatios();
			tmrCdlRatios.Start();
		}

		private void AutoLoadDbgFile(bool silent)
		{
			if(ConfigManager.Config.DebugInfo.AutoLoadDbgFiles) {
				string dbgPath = Path.Combine(Path.GetDirectoryName(ConfigManager.Config.RecentFiles[0].Path), Path.GetFileNameWithoutExtension(ConfigManager.Config.RecentFiles[0].RomName) + ".dbg");
				if(File.Exists(dbgPath)) {
					Ld65DbgImporter dbgImporter = new Ld65DbgImporter();
					dbgImporter.Import(dbgPath, silent);
				}
			}
		}

		private void SaveWorkspace()
		{
			if(_workspace != null) {
				_workspace.WatchValues = ctrlWatch.GetWatchValues();
				_workspace.Labels = LabelManager.GetLabels();
				_workspace.Breakpoints = BreakpointManager.Breakpoints;
				_workspace.Save();
			}
		}

		private void UpdateWorkspace()
		{
			SaveWorkspace();
			
			_workspace = DebugWorkspace.GetWorkspace();

			LabelManager.OnLabelUpdated -= LabelManager_OnLabelUpdated;
			if(_workspace.Labels.Count == 0) {
				LabelManager.ResetLabels();
				LabelManager.SetDefaultLabels(InteropEmu.FdsGetSideCount() > 0);
			} else {
				LabelManager.ResetLabels();
				LabelManager.SetLabels(_workspace.Labels);
			}
			LabelManager.OnLabelUpdated += LabelManager_OnLabelUpdated;

			ctrlLabelList.UpdateLabelList();
			ctrlFunctionList.UpdateFunctionList(true);

			ctrlWatch.SetWatchValues(_workspace.WatchValues);

			BreakpointManager.Breakpoints.Clear();
			BreakpointManager.Breakpoints.AddRange(_workspace.Breakpoints);
			ctrlBreakpoints.RefreshList();
		}

		private void UpdateCdlRatios()
		{
			CdlRatios ratios = new CdlRatios();
			InteropEmu.DebugGetCdlRatios(ref ratios);

			lblPrgAnalysisResult.Text = string.Format("{0:0.00}% (Code: {1:0.00}%, Data: {2:0.00}%, Unknown: {3:0.00}%)", ratios.PrgRatio * 100, ratios.CodeRatio * 100, ratios.DataRatio * 100, (1 - ratios.PrgRatio) * 100);
			if(ratios.ChrRatio >= 0) {
				lblChrAnalysisResult.Text = string.Format("{0:0.00}% (Drawn: {1:0.00}%, Read: {2:0.00}%, Unknown: {3:0.00}%)", ratios.ChrRatio * 100, ratios.ChrDrawnRatio * 100, ratios.ChrReadRatio * 100, (1 - ratios.ChrRatio) * 100);
			} else {
				lblChrAnalysisResult.Text = "N/A (CHR RAM)";
			}
		}

		private void UpdateDebuggerFlags()
		{
			DebuggerFlags flags = mnuPpuPartialDraw.Checked ? DebuggerFlags.PpuPartialDraw : DebuggerFlags.None;
			if(mnuShowEffectiveAddresses.Checked) {
				flags |= DebuggerFlags.ShowEffectiveAddresses;
			}
			if(mnuShowOnlyDisassembledCode.Checked) {
				flags |= DebuggerFlags.ShowOnlyDisassembledCode;
			}
			InteropEmu.DebugSetFlags(flags);
		}

		private void _notifListener_OnNotification(InteropEmu.NotificationEventArgs e)
		{
			switch(e.NotificationType) {
				case InteropEmu.ConsoleNotificationType.CodeBreak:
					this.BeginInvoke((MethodInvoker)(() => UpdateDebugger()));
					BreakpointManager.SetBreakpoints();
					break;

				case InteropEmu.ConsoleNotificationType.GameReset:
				case InteropEmu.ConsoleNotificationType.GameLoaded:
					this.BeginInvoke((MethodInvoker)(() => {
						this.UpdateWorkspace();
						this.AutoLoadDbgFile(true);
						UpdateDebugger();
						BreakpointManager.SetBreakpoints();
					}));					
					InteropEmu.DebugStep(1);
					break;
			}
		}

		private bool UpdateSplitView()
		{
			if(mnuSplitView.Checked) {
				tlpTop.ColumnStyles[1].SizeType = SizeType.Percent;
				tlpTop.ColumnStyles[0].Width = 50f;
				tlpTop.ColumnStyles[1].Width = 50f;
				this.MinimumSize = new Size(1250, 700);
			} else {
				tlpTop.ColumnStyles[1].SizeType = SizeType.Absolute;
				tlpTop.ColumnStyles[1].Width = 0f;
				this.MinimumSize = new Size(1000, 700);
			}
			ctrlDebuggerCodeSplit.Visible = mnuSplitView.Checked;
			return mnuSplitView.Checked;
		}

		private void UpdateVectorAddresses()
		{
			int nmiHandler = InteropEmu.DebugGetMemoryValue(0xFFFA) | (InteropEmu.DebugGetMemoryValue(0xFFFB) << 8);
			int resetHandler = InteropEmu.DebugGetMemoryValue(0xFFFC) | (InteropEmu.DebugGetMemoryValue(0xFFFD) << 8);
			int irqHandler = InteropEmu.DebugGetMemoryValue(0xFFFE) | (InteropEmu.DebugGetMemoryValue(0xFFFF) << 8);

			mnuGoToNmiHandler.Text = "NMI Handler ($" + nmiHandler.ToString("X4") + ")";
			mnuGoToResetHandler.Text = "Reset Handler ($" + resetHandler.ToString("X4") + ")";
			mnuGoToIrqHandler.Text = "IRQ Handler ($" + irqHandler.ToString("X4") + ")";
		}

		string _previousCode = string.Empty;
		private void UpdateDebugger(bool updateActiveAddress = true)
		{
			if(!_debuggerInitialized) {
				return;
			}

			ctrlLabelList.UpdateLabelListAddresses();
			ctrlFunctionList.UpdateFunctionList(false);
			UpdateDebuggerFlags();
			UpdateVectorAddresses();

			if(InteropEmu.DebugIsCodeChanged()) {
				_previousCode = InteropEmu.DebugGetCode();
				ctrlDebuggerCode.Code = _previousCode;
			}

			DebugState state = new DebugState();
			InteropEmu.DebugGetState(ref state);

			if(UpdateSplitView()) {
				ctrlDebuggerCodeSplit.Code = _previousCode;
				ctrlDebuggerCodeSplit.UpdateCode(true);
			} else {
				_lastCodeWindow = ctrlDebuggerCode;
			}

			ctrlDebuggerCode.SetActiveAddress(state.CPU.DebugPC);
			ctrlDebuggerCodeSplit.SetActiveAddress(state.CPU.DebugPC);

			if(updateActiveAddress) {
				_lastCodeWindow.SelectActiveAddress(state.CPU.DebugPC);
			}

			UpdateLineColors();

			ctrlConsoleStatus.UpdateStatus(ref state);
			ctrlWatch.UpdateWatch();
			ctrlCallstack.UpdateCallstack();

			ctrlCpuMemoryMapping.UpdateCpuRegions(state.Cartridge);
			ctrlPpuMemoryMapping.UpdatePpuRegions(state.Cartridge);

			this.BringToFront();
		}

		private void ClearActiveStatement()
		{
			ctrlDebuggerCode.ClearActiveAddress();
			ctrlDebuggerCodeSplit.ClearActiveAddress();
			UpdateLineColors();
		}

		private void ToggleBreakpoint(bool toggleEnabled)
		{
			BreakpointManager.ToggleBreakpoint(_lastCodeWindow.GetCurrentLine(), toggleEnabled);
		}
		
		private void UpdateLineColors()
		{
			ctrlDebuggerCodeSplit.UpdateLineColors();
			ctrlDebuggerCode.UpdateLineColors();
		}

		private void OpenChildForm(Form frm)
		{
			this._childForms.Add(frm);
			frm.FormClosed += (obj, args) => {
				this._childForms.Remove((Form)obj);
			};
			frm.Show();
		}

		private void mnuContinue_Click(object sender, EventArgs e)
		{
			ClearActiveStatement();
			InteropEmu.DebugRun();
		}

		private void frmDebugger_FormClosed(object sender, FormClosedEventArgs e)
		{
			InteropEmu.DebugRelease();
		}

		private void mnuToggleBreakpoint_Click(object sender, EventArgs e)
		{
			ToggleBreakpoint(false);
		}

		private void mnuDisableEnableBreakpoint_Click(object sender, EventArgs e)
		{
			ToggleBreakpoint(true);
		}

		private void mnuBreak_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugStep(1);
		}

		private void mnuStepInto_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugStep(1);
		}

		private void mnuStepOut_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugStepOut();
		}
		
		private void mnuStepOver_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugStepOver();
		}


		private void mnuRunPpuCycle_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugPpuStep(1);
		}

		private void mnuRunScanline_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugPpuStep(341);
		}

		private void mnuRunOneFrame_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugPpuStep(89341);
		}

		private void ctrlDebuggerCode_OnWatchAdded(WatchEventArgs args)
		{
			this.ctrlWatch.AddWatch(args.WatchValue);
		}

		private void ctrlDebuggerCode_OnSetNextStatement(AddressEventArgs args)
		{
			UInt16 addr = (UInt16)args.Address;
			InteropEmu.DebugSetNextStatement(addr);
			this.UpdateDebugger();
		}

		private void mnuFind_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.OpenSearchBox();
		}
		
		private void mnuFindNext_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.FindNext();
		}

		private void mnuFindPrev_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.FindPrevious();
		}

		private void mnuSplitView_Click(object sender, EventArgs e)
		{
			ConfigManager.Config.DebugInfo.SplitView = this.mnuSplitView.Checked;
			ConfigManager.ApplyChanges();

			UpdateDebugger(false);
		}

		private void mnuMemoryViewer_Click(object sender, EventArgs e)
		{
			OpenChildForm(new frmMemoryViewer());
		}

		private void BreakpointManager_BreakpointsChanged(object sender, EventArgs e)
		{
			UpdateLineColors();
		}

		private void ctrlDebuggerCode_Enter(object sender, EventArgs e)
		{
			_lastCodeWindow = ctrlDebuggerCode;
		}

		private void ctrlDebuggerCodeSplit_Enter(object sender, EventArgs e)
		{
			_lastCodeWindow = ctrlDebuggerCodeSplit;
		}

		private void mnuGoToAddress_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.GoToAddress();
		}

		private void mnuGoToIrqHandler_Click(object sender, EventArgs e)
		{
			int address = (InteropEmu.DebugGetMemoryValue(0xFFFF) << 8) | InteropEmu.DebugGetMemoryValue(0xFFFE);
			_lastCodeWindow.ScrollToLineNumber(address);
		}

		private void mnuGoToNmiHandler_Click(object sender, EventArgs e)
		{
			int address = (InteropEmu.DebugGetMemoryValue(0xFFFB) << 8) | InteropEmu.DebugGetMemoryValue(0xFFFA);
			_lastCodeWindow.ScrollToLineNumber(address);
		}

		private void mnuGoToResetHandler_Click(object sender, EventArgs e)
		{
			int address = (InteropEmu.DebugGetMemoryValue(0xFFFD) << 8) | InteropEmu.DebugGetMemoryValue(0xFFFC);
			_lastCodeWindow.ScrollToLineNumber(address);
		}

		private void mnuIncreaseFontSize_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.FontSize++;
		}

		private void mnuDecreaseFontSize_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.FontSize--;
		}

		private void mnuResetFontSize_Click(object sender, EventArgs e)
		{
			_lastCodeWindow.FontSize = 13;
		}

		private void mnuClose_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			ConfigManager.Config.DebugInfo.WindowWidth = this.Width;
			ConfigManager.Config.DebugInfo.WindowHeight = this.Height;
			ConfigManager.Config.DebugInfo.BottomPanelHeight = this.splitContainer.SplitterDistance;
			ConfigManager.ApplyChanges();

			SaveWorkspace();

			foreach(Form frm in this._childForms.ToArray()) {
				frm.Close();
			}

			base.OnFormClosed(e);
		}

		private void mnuNametableViewer_Click(object sender, EventArgs e)
		{
			OpenChildForm(new frmPpuViewer());
		}

		private void ctrlCallstack_FunctionSelected(object sender, EventArgs e)
		{
			_lastCodeWindow.ScrollToLineNumber((int)sender);
		}

		private void ctrlConsoleStatus_OnStateChanged(object sender, EventArgs e)
		{
			UpdateDebugger(true);
		}

		private void tmrCdlRatios_Tick(object sender, EventArgs e)
		{
			this.UpdateCdlRatios();
		}

		private void mnuLoadCdlFile_Click(object sender, EventArgs e)
		{
			OpenFileDialog ofd = new OpenFileDialog();
			ofd.Filter = "CDL files (*.cdl)|*.cdl";
			if(ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
				if(!InteropEmu.DebugLoadCdlFile(ofd.FileName)) {
					MessageBox.Show("Could not load CDL file.  The file selected file is invalid.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void mnuSaveAsCdlFile_Click(object sender, EventArgs e)
		{
			SaveFileDialog sfd = new SaveFileDialog();
			sfd.Filter = "CDL files (*.cdl)|*.cdl";
			sfd.AddExtension = true;
			if(sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
				if(!InteropEmu.DebugSaveCdlFile(sfd.FileName)) {
					MessageBox.Show("Error while trying to save CDL file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void mnuResetCdlLog_Click(object sender, EventArgs e)
		{
			InteropEmu.DebugResetCdlLog();
		}

		private void ctrlBreakpoints_BreakpointNavigation(object sender, EventArgs e)
		{
			_lastCodeWindow.ScrollToLineNumber((int)((Breakpoint)sender).Address);
		}

		private void mnuTraceLogger_Click(object sender, EventArgs e)
		{
			if(_traceLogger == null) {
				_traceLogger = new frmTraceLogger();
				_traceLogger.FormClosed += (s, evt) => {
					_traceLogger = null;
				};
				_traceLogger.Show(sender, this);
			} else {
				_traceLogger.Focus();
			}
		}

		private void mnuPpuPartialDraw_Click(object sender, EventArgs e)
		{
			ConfigManager.Config.DebugInfo.PpuPartialDraw = mnuPpuPartialDraw.Checked;
			ConfigManager.ApplyChanges();
		}
		
		private void mnuShowEffectiveAddresses_CheckedChanged(object sender, EventArgs e)
		{
			ConfigManager.Config.DebugInfo.ShowEffectiveAddresses = mnuShowEffectiveAddresses.Checked;
			ConfigManager.ApplyChanges();
			UpdateDebugger(false);
		}

		private void mnuShowOnlyDisassembledCode_CheckedChanged(object sender, EventArgs e)
		{
			ConfigManager.Config.DebugInfo.ShowOnlyDisassembledCode = mnuShowOnlyDisassembledCode.Checked;
			ConfigManager.ApplyChanges();
			UpdateDebugger(false);
		}

		private void mnuShowCpuMemoryMapping_CheckedChanged(object sender, EventArgs e)
		{
			ctrlCpuMemoryMapping.Visible = mnuShowCpuMemoryMapping.Checked;
			ConfigManager.Config.DebugInfo.ShowCpuMemoryMapping = mnuShowCpuMemoryMapping.Checked;
			ConfigManager.ApplyChanges();
			ctrlCpuMemoryMapping.Invalidate();
			ctrlPpuMemoryMapping.Invalidate();
		}

		private void mnuShowPpuMemoryMapping_CheckedChanged(object sender, EventArgs e)
		{
			ctrlPpuMemoryMapping.Visible = mnuShowPpuMemoryMapping.Checked;
			ConfigManager.Config.DebugInfo.ShowPpuMemoryMapping = mnuShowPpuMemoryMapping.Checked;
			ConfigManager.ApplyChanges();
			ctrlCpuMemoryMapping.Invalidate();
			ctrlPpuMemoryMapping.Invalidate();
		}

		private void mnuShowFunctionLabelLists_CheckedChanged(object sender, EventArgs e)
		{
			tlpFunctionLabelLists.Visible = mnuShowFunctionLabelLists.Checked;
			ConfigManager.Config.DebugInfo.ShowFunctionLabelLists = mnuShowFunctionLabelLists.Checked;
			ConfigManager.ApplyChanges();
		}

		private void mnuHighlightUnexecutedCode_Click(object sender, EventArgs e)
		{
			ConfigManager.Config.DebugInfo.HighlightUnexecutedCode = mnuHighlightUnexecutedCode.Checked;
			ConfigManager.ApplyChanges();
			ctrlDebuggerCode.UpdateLineColors();
			ctrlDebuggerCodeSplit.UpdateLineColors();
		}

		private void frmDebugger_Resize(object sender, EventArgs e)
		{
			ctrlCpuMemoryMapping.Invalidate();
			ctrlPpuMemoryMapping.Invalidate();
		}

		private void ctrlFunctionList_OnFunctionSelected(object relativeAddress, EventArgs e)
		{
			_lastCodeWindow.ScrollToLineNumber((Int32)relativeAddress);
		}

		private void LabelManager_OnLabelUpdated(object sender, EventArgs e)
		{
			SaveWorkspace();
			ctrlLabelList.UpdateLabelList();
			ctrlFunctionList.UpdateFunctionList(true);
			UpdateDebugger(false);
		}

		private void ctrlLabelList_OnLabelSelected(object relativeAddress, EventArgs e)
		{
			_lastCodeWindow.ScrollToLineNumber((Int32)relativeAddress);
		}

		private void mnuResetWorkspace_Click(object sender, EventArgs e)
		{
			if(MessageBox.Show("This operation will empty the watch window, remove all breakpoints, and reset labels to their default state." + Environment.NewLine + "Are you sure?", "", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK) {
				_workspace.Breakpoints = new List<Breakpoint>();
				_workspace.Labels = new List<CodeLabel>();
				_workspace.WatchValues = new List<string>();
				_workspace.Save();
				_workspace = null;
				LabelManager.ResetLabels();
				UpdateWorkspace();
				UpdateDebugger();
			}
		}

		private void mnuImportLabels_Click(object sender, EventArgs e)
		{
			OpenFileDialog ofd = new OpenFileDialog();
			ofd.Filter = "All supported files (*.dbg)|*.dbg";
			if(ofd.ShowDialog() == DialogResult.OK) {
				Ld65DbgImporter dbgImporter = new Ld65DbgImporter();
				dbgImporter.Import(ofd.FileName);
			}
		}

		private void ctrlLabelList_OnFindOccurrence(object sender, EventArgs e)
		{
			CodeLabel label = sender as CodeLabel;
			_lastCodeWindow.FindAllOccurrences(label.Label, true, true);
		}

		private void ctrlFunctionList_OnFindOccurrence(object sender, EventArgs e)
		{
			_lastCodeWindow.FindAllOccurrences(sender as string, true, true);
		}

		private void mnuBreakIn_Click(object sender, EventArgs e)
		{
			using(frmBreakIn frm = new frmBreakIn()) {
				frm.ShowDialog();
			}
		}

		private void mnuFindAllOccurrences_Click(object sender, EventArgs e)
		{
			frmFindOccurrences frm = new Debugger.frmFindOccurrences();
			if(frm.ShowDialog() == DialogResult.OK) {
				_lastCodeWindow.FindAllOccurrences(frm.SearchString, frm.MatchWholeWord, frm.MatchCase);
			}
		}

		private void mnuAutoLoadDbgFiles_CheckedChanged(object sender, EventArgs e)
		{
			if(_debuggerInitialized) {
				ConfigManager.Config.DebugInfo.AutoLoadDbgFiles = mnuAutoLoadDbgFiles.Checked;
				ConfigManager.ApplyChanges();

				AutoLoadDbgFile(false);
			}
		}
	}
}
