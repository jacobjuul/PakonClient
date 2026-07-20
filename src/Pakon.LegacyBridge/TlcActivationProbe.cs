using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Pakon.LegacyBridge
{
    internal static class TlcActivationProbe
    {
        private static readonly Guid TlcMainClassId = new Guid("6449DE65-60A9-4A45-A3A1-337F5E6B41E0");

        public static IDictionary<string, string> ActivateAndRelease()
        {
            object instance = null;
            try
            {
                instance = Activator.CreateInstance(Type.GetTypeFromCLSID(TlcMainClassId, true));
                return new Dictionary<string, string>
                {
                    { "clsid", TlcMainClassId.ToString("D") },
                    { "runtimeType", instance.GetType().FullName },
                    { "isComObject", Marshal.IsComObject(instance).ToString() }
                };
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance))
                {
                    Marshal.FinalReleaseComObject(instance);
                }
            }
        }
    }
}
