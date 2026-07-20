using System;
using System.Runtime.InteropServices;

namespace PakonTransportProbe
{
    internal static class TlcComProbe
    {
        // TLC.TLAMain.1, registered by TLC.dll. This bypasses tlx.dll's
        // PakonX35 backend-selection facade, but deliberately invokes no TLC method.
        private static readonly Guid TlcMainClassId = new Guid("6449DE65-60A9-4A45-A3A1-337F5E6B41E0");

        public static bool CreateAndRelease(ProbeLog log)
        {
            object instance = null;
            try
            {
                log.Write("TLC direct COM activation: CLSID={0}", TlcMainClassId);
                var type = Type.GetTypeFromCLSID(TlcMainClassId, true);
                instance = Activator.CreateInstance(type);
                log.Write("TLC direct COM activation succeeded: runtimeType={0}; isComObject={1}", instance.GetType().FullName, Marshal.IsComObject(instance));
                log.Write("No TLC interface was queried and InitializeScanner was not called.");
                return true;
            }
            catch (Exception exception)
            {
                log.Write("TLC direct COM activation failed: {0}: {1}", exception.GetType().FullName, exception.Message);
                return false;
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance))
                {
                    Marshal.FinalReleaseComObject(instance);
                    log.Write("TLC direct COM object released.");
                }
            }
        }
    }
}
