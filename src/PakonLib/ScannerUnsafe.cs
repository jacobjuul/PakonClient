// Pakon.ScannerUnsafe
using System;
using System.Runtime.InteropServices;
using TLXLib;
using PakonLib.Enums;

namespace PakonLib 
{
    /// <summary>
    /// Manages the caller-owned buffers used by TLX's rendered-image delivery path.
    /// These are post-processing output buffers registered with <see cref="Scanner.Images"/>;
    /// they are not the FX35 driver's raw scan-acquisition ring buffer.
    /// </summary>
    public class ScannerUnsafe
    {
        private const int TlxClientPointerBytes = 4;

        private MemoryFileFormat memoryFormat;

        private bool usingFirstBuffer = true;

        private int bufferSize;

        private IntPtr clientBufferPointer1;

        private unsafe byte* clientBuffer1 = null;

        private int clientBufferAddress1 = 0;

        private IntPtr clientBufferPointer2;

        private unsafe byte* clientBuffer2 = null;

        private int clientBufferAddress2 = 0;

        public MemoryFileFormat MemoryFormat
        {
            get
            {
                return memoryFormat;
            }
            set
            {
                memoryFormat = value;
            }
        }

        public event ImageFromClientBuffer ImageFromClientBufferReceived;

        public unsafe void Allocate(int bufferByteCount)
        {
            // TLX's ClientMemoryBufferAdd ABI receives an x86 pointer value, not a managed handle.
            // This limitation belongs to the legacy COM renderer; a future direct-driver acquisition
            // path will use a separate RING_TAIL allocation and must not reuse these buffers.
            if (IntPtr.Size != TlxClientPointerBytes)
            {
                throw new PlatformNotSupportedException("TLX client memory buffers use 32-bit pointer addresses. Run PakonClient as x86.");
            }

            usingFirstBuffer = true;
            bufferSize = bufferByteCount;
            clientBufferPointer1 = Marshal.AllocHGlobal(bufferByteCount);
            clientBuffer1 = (byte*)(void*)clientBufferPointer1;
            clientBufferAddress1 = checked(clientBufferPointer1.ToInt32());
            clientBufferPointer2 = Marshal.AllocHGlobal(bufferByteCount);
            clientBuffer2 = (byte*)(void*)clientBufferPointer2;
            clientBufferAddress2 = checked(clientBufferPointer2.ToInt32());
        }

        public unsafe void Deallocate()
        {
            if (clientBuffer1 != null)
            {
                Marshal.FreeHGlobal(clientBufferPointer1);
            }
            clientBuffer1 = null;
            clientBufferAddress1 = 0;
            if (clientBuffer2 != null)
            {
                Marshal.FreeHGlobal(clientBufferPointer2);
            }
            clientBuffer2 = null;
            clientBufferAddress2 = 0;
        }

        public unsafe void NextBuffer(Scanner scanner)
        {
            if (clientBuffer1 == null)
            {
                return;
            }
            if (usingFirstBuffer)
            {
                byte* buffer = clientBuffer1;
                int index = 0;
                while (index < bufferSize)
                {
                    *buffer = 0;
                    index++;
                    buffer++;
                }
                scanner.Images.RegisterRenderBuffer(clientBufferAddress1, bufferSize);
            }
            else
            {
                byte* buffer = clientBuffer2;
                int index = 0;
                while (index < bufferSize)
                {
                    *buffer = 0;
                    index++;
                    buffer++;
                }
                scanner.Images.RegisterRenderBuffer(clientBufferAddress2, bufferSize);
            }
            usingFirstBuffer = !usingFirstBuffer;
        }

        public unsafe void ProcessBuffer(Scanner scanner)
        {
            if (clientBuffer1 != null)
            {
                // At this point TLX has already completed its staging, framing, and requested
                // render pipeline. The SiPlanar header describes renderer output, not scanner
                // packets or the intermediate PFS raw stream.
                byte* buffer = usingFirstBuffer ? clientBuffer1 : clientBuffer2;
                uint bytesToCopy = 0u;

                switch (memoryFormat.NativeValue)
                {
                    case FILE_FORMAT_SAVE_TO_MEMORY_000.iFILE_FORMAT_SAVE_TO_MEMORY_PLANAR_16:
                    {
                        SiPlanarFileHeader* header = (SiPlanarFileHeader*)buffer;
                        if (header->Size < sizeof(SiPlanarFileHeader) || header->Width == 0 || header->Height == 0 || header->BitCount == 0)
                        {
                            throw new InvalidOperationException("Invalid planar raw header: size=" + header->Size + ", width=" + header->Width + ", height=" + header->Height + ", bitCount=" + header->BitCount + ".");
                        }

                        var expectedByteCount = (ulong)header->Width * header->Height * (header->BitCount / 8u) + (uint)sizeof(SiPlanarFileHeader);
                        if (expectedByteCount > (ulong)bufferSize)
                        {
                            throw new InvalidOperationException("Planar raw header reports " + expectedByteCount + " bytes, exceeding the " + bufferSize + " byte client buffer.");
                        }

                        bytesToCopy = (uint)expectedByteCount;
                        break;
                    }
                    default:
                        throw new ArgumentException("Format not supported");
                    case FILE_FORMAT_SAVE_TO_MEMORY_000.iFILE_FORMAT_SAVE_TO_MEMORY_PLANAR_8:
                    case FILE_FORMAT_SAVE_TO_MEMORY_000.iFILE_FORMAT_SAVE_TO_MEMORY_DIB_8:
                        break;
                }

                if (bytesToCopy == 0)
                {
                    throw new ArgumentException("Format not implemented");
                }
                byte[] data = new byte[bytesToCopy];
                byte* source = buffer;
                uint index = 0u;
                while (index < bytesToCopy)
                {
                    data[index] = *source;
                    index++;
                    source++;
                }
                if (ImageFromClientBufferReceived != null)
                {
                    ImageFromClientBufferReceived(data, bytesToCopy);
                }
            }
        }
    }

}

