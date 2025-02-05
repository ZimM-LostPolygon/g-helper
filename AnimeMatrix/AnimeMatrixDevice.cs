﻿// Source thanks to https://github.com/vddCore/Starlight with some adjustments from me

using Starlight.Communication;
using System.Diagnostics;
using System.Text;
using System.Management;

namespace Starlight.AnimeMatrix
{
    public class BuiltInAnimation
    {
        public enum Startup
        {
            GlitchConstruction,
            StaticEmergence
        }

        public enum Shutdown
        {
            GlitchOut,
            SeeYa
        }

        public enum Sleeping
        {
            BannerSwipe,
            Starfield
        }

        public enum Running
        {
            BinaryBannerScroll,
            RogLogoGlitch
        }

        public byte AsByte { get; }

        public BuiltInAnimation(
            Running running,
            Sleeping sleeping,
            Shutdown shutdown,
            Startup startup
        )
        {
            AsByte |= (byte)(((int)running & 0x01) << 0);
            AsByte |= (byte)(((int)sleeping & 0x01) << 1);
            AsByte |= (byte)(((int)shutdown & 0x01) << 2);
            AsByte |= (byte)(((int)startup & 0x01) << 3);
        }
    }

    internal class AnimeMatrixPacket : Packet
    {
        public AnimeMatrixPacket(byte[] command)
            : base(0x5E, 640, command)
        {
        }
    }

    public enum BrightnessMode : byte
    {
        Off = 0,
        Dim = 1,
        Medium = 2,
        Full = 3
    }


    public class AnimeMatrixDevice : Device
    {
        private const int UpdatePageLength = 0x0278;

        public int LedCount => 1450;

        private byte[] _displayBuffer = new byte[UpdatePageLength * 3];
        private List<byte[]> frames = new List<byte[]>();

        private int pages = 3;

        public int MaxColumns = 34;
        public int MaxRows = 61;

        public int FullRows = 11;

        private int frameIndex = 0;

        public AnimeMatrixDevice()
            : base(0x0B05, 0x193B, 640)
        {
            string model = GetModel();
            Debug.WriteLine(model);
            if (model is not null && model.Contains("401"))
            {
                pages = 2;

                FullRows = 6;
                MaxColumns = 33;
                MaxRows = 55;
            }
        }


        public string GetModel()
        {
            using (var searcher = new ManagementObjectSearcher(@"Select * from Win32_ComputerSystem"))
            {
                foreach (var process in searcher.Get())
                    return process["Model"].ToString();
            }

            return null;

        }

        public byte[] GetBuffer()
        {
            return _displayBuffer;
        }

        public void PresentNextFrame()
        {
            //Debug.WriteLine(frameIndex);
            if (frameIndex >= frames.Count) frameIndex = 0;
            _displayBuffer = frames[frameIndex];
            Present();
            frameIndex++;
        }

        public void ClearFrames()
        {
            frames.Clear();
            frameIndex = 0;
        }

        public void AddFrame()
        {
            frames.Add(_displayBuffer.ToArray());
        }

        public void SendRaw(params byte[] data)
        {
            Set(Packet<AnimeMatrixPacket>(data));
        }


        public int EmptyColumns(int row)
        {
            return (int)Math.Ceiling(Math.Max(0, row - FullRows) / 2.0);
        }
        public int Columns(int row)
        {
            EnsureRowInRange(row);
            return MaxColumns - EmptyColumns(row);
        }

        public int RowToLinearAddress(int row)
        {
            EnsureRowInRange(row);

            var ret = 0;

            if (row > 0)
            {
                for (var i = 0; i < row; i++)
                    ret += Columns(i);
            }

            return ret;
        }

        public void WakeUp()
        {
            Set(Packet<AnimeMatrixPacket>(Encoding.ASCII.GetBytes("ASUS Tech.Inc.")));
        }

        public void SetLedLinear(int address, byte value)
        {
            EnsureAddressableLed(address);
            _displayBuffer[address] = value;
        }

        public void SetLedLinearImmediate(int address, byte value)
        {
            EnsureAddressableLed(address);
            _displayBuffer[address] = value;

            Set(Packet<AnimeMatrixPacket>(0xC0, 0x02)
                .AppendData(BitConverter.GetBytes((ushort)(address + 1)))
                .AppendData(BitConverter.GetBytes((ushort)0x0001))
                .AppendData(value)
            );

            Set(Packet<AnimeMatrixPacket>(0xC0, 0x03));
        }

        public void SetLedPlanar(int x, int y, byte value)
        {
            EnsureRowInRange(y);
            var start = RowToLinearAddress(y) - EmptyColumns(y);

            if (x > EmptyColumns(y))
                SetLedLinear(start + x, value);
        }

        public void Clear(bool present = false)
        {
            for (var i = 0; i < _displayBuffer.Length; i++)
                _displayBuffer[i] = 0;

            if (present)
                Present();
        }

        public void Present()
        {

            Set(Packet<AnimeMatrixPacket>(0xC0, 0x02)
                .AppendData(BitConverter.GetBytes((ushort)(UpdatePageLength * 0 + 1)))
                .AppendData(BitConverter.GetBytes((ushort)UpdatePageLength))
                .AppendData(_displayBuffer[(UpdatePageLength * 0)..(UpdatePageLength * 1)])
            );

            Set(Packet<AnimeMatrixPacket>(0xC0, 0x02)
                .AppendData(BitConverter.GetBytes((ushort)(UpdatePageLength * 1 + 1)))
                .AppendData(BitConverter.GetBytes((ushort)UpdatePageLength))
                .AppendData(_displayBuffer[(UpdatePageLength * 1)..(UpdatePageLength * 2)])
            );

            if (pages > 2)
                Set(Packet<AnimeMatrixPacket>(0xC0, 0x02)
                    .AppendData(BitConverter.GetBytes((ushort)(UpdatePageLength * 2 + 1)))
                    .AppendData(BitConverter.GetBytes((ushort)(LedCount - UpdatePageLength * 2)))
                    .AppendData(
                        _displayBuffer[(UpdatePageLength * 2)..(UpdatePageLength * 2 + (LedCount - UpdatePageLength * 2))])
                );

            Set(Packet<AnimeMatrixPacket>(0xC0, 0x03));
        }

        public void SetDisplayState(bool enable)
        {
            if (enable)
            {
                Set(Packet<AnimeMatrixPacket>(0xC3, 0x01)
                    .AppendData(0x00));
            }
            else
            {
                Set(Packet<AnimeMatrixPacket>(0xC3, 0x01)
                    .AppendData(0x80));
            }
        }

        public void SetBrightness(BrightnessMode mode)
        {
            Set(Packet<AnimeMatrixPacket>(0xC0, 0x04)
                .AppendData((byte)mode)
            );
        }

        public void SetBuiltInAnimation(bool enable)
        {
            var enabled = enable ? (byte)0x00 : (byte)0x80;
            Set(Packet<AnimeMatrixPacket>(0xC4, 0x01, enabled));
        }

        public void SetBuiltInAnimation(bool enable, BuiltInAnimation animation)
        {
            SetBuiltInAnimation(enable);
            Set(Packet<AnimeMatrixPacket>(0xC5, animation.AsByte));
        }

        public void GenerateFrame(Image image)
        {

            int width = MaxColumns * 3;
            int height = MaxRows;
            float scale;

            Bitmap canvas = new Bitmap(width, height);

            scale = Math.Min((float)width / (float)image.Width, (float)height / (float)image.Height);

            var graph = Graphics.FromImage(canvas);
            var scaleWidth = (int)(image.Width * scale);
            var scaleHeight = (int)(image.Height * scale);

            graph.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
            graph.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graph.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            graph.DrawImage(image, ((int)width - scaleWidth), ((int)height - scaleHeight) / 2, scaleWidth, scaleHeight);

            Bitmap bmp = new Bitmap(canvas, MaxColumns, MaxRows);

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var pixel = bmp.GetPixel(x, y);
                    byte color = (byte)(Math.Max((pixel.R + pixel.G + pixel.B) / 3 - 10, 0));
                    SetLedPlanar(x, y, color);
                }
            }

        }

        private void EnsureRowInRange(int row)
        {
            if (row < 0 || row >= MaxRows)
            {
                throw new IndexOutOfRangeException($"Y-coordinate should fall in range of [0, {MaxRows - 1}].");
            }
        }

        private void EnsureAddressableLed(int address)
        {
            if (address < 0 || address >= LedCount)
            {
                throw new IndexOutOfRangeException($"Linear LED address must be in range of [0, {LedCount - 1}].");
            }
        }
    }
}