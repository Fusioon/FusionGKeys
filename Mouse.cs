using System;
using System.Collections.Generic;
using Device.Net;
using Device.Net.Windows;
using Microsoft.Win32.SafeHandles;

namespace FusionGKeys
{
    public enum EMouseModel
    {
        Unknown,
        G903
    }
    class Mouse
    {
        SafeFileHandle _writeHandle;
        SafeFileHandle _readHandle;

        const int k_VendorId = 0x0;
        public string Name { get; }
        public EMouseModel Model { get; }
        bool SendData(byte[] data)
        {
            return false;
        }
        bool ReadData(byte[] data)
        {
            return false;
        }

        private Mouse(string name, EMouseModel model, SafeFileHandle write, SafeFileHandle read)
        {
            Name = name;
            Model = model;
            _writeHandle = write;
            _readHandle = read;
        }

        public static List<Mouse> GetConnected()
        {
            List<Mouse> mice = new List<Mouse>();

            return mice;
        }
    }
}
