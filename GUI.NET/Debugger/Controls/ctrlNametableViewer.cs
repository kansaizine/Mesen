﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Mesen.GUI.Config;

namespace Mesen.GUI.Debugger.Controls
{
	public partial class ctrlNametableViewer : UserControl
	{
		private byte[][] _nametablePixelData = new byte[4][];
		private byte[][] _tileData = new byte[4][];
		private byte[][] _attributeData = new byte[4][];

		public ctrlNametableViewer()
		{
			InitializeComponent();

			bool designMode = (LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			if(!designMode) {
				chkShowPpuScrollOverlay.Checked = ConfigManager.Config.DebugInfo.ShowPpuScrollOverlay;
				chkShowPpuScrollOverlay.CheckedChanged += this.chkShowScrollWindow_CheckedChanged;
			}
		}

		public void GetData()
		{
			for(int i = 0; i < 4; i++) {
				InteropEmu.DebugGetNametable(i, out _nametablePixelData[i], out _tileData[i], out _attributeData[i]);
			}
		}

		public void RefreshViewer()
		{
			int xScroll, yScroll;
			InteropEmu.DebugGetPpuScroll(out xScroll, out yScroll);

			Bitmap target = new Bitmap(512, 480);
			using(Graphics g = Graphics.FromImage(target)) {
				for(int i = 0; i < 4; i++) {
					GCHandle handle = GCHandle.Alloc(_nametablePixelData[i], GCHandleType.Pinned);
					Bitmap source = new Bitmap(256, 240, 4*256, System.Drawing.Imaging.PixelFormat.Format32bppArgb, handle.AddrOfPinnedObject());
					try {
						g.DrawImage(source, new Rectangle(i % 2 == 0 ? 0 : 256, i <= 1 ? 0 : 240, 256, 240), new Rectangle(0, 0, 256, 240), GraphicsUnit.Pixel);
					} finally {
						handle.Free();
					}
				}

				if(chkShowPpuScrollOverlay.Checked) {
					using(Brush brush = new SolidBrush(Color.FromArgb(75, 100, 180, 215))) {
						g.FillRectangle(brush, xScroll, yScroll, 256, 240);
						if(xScroll + 256 >= 512) {
							g.FillRectangle(brush, 0, yScroll, xScroll - 256, 240);
						}
						if(yScroll + 240 >= 480) {
							g.FillRectangle(brush, xScroll, 0, 256, yScroll - 240);
						}
						if(xScroll + 256 >= 512 && yScroll + 240 >= 480) {
							g.FillRectangle(brush, 0, 0, xScroll - 256, yScroll - 240);
						}
					}
					using(Pen pen = new Pen(Color.FromArgb(230, 150, 150, 150), 2)) {
						g.DrawRectangle(pen, xScroll, yScroll, 256, 240);
						if(xScroll + 256 >= 512) {
							g.DrawRectangle(pen, 0, yScroll, xScroll - 256, 240);
						}
						if(yScroll + 240 >= 480) {
							g.DrawRectangle(pen, xScroll, 0, 256, yScroll - 240);
						}
						if(xScroll + 256 >= 512 && yScroll + 240 >= 480) {
							g.DrawRectangle(pen, 0, 0, xScroll - 256, yScroll - 240);
						}
					}
				}
			}
			this.picNametable.Image = target;
		}

		private void picNametable_MouseMove(object sender, MouseEventArgs e)
		{
			int nametableIndex = 0;
			if(e.X >= 256) {
				nametableIndex++;
			}
			if(e.Y >= 240) {
				nametableIndex+=2;
			}

			int baseAddress = 0x2000 + nametableIndex * 0x400;

			DebugState state = new DebugState();
			InteropEmu.DebugGetState(ref state);
			int bgAddr = state.PPU.ControlFlags.BackgroundPatternAddr;

			int tileX = Math.Min(e.X / 8, 31);
			int tileY = Math.Min(e.Y / 8, 29);
			int shift = (tileX & 0x02) | ((tileY & 0x02) << 1);

			int tileIndex = _tileData[nametableIndex][tileY*32+tileX];
			int attributeData = _attributeData[nametableIndex][tileY*32+tileX];
			int attributeAddr = baseAddress + 960 + ((tileY & 0xFC) << 1) + (tileX >> 2);
			int paletteBaseAddr = ((attributeData >> shift) & 0x03) << 2;

			this.txtTileIndex.Text = tileIndex.ToString("X2");
			this.txtTileAddress.Text = (bgAddr + tileIndex * 16).ToString("X4");
			this.txtAttributeData.Text = attributeData.ToString("X2");
			this.txtAttributeAddress.Text = attributeAddr.ToString("X4");
			this.txtPaletteAddress.Text = (0x3F00 + paletteBaseAddr).ToString("X4");

			Bitmap tile = new Bitmap(64, 64);
			using(Graphics g = Graphics.FromImage(tile)) {
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
				g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
				g.DrawImage(this.picNametable.Image, new Rectangle(0, 0, 64, 64), new Rectangle(e.X/8*8, e.Y/8*8, 8, 8), GraphicsUnit.Pixel);
			}
			this.picTile.Image = tile;
		}

		private void chkShowScrollWindow_CheckedChanged(object sender, EventArgs e)
		{
			ConfigManager.Config.DebugInfo.ShowPpuScrollOverlay = chkShowPpuScrollOverlay.Checked;
			ConfigManager.ApplyChanges();
			this.RefreshViewer();
		}
	}
}
